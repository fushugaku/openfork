namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// SESSION EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a new session is created.
/// </summary>
public record SessionCreatedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Raised when a session is updated (name, agent, etc.).
/// </summary>
public record SessionUpdatedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string? OldName { get; init; }
    public string? NewName { get; init; }
    public Guid? OldAgentId { get; init; }
    public Guid? NewAgentId { get; init; }
}

/// <summary>
/// Raised when a session is deleted.
/// </summary>
public record SessionDeletedEvent : EventBase
{
    public Guid SessionId { get; init; }
}

/// <summary>
/// Raised when a session becomes the active session.
/// </summary>
public record SessionActivatedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public Guid? PreviousSessionId { get; init; }
}
