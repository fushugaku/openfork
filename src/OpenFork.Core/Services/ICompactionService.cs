using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

/// <summary>
/// Layer 3: Conversation compaction service.
/// Summarizes older messages when context is overflowing.
/// </summary>
public interface ICompactionService
{
    /// <summary>
    /// Compact conversation by summarizing older messages.
    /// </summary>
    /// <param name="sessionId">Session to compact.</param>
    /// <param name="messages">Current messages in the session.</param>
    /// <param name="currentTokens">Current token count.</param>
    /// <param name="contextLimit">Context window limit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Compaction result with summary and metadata.</returns>
    Task<CompactionResult> CompactConversationAsync(
        long sessionId,
        IReadOnlyList<Message> messages,
        int currentTokens,
        int contextLimit,
        CancellationToken ct = default);

    /// <summary>
    /// Load messages respecting compaction boundaries.
    /// Returns a synthetic summary message for compacted content.
    /// </summary>
    /// <param name="sessionId">Session to load messages for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Messages including synthetic summary for compacted portion.</returns>
    Task<IReadOnlyList<Message>> LoadMessagesWithCompactionBoundaryAsync(
        long sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Check if compaction is needed based on current token count.
    /// </summary>
    bool IsCompactionNeeded(int currentTokens, int contextLimit);
}

/// <summary>
/// Result of conversation compaction.
/// </summary>
public record CompactionResult
{
    /// <summary>Whether compaction was performed.</summary>
    public bool WasCompacted { get; init; }

    /// <summary>Token count before compaction.</summary>
    public int TokensBefore { get; init; }

    /// <summary>Token count after compaction.</summary>
    public int TokensAfter { get; init; }

    /// <summary>Number of messages that were compacted.</summary>
    public int MessagesCompacted { get; init; }

    /// <summary>Generated summary of compacted content.</summary>
    public string? Summary { get; init; }

    /// <summary>ID of the created compaction part.</summary>
    public Guid? CompactionPartId { get; init; }

    /// <summary>Tokens removed by compaction.</summary>
    public int TokensRemoved => TokensBefore - TokensAfter;
}
