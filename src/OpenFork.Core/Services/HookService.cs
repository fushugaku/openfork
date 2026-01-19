using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Events;
using OpenFork.Core.Hooks;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for managing and executing hooks.
/// </summary>
public class HookService : IHookService
{
    private readonly ConcurrentDictionary<string, IHook> _hooks = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<HookService> _logger;

    public HookService(IEventBus eventBus, ILogger<HookService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public void Register(IHook hook)
    {
        _hooks[hook.Id] = hook;
        _logger.LogDebug("Registered hook: {Name} ({Trigger})", hook.Name, hook.Trigger);
    }

    public void Unregister(string hookId)
    {
        if (_hooks.TryRemove(hookId, out var hook))
        {
            _logger.LogDebug("Unregistered hook: {Name}", hook.Name);
        }
    }

    public async Task<HookPipelineResult> ExecuteAsync(
        HookTrigger trigger,
        HookContext context,
        CancellationToken ct = default)
    {
        var hooks = _hooks.Values
            .Where(h => h.Trigger == trigger && h.Enabled)
            .OrderBy(h => h.Priority)
            .ToList();

        if (hooks.Count == 0)
        {
            return new HookPipelineResult
            {
                AllSucceeded = true,
                ShouldContinue = true,
                FinalContext = context
            };
        }

        var records = new List<HookExecutionRecord>();
        var currentContext = context;
        var allSucceeded = true;
        var shouldContinue = true;

        foreach (var hook in hooks)
        {
            // For pre-hooks, if a previous hook cancelled, skip remaining
            if (!shouldContinue && trigger.ToString().StartsWith("Pre"))
            {
                break;
            }

            var startTime = DateTimeOffset.UtcNow;

            try
            {
                var result = await hook.ExecuteAsync(currentContext, ct);

                records.Add(new HookExecutionRecord
                {
                    HookId = hook.Id,
                    HookName = hook.Name,
                    Result = result,
                    Duration = DateTimeOffset.UtcNow - startTime
                });

                if (!result.Success)
                {
                    allSucceeded = false;
                    _logger.LogWarning("Hook {Name} failed: {Error}", hook.Name, result.Error);
                }

                if (!result.Continue)
                {
                    shouldContinue = false;
                    _logger.LogInformation("Hook {Name} cancelled pipeline: {Reason}",
                        hook.Name, result.Error);
                }

                // Apply modified context
                if (result.ModifiedContext != null)
                {
                    currentContext = result.ModifiedContext;
                }

                // Merge data
                if (result.Data != null)
                {
                    foreach (var (key, value) in result.Data)
                    {
                        currentContext.Data[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook {Name} threw exception", hook.Name);
                allSucceeded = false;

                records.Add(new HookExecutionRecord
                {
                    HookId = hook.Id,
                    HookName = hook.Name,
                    Result = HookResult.Fail(ex.Message),
                    Duration = DateTimeOffset.UtcNow - startTime
                });
            }
        }

        // Publish event
        await _eventBus.PublishAsync(new HookPipelineExecutedEvent
        {
            Trigger = trigger,
            HooksExecuted = records.Count,
            AllSucceeded = allSucceeded,
            ShouldContinue = shouldContinue
        }, ct);

        return new HookPipelineResult
        {
            AllSucceeded = allSucceeded,
            ShouldContinue = shouldContinue,
            FinalContext = currentContext,
            ExecutionRecords = records
        };
    }

    public IReadOnlyList<IHook> GetHooks(HookTrigger? trigger = null)
    {
        var query = _hooks.Values.AsEnumerable();
        if (trigger.HasValue)
        {
            query = query.Where(h => h.Trigger == trigger.Value);
        }
        return query.OrderBy(h => h.Priority).ToList();
    }

    public bool HasHooks(HookTrigger trigger)
    {
        return _hooks.Values.Any(h => h.Trigger == trigger && h.Enabled);
    }
}
