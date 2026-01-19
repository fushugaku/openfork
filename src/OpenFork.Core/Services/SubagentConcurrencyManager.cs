using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

/// <summary>
/// Manages concurrency limits for subagent executions.
/// Tracks running instances per agent type and queues requests when limits are reached.
/// </summary>
public class SubagentConcurrencyManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly ConcurrentDictionary<string, int> _runningCounts = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<QueuedExecution>> _queues = new();
    private readonly ILogger<SubagentConcurrencyManager> _logger;

    public SubagentConcurrencyManager(ILogger<SubagentConcurrencyManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Acquires a slot for the given agent type. Blocks if the limit is reached.
    /// </summary>
    /// <param name="agent">The agent to acquire a slot for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A disposable that releases the slot when disposed.</returns>
    public async Task<IDisposable> AcquireSlotAsync(Agent agent, CancellationToken ct)
    {
        var maxConcurrent = agent.MaxConcurrentInstances;

        // 0 means unlimited
        if (maxConcurrent <= 0)
        {
            IncrementRunning(agent.Slug);
            return new SlotRelease(this, agent.Slug);
        }

        var semaphore = _semaphores.GetOrAdd(agent.Slug, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));

        _logger.LogDebug(
            "Agent {Slug} waiting for slot (max={Max}, available={Available})",
            agent.Slug, maxConcurrent, semaphore.CurrentCount);

        await semaphore.WaitAsync(ct);
        IncrementRunning(agent.Slug);

        _logger.LogDebug(
            "Agent {Slug} acquired slot (running={Running})",
            agent.Slug, GetRunningCount(agent.Slug));

        return new SlotRelease(this, agent.Slug);
    }

    /// <summary>
    /// Tries to acquire a slot without blocking.
    /// </summary>
    /// <returns>True if slot was acquired, false if limit reached.</returns>
    public bool TryAcquireSlot(Agent agent, out IDisposable? release)
    {
        release = null;
        var maxConcurrent = agent.MaxConcurrentInstances;

        // 0 means unlimited
        if (maxConcurrent <= 0)
        {
            IncrementRunning(agent.Slug);
            release = new SlotRelease(this, agent.Slug);
            return true;
        }

        var semaphore = _semaphores.GetOrAdd(agent.Slug, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));

        if (semaphore.Wait(0))
        {
            IncrementRunning(agent.Slug);
            release = new SlotRelease(this, agent.Slug);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Enqueues an execution request when the limit is reached.
    /// </summary>
    public void Enqueue(string agentSlug, QueuedExecution execution)
    {
        var queue = _queues.GetOrAdd(agentSlug, _ => new ConcurrentQueue<QueuedExecution>());
        queue.Enqueue(execution);

        _logger.LogInformation(
            "Queued execution for agent {Slug} (queue depth={Depth})",
            agentSlug, queue.Count);
    }

    /// <summary>
    /// Tries to dequeue the next execution for the given agent type.
    /// </summary>
    public bool TryDequeue(string agentSlug, out QueuedExecution? execution)
    {
        execution = null;

        if (_queues.TryGetValue(agentSlug, out var queue))
        {
            return queue.TryDequeue(out execution);
        }

        return false;
    }

    /// <summary>
    /// Gets the current number of running instances for an agent type.
    /// </summary>
    public int GetRunningCount(string agentSlug)
    {
        return _runningCounts.GetValueOrDefault(agentSlug, 0);
    }

    /// <summary>
    /// Gets the current queue depth for an agent type.
    /// </summary>
    public int GetQueueDepth(string agentSlug)
    {
        return _queues.TryGetValue(agentSlug, out var queue) ? queue.Count : 0;
    }

    /// <summary>
    /// Gets status information for all agent types with activity.
    /// </summary>
    public IReadOnlyDictionary<string, AgentConcurrencyStatus> GetStatus()
    {
        var result = new Dictionary<string, AgentConcurrencyStatus>();

        foreach (var slug in _runningCounts.Keys.Union(_queues.Keys).Distinct())
        {
            var maxConcurrent = _semaphores.TryGetValue(slug, out var sem)
                ? sem.CurrentCount + GetRunningCount(slug)
                : 0;

            result[slug] = new AgentConcurrencyStatus
            {
                AgentSlug = slug,
                RunningCount = GetRunningCount(slug),
                QueueDepth = GetQueueDepth(slug),
                MaxConcurrent = maxConcurrent
            };
        }

        return result;
    }

    private void IncrementRunning(string agentSlug)
    {
        _runningCounts.AddOrUpdate(agentSlug, 1, (_, count) => count + 1);
    }

    private void DecrementRunning(string agentSlug)
    {
        _runningCounts.AddOrUpdate(agentSlug, 0, (_, count) => Math.Max(0, count - 1));
    }

    private void ReleaseSlot(string agentSlug)
    {
        DecrementRunning(agentSlug);

        if (_semaphores.TryGetValue(agentSlug, out var semaphore))
        {
            semaphore.Release();

            _logger.LogDebug(
                "Agent {Slug} released slot (running={Running}, available={Available})",
                agentSlug, GetRunningCount(agentSlug), semaphore.CurrentCount);
        }
    }

    private sealed class SlotRelease : IDisposable
    {
        private readonly SubagentConcurrencyManager _manager;
        private readonly string _agentSlug;
        private bool _disposed;

        public SlotRelease(SubagentConcurrencyManager manager, string agentSlug)
        {
            _manager = manager;
            _agentSlug = agentSlug;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manager.ReleaseSlot(_agentSlug);
        }
    }
}

/// <summary>
/// A queued subagent execution request.
/// </summary>
public class QueuedExecution
{
    public required Guid SubSessionId { get; init; }
    public required TaskCompletionSource<SubSession> CompletionSource { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Status information for an agent type's concurrency.
/// </summary>
public record AgentConcurrencyStatus
{
    public required string AgentSlug { get; init; }
    public int RunningCount { get; init; }
    public int QueueDepth { get; init; }
    public int MaxConcurrent { get; init; }
}
