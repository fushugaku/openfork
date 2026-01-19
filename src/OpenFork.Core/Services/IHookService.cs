using OpenFork.Core.Hooks;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for managing and executing hooks.
/// </summary>
public interface IHookService
{
    /// <summary>Register a hook.</summary>
    void Register(IHook hook);

    /// <summary>Unregister a hook.</summary>
    void Unregister(string hookId);

    /// <summary>Execute all hooks for a trigger.</summary>
    Task<HookPipelineResult> ExecuteAsync(
        HookTrigger trigger,
        HookContext context,
        CancellationToken ct = default);

    /// <summary>Get all registered hooks.</summary>
    IReadOnlyList<IHook> GetHooks(HookTrigger? trigger = null);

    /// <summary>Check if any hooks are registered for a trigger.</summary>
    bool HasHooks(HookTrigger trigger);
}

/// <summary>
/// Result of executing a hook pipeline.
/// </summary>
public record HookPipelineResult
{
    /// <summary>Whether all hooks succeeded.</summary>
    public bool AllSucceeded { get; init; }

    /// <summary>Whether to continue with the main action (for pre-hooks).</summary>
    public bool ShouldContinue { get; init; }

    /// <summary>The final context after all hooks.</summary>
    public HookContext FinalContext { get; init; } = null!;

    /// <summary>Execution records for each hook.</summary>
    public List<HookExecutionRecord> ExecutionRecords { get; init; } = new();
}

/// <summary>
/// Record of a single hook execution.
/// </summary>
public record HookExecutionRecord
{
    public string HookId { get; init; } = string.Empty;
    public string HookName { get; init; } = string.Empty;
    public HookResult Result { get; init; } = null!;
    public TimeSpan Duration { get; init; }
}
