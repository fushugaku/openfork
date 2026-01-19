namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// TOKEN MANAGEMENT EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when tool output is truncated (Layer 1).
/// </summary>
public record OutputTruncatedEvent : EventBase
{
    public long SessionId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public int OriginalBytes { get; init; }
    public int TruncatedBytes { get; init; }
    public int OriginalLines { get; init; }
    public int TruncatedLines { get; init; }
    public string? SpillPath { get; init; }
}

/// <summary>
/// Raised when message parts are pruned (Layer 2).
/// </summary>
public record OutputsPrunedEvent : EventBase
{
    public long SessionId { get; init; }
    public int TokensBefore { get; init; }
    public int TokensAfter { get; init; }
    public int PartsPruned { get; init; }
}

/// <summary>
/// Raised when conversation is compacted (Layer 3).
/// </summary>
public record ConversationCompactedEvent : EventBase
{
    public long SessionId { get; init; }
    public int TokensBefore { get; init; }
    public int TokensAfter { get; init; }
    public int MessagesCompacted { get; init; }
    public Guid CompactionPartId { get; init; }
}

/// <summary>
/// Raised when token usage approaches limits.
/// </summary>
public record TokenWarningEvent : EventBase
{
    public long SessionId { get; init; }
    public int CurrentTokens { get; init; }
    public int ContextLimit { get; init; }
    public double UsagePercentage { get; init; }
    public string WarningLevel { get; init; } = string.Empty; // "warning", "critical"
}
