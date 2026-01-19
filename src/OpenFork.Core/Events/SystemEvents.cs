namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// SYSTEM EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when an error occurs.
/// </summary>
public record ErrorOccurredEvent : EventBase
{
    public string Component { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? StackTrace { get; init; }
    public string? ExceptionType { get; init; }
    public Guid? SessionId { get; init; }
}

/// <summary>
/// Raised when a warning condition is detected.
/// </summary>
public record WarningRaisedEvent : EventBase
{
    public string Component { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Guid? SessionId { get; init; }
}

/// <summary>
/// Raised when a metric is recorded.
/// </summary>
public record MetricRecordedEvent : EventBase
{
    public string Name { get; init; } = string.Empty;
    public double Value { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
}

/// <summary>
/// Raised when the application starts.
/// </summary>
public record ApplicationStartedEvent : EventBase
{
    public string Version { get; init; } = string.Empty;
}

/// <summary>
/// Raised when the application is shutting down.
/// </summary>
public record ApplicationShuttingDownEvent : EventBase
{
    public string? Reason { get; init; }
}
