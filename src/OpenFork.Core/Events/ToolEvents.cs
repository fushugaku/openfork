namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// TOOL EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a tool execution starts.
/// </summary>
public record ToolExecutionStartedEvent : EventBase
{
    public Guid ExecutionId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? Input { get; init; }
    public Guid SessionId { get; init; }
}

/// <summary>
/// Raised to report tool execution progress.
/// </summary>
public record ToolExecutionProgressEvent : EventBase
{
    public Guid ExecutionId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string Progress { get; init; } = string.Empty;
    public double? PercentComplete { get; init; }
}

/// <summary>
/// Raised when a tool execution completes.
/// </summary>
public record ToolExecutionCompletedEvent : EventBase
{
    public Guid ExecutionId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Raised when a tool execution is cancelled.
/// </summary>
public record ToolExecutionCancelledEvent : EventBase
{
    public Guid ExecutionId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
