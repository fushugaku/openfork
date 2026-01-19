using OpenFork.Core.Hooks;

namespace OpenFork.Core.Events;

/// <summary>
/// Fired when a hook pipeline has finished executing.
/// </summary>
public record HookPipelineExecutedEvent : EventBase
{
    /// <summary>The trigger that caused execution.</summary>
    public HookTrigger Trigger { get; init; }

    /// <summary>Number of hooks executed.</summary>
    public int HooksExecuted { get; init; }

    /// <summary>Whether all hooks succeeded.</summary>
    public bool AllSucceeded { get; init; }

    /// <summary>Whether the main action should continue.</summary>
    public bool ShouldContinue { get; init; }
}

/// <summary>
/// Fired when a hook is registered.
/// </summary>
public record HookRegisteredEvent : EventBase
{
    /// <summary>Hook ID.</summary>
    public string HookId { get; init; } = string.Empty;

    /// <summary>Hook name.</summary>
    public string HookName { get; init; } = string.Empty;

    /// <summary>Hook trigger.</summary>
    public HookTrigger Trigger { get; init; }
}

/// <summary>
/// Fired when a hook is unregistered.
/// </summary>
public record HookUnregisteredEvent : EventBase
{
    /// <summary>Hook ID.</summary>
    public string HookId { get; init; } = string.Empty;
}

/// <summary>
/// Fired when a single hook execution completes.
/// </summary>
public record HookExecutedEvent : EventBase
{
    /// <summary>Hook ID.</summary>
    public string HookId { get; init; } = string.Empty;

    /// <summary>Hook name.</summary>
    public string HookName { get; init; } = string.Empty;

    /// <summary>Hook trigger.</summary>
    public HookTrigger Trigger { get; init; }

    /// <summary>Whether the hook succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Execution duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }
}
