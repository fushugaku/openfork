using OpenFork.Core.Domain.Parts;

namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// MESSAGE EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a new message is created.
/// </summary>
public record MessageCreatedEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Preview { get; init; }
}

/// <summary>
/// Raised when streaming starts for a message.
/// </summary>
public record MessageStreamStartedEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
}

/// <summary>
/// Raised for each streaming chunk received.
/// </summary>
public record MessageStreamChunkEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
    public string Chunk { get; init; } = string.Empty;
    public int TotalLength { get; init; }
}

/// <summary>
/// Raised when streaming completes for a message.
/// </summary>
public record MessageStreamCompletedEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
    public string? FinishReason { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
}

/// <summary>
/// Raised when a message is compacted (summarized).
/// </summary>
public record MessageCompactedEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
    public string? Summary { get; init; }
    public int MessagesCompacted { get; init; }
    public int TokensRemoved { get; init; }
}

/// <summary>
/// Raised when a message is updated.
/// </summary>
public record MessageUpdatedEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
    public string? UpdateType { get; init; }
}

/// <summary>
/// Raised when a message is deleted.
/// </summary>
public record MessageDeletedEvent : EventBase
{
    public long MessageId { get; init; }
    public long SessionId { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// MESSAGE PART EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a message part is created.
/// </summary>
public record PartCreatedEvent : EventBase
{
    public MessagePart Part { get; init; } = null!;
    public long MessageId { get; init; }
    public long SessionId { get; init; }
}

/// <summary>
/// Raised when a message part is updated.
/// </summary>
public record PartUpdatedEvent : EventBase
{
    public MessagePart Part { get; init; } = null!;
    public long MessageId { get; init; }
    public long SessionId { get; init; }
    public string? ChangedProperty { get; init; }
}

/// <summary>
/// Raised when a message part is deleted.
/// </summary>
public record PartDeletedEvent : EventBase
{
    public Guid PartId { get; init; }
    public long MessageId { get; init; }
    public string PartType { get; init; } = string.Empty;
}
