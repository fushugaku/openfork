using OpenFork.Core.Domain;

namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// SUBSESSION LIFECYCLE EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a subsession is created via the task tool.
/// </summary>
public record SubSessionCreatedEvent : EventBase
{
    /// <summary>The newly created subsession ID.</summary>
    public Guid SubSessionId { get; init; }

    /// <summary>Parent session that spawned this subsession.</summary>
    public long ParentSessionId { get; init; }

    /// <summary>Agent slug/type for the subagent.</summary>
    public string AgentSlug { get; init; } = string.Empty;

    /// <summary>Short description of the task.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Raised when a subsession's status changes.
/// </summary>
public record SubSessionStatusChangedEvent : EventBase
{
    /// <summary>The subsession ID.</summary>
    public Guid SubSessionId { get; init; }

    /// <summary>Parent session ID.</summary>
    public long ParentSessionId { get; init; }

    /// <summary>Previous status.</summary>
    public SubSessionStatus OldStatus { get; init; }

    /// <summary>New status.</summary>
    public SubSessionStatus NewStatus { get; init; }
}

/// <summary>
/// Raised when a subsession makes progress (streaming output, tool call, etc.).
/// </summary>
public record SubSessionProgressEvent : EventBase
{
    /// <summary>The subsession ID.</summary>
    public Guid SubSessionId { get; init; }

    /// <summary>Parent session ID.</summary>
    public long ParentSessionId { get; init; }

    /// <summary>Type of progress (text, tool, reasoning).</summary>
    public string PartType { get; init; } = string.Empty;

    /// <summary>Content of the progress update.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Current iteration number.</summary>
    public int Iteration { get; init; }
}

/// <summary>
/// Raised when a subsession completes successfully.
/// </summary>
public record SubSessionCompletedEvent : EventBase
{
    /// <summary>The subsession ID.</summary>
    public Guid SubSessionId { get; init; }

    /// <summary>Parent session ID.</summary>
    public long ParentSessionId { get; init; }

    /// <summary>Final result from the subagent.</summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>Number of iterations used.</summary>
    public int IterationsUsed { get; init; }

    /// <summary>Execution duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Raised when a subsession fails with an error.
/// </summary>
public record SubSessionFailedEvent : EventBase
{
    /// <summary>The subsession ID.</summary>
    public Guid SubSessionId { get; init; }

    /// <summary>Parent session ID.</summary>
    public long ParentSessionId { get; init; }

    /// <summary>Error message.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Exception type if available.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Iteration where failure occurred.</summary>
    public int? FailedAtIteration { get; init; }
}

/// <summary>
/// Raised when a subsession is cancelled.
/// </summary>
public record SubSessionCancelledEvent : EventBase
{
    /// <summary>The subsession ID.</summary>
    public Guid SubSessionId { get; init; }

    /// <summary>Parent session ID.</summary>
    public long ParentSessionId { get; init; }

    /// <summary>Reason for cancellation.</summary>
    public string? Reason { get; init; }
}
