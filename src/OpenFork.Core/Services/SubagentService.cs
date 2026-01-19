using Microsoft.Extensions.Logging;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Agents;
using OpenFork.Core.Domain;
using OpenFork.Core.Events;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for managing subagent executions via the task tool.
/// Includes concurrency control to limit simultaneous instances per agent type.
/// </summary>
public class SubagentService : ISubagentService
{
    private readonly ISubSessionRepository _repository;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IEventBus _eventBus;
    private readonly ILogger<SubagentService> _logger;
    private readonly ChatService _chatService;
    private readonly ISessionRepository _sessionRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly SubagentConcurrencyManager _concurrencyManager;

    public SubagentService(
        ISubSessionRepository repository,
        IAgentRegistry agentRegistry,
        IEventBus eventBus,
        ILogger<SubagentService> logger,
        ChatService chatService,
        ISessionRepository sessionRepository,
        IProjectRepository projectRepository,
        SubagentConcurrencyManager concurrencyManager)
    {
        _repository = repository;
        _agentRegistry = agentRegistry;
        _eventBus = eventBus;
        _logger = logger;
        _chatService = chatService;
        _sessionRepository = sessionRepository;
        _projectRepository = projectRepository;
        _concurrencyManager = concurrencyManager;
    }

    public async Task<SubSession> CreateSubSessionAsync(
        long parentSessionId,
        long? parentMessageId,
        string agentSlug,
        string prompt,
        string? description = null,
        int maxIterations = 10,
        CancellationToken ct = default)
    {
        // Validate agent type exists
        var agent = _agentRegistry.GetBySlug(agentSlug);
        if (agent == null)
        {
            throw new InvalidOperationException($"Unknown agent type: {agentSlug}");
        }

        if (agent.Category != AgentCategory.Subagent)
        {
            throw new InvalidOperationException($"Agent '{agentSlug}' is not a subagent type");
        }

        // For subagents, use the agent's default permissions
        // Parent session permissions don't carry over since subagents run in isolated subsessions
        var effectivePermissions = agent.Permissions;

        var subSession = new SubSession
        {
            Id = Guid.NewGuid(),
            ParentSessionId = parentSessionId,
            ParentMessageId = parentMessageId,
            AgentSlug = agentSlug,
            Prompt = prompt,
            Description = description,
            Status = SubSessionStatus.Pending,
            MaxIterations = Math.Min(maxIterations, agent.MaxIterations),
            EffectivePermissions = effectivePermissions,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateAsync(subSession, ct);

        _logger.LogInformation(
            "Created subsession {Id} of type {AgentSlug} for parent session {ParentSessionId}",
            subSession.Id, agentSlug, parentSessionId);

        // Publish creation event
        await _eventBus.PublishAsync(new SubSessionCreatedEvent
        {
            SubSessionId = subSession.Id,
            ParentSessionId = parentSessionId,
            AgentSlug = agentSlug,
            Description = description
        }, ct);

        return subSession;
    }

    public async Task<SubSession> ExecuteSubSessionAsync(
        Guid subSessionId,
        CancellationToken ct = default)
    {
        var subSession = await _repository.GetByIdAsync(subSessionId, ct)
            ?? throw new InvalidOperationException($"SubSession {subSessionId} not found");

        if (subSession.Status != SubSessionStatus.Pending && subSession.Status != SubSessionStatus.Queued)
        {
            _logger.LogWarning("SubSession {Id} is already {Status}", subSessionId, subSession.Status);
            return subSession;
        }

        // Get the agent configuration
        var agent = _agentRegistry.GetBySlug(subSession.AgentSlug)
            ?? BuiltInAgents.GeneralSubagent;

        // Check concurrency limit - try to acquire slot without blocking first
        if (!_concurrencyManager.TryAcquireSlot(agent, out var slotRelease))
        {
            // Limit reached - queue the execution
            _logger.LogInformation(
                "Concurrency limit reached for agent {Slug} (max={Max}, running={Running}). Queuing subsession {Id}",
                agent.Slug, agent.MaxConcurrentInstances, _concurrencyManager.GetRunningCount(agent.Slug), subSessionId);

            var oldStatus = subSession.Status;
            subSession.Status = SubSessionStatus.Queued;
            await _repository.UpdateAsync(subSession, ct);

            await _eventBus.PublishAsync(new SubSessionStatusChangedEvent
            {
                SubSessionId = subSessionId,
                ParentSessionId = subSession.ParentSessionId,
                OldStatus = oldStatus,
                NewStatus = SubSessionStatus.Queued
            }, ct);

            // Enqueue and wait for slot
            var tcs = new TaskCompletionSource<SubSession>();
            _concurrencyManager.Enqueue(agent.Slug, new QueuedExecution
            {
                SubSessionId = subSessionId,
                CompletionSource = tcs,
                CancellationToken = ct
            });

            // Register cancellation
            ct.Register(() => tcs.TrySetCanceled());

            // Wait for execution to complete (will be processed when a slot becomes available)
            return await tcs.Task;
        }

        // We have a slot - proceed with execution
        SubSession result;
        try
        {
            result = await ExecuteWithSlotAsync(subSession, agent, ct);
        }
        finally
        {
            // Release slot
            slotRelease?.Dispose();

            // Process queue after slot is released
            _ = ProcessQueueAsync(agent.Slug);
        }

        return result;
    }

    /// <summary>
    /// Processes the queue for the given agent type, executing the next queued item if available.
    /// </summary>
    private async Task ProcessQueueAsync(string agentSlug)
    {
        if (!_concurrencyManager.TryDequeue(agentSlug, out var queued) || queued == null)
        {
            return;
        }

        try
        {
            // Check if cancelled
            if (queued.CancellationToken.IsCancellationRequested)
            {
                queued.CompletionSource.TrySetCanceled();
                // Try next in queue
                _ = ProcessQueueAsync(agentSlug);
                return;
            }

            _logger.LogInformation(
                "Processing queued execution for subsession {Id}",
                queued.SubSessionId);

            // Re-run ExecuteSubSessionAsync - it will acquire a slot
            var result = await ExecuteSubSessionAsync(queued.SubSessionId, queued.CancellationToken);
            queued.CompletionSource.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            queued.CompletionSource.TrySetCanceled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process queued execution for {Id}", queued.SubSessionId);
            queued.CompletionSource.TrySetException(ex);
        }
    }

    private async Task<SubSession> ExecuteWithSlotAsync(
        SubSession subSession,
        Agent agent,
        CancellationToken ct)
    {
        var subSessionId = subSession.Id;

        // Update status to running
        var oldStatus = subSession.Status;
        subSession.Status = SubSessionStatus.Running;
        await _repository.UpdateAsync(subSession, ct);

        await _eventBus.PublishAsync(new SubSessionStatusChangedEvent
        {
            SubSessionId = subSessionId,
            ParentSessionId = subSession.ParentSessionId,
            OldStatus = oldStatus,
            NewStatus = SubSessionStatus.Running
        }, ct);

        var startTime = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation(
                "Executing subagent {AgentSlug} for subsession {Id} (running={Running}/{Max})",
                subSession.AgentSlug, subSessionId,
                _concurrencyManager.GetRunningCount(agent.Slug), agent.MaxConcurrentInstances);

            // Get working directory from parent session's project
            var parentSession = await _sessionRepository.GetAsync(subSession.ParentSessionId);
            var workingDir = Environment.CurrentDirectory;
            if (parentSession != null)
            {
                var project = await _projectRepository.GetAsync(parentSession.ProjectId);
                if (project != null && !string.IsNullOrEmpty(project.RootPath))
                {
                    workingDir = project.RootPath;
                }
            }

            // Create the subagent request
            var request = new SubagentRequest
            {
                Agent = agent,
                Prompt = subSession.Prompt ?? string.Empty,
                WorkingDirectory = workingDir,
                MaxIterations = subSession.MaxIterations,
                OnUpdate = async update =>
                {
                    // Publish progress events for streaming updates
                    await _eventBus.PublishAsync(new SubSessionProgressEvent
                    {
                        SubSessionId = subSessionId,
                        ParentSessionId = subSession.ParentSessionId,
                        PartType = update.IsDone ? "done" : "content",
                        Content = update.Delta,
                        Iteration = subSession.IterationsUsed
                    }, ct);
                },
                OnToolExecution = async toolUpdate =>
                {
                    // Publish tool execution events
                    await _eventBus.PublishAsync(new SubSessionProgressEvent
                    {
                        SubSessionId = subSessionId,
                        ParentSessionId = subSession.ParentSessionId,
                        PartType = "tool",
                        Content = $"[{toolUpdate.ToolName}] {(toolUpdate.Success ? "✓" : "✗")}",
                        Iteration = subSession.IterationsUsed
                    }, ct);
                }
            };

            // Execute the subagent using ChatService
            var result = await _chatService.RunSubagentAsync(request, ct);

            subSession.IterationsUsed = result.IterationsUsed;

            // Mark as completed or failed based on result
            if (result.Success)
            {
                subSession.Status = SubSessionStatus.Completed;
                subSession.Result = result.Output;
            }
            else
            {
                subSession.Status = SubSessionStatus.Failed;
                subSession.Result = result.Output;
                subSession.Error = result.Error;
            }

            subSession.CompletedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(subSession, ct);

            var duration = DateTimeOffset.UtcNow - startTime;

            if (result.Success)
            {
                await _eventBus.PublishAsync(new SubSessionCompletedEvent
                {
                    SubSessionId = subSessionId,
                    ParentSessionId = subSession.ParentSessionId,
                    Result = subSession.Result ?? string.Empty,
                    IterationsUsed = subSession.IterationsUsed,
                    Duration = duration
                }, ct);

                _logger.LogInformation(
                    "SubSession {Id} completed successfully in {Duration} ({Iterations} iterations)",
                    subSessionId, duration, result.IterationsUsed);
            }
            else
            {
                await _eventBus.PublishAsync(new SubSessionFailedEvent
                {
                    SubSessionId = subSessionId,
                    ParentSessionId = subSession.ParentSessionId,
                    Error = result.Error ?? "Unknown error",
                    ExceptionType = "SubagentExecutionError",
                    FailedAtIteration = subSession.IterationsUsed
                }, ct);

                _logger.LogWarning(
                    "SubSession {Id} failed after {Duration}: {Error}",
                    subSessionId, duration, result.Error);
            }

            return subSession;
        }
        catch (OperationCanceledException)
        {
            subSession.Status = SubSessionStatus.Cancelled;
            subSession.CompletedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(subSession, ct);

            await _eventBus.PublishAsync(new SubSessionCancelledEvent
            {
                SubSessionId = subSessionId,
                ParentSessionId = subSession.ParentSessionId,
                Reason = "Operation cancelled"
            }, ct);

            return subSession;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubSession {Id} failed", subSessionId);

            subSession.Status = SubSessionStatus.Failed;
            subSession.Error = ex.Message;
            subSession.CompletedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(subSession, ct);

            await _eventBus.PublishAsync(new SubSessionFailedEvent
            {
                SubSessionId = subSessionId,
                ParentSessionId = subSession.ParentSessionId,
                Error = ex.Message,
                ExceptionType = ex.GetType().Name,
                FailedAtIteration = subSession.IterationsUsed
            }, ct);

            return subSession;
        }
    }

    public async Task<SubSession?> GetSubSessionAsync(Guid id, CancellationToken ct = default)
    {
        return await _repository.GetByIdAsync(id, ct);
    }

    public async Task<IReadOnlyList<SubSession>> GetSubSessionsForParentAsync(
        long parentSessionId,
        CancellationToken ct = default)
    {
        return await _repository.GetByParentSessionIdAsync(parentSessionId, ct);
    }

    public async Task CancelSubSessionAsync(
        Guid subSessionId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var subSession = await _repository.GetByIdAsync(subSessionId, ct);
        if (subSession == null) return;

        if (subSession.Status == SubSessionStatus.Completed ||
            subSession.Status == SubSessionStatus.Failed ||
            subSession.Status == SubSessionStatus.Cancelled)
        {
            return; // Already in terminal state
        }

        subSession.Status = SubSessionStatus.Cancelled;
        subSession.Error = reason;
        subSession.CompletedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateAsync(subSession, ct);

        await _eventBus.PublishAsync(new SubSessionCancelledEvent
        {
            SubSessionId = subSessionId,
            ParentSessionId = subSession.ParentSessionId,
            Reason = reason
        }, ct);

        _logger.LogInformation("SubSession {Id} cancelled: {Reason}", subSessionId, reason ?? "No reason provided");
    }

    public bool CanSpawnSubagent(Agent parentAgent, string subagentSlug)
    {
        return _agentRegistry.CanSpawnSubagent(parentAgent, subagentSlug);
    }

    public int GetRunningCount(string agentSlug)
    {
        return _concurrencyManager.GetRunningCount(agentSlug);
    }

    public int GetQueueDepth(string agentSlug)
    {
        return _concurrencyManager.GetQueueDepth(agentSlug);
    }

    public IReadOnlyDictionary<string, AgentConcurrencyStatus> GetConcurrencyStatus()
    {
        return _concurrencyManager.GetStatus();
    }
}
