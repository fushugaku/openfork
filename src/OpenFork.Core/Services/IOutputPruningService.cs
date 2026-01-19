using OpenFork.Core.Domain.Parts;

namespace OpenFork.Core.Services;

/// <summary>
/// Layer 2: Tool output pruning service.
/// Prunes old tool outputs when approaching context limit.
/// </summary>
public interface IOutputPruningService
{
    /// <summary>
    /// Prune message parts to reduce token count.
    /// </summary>
    /// <param name="parts">Current message parts.</param>
    /// <param name="currentTokens">Current token count.</param>
    /// <param name="contextLimit">Context window limit.</param>
    /// <returns>Pruning result with pruned parts and metadata.</returns>
    PruneResult PruneMessageParts(
        IReadOnlyList<MessagePart> parts,
        int currentTokens,
        int contextLimit);
}

/// <summary>
/// Result of pruning message parts.
/// </summary>
public record PruneResult
{
    /// <summary>Parts after pruning (cloned with modified outputs).</summary>
    public IReadOnlyList<MessagePart> PrunedParts { get; init; } = Array.Empty<MessagePart>();

    /// <summary>Token count before pruning.</summary>
    public int TokensBefore { get; init; }

    /// <summary>Token count after pruning.</summary>
    public int TokensAfter { get; init; }

    /// <summary>Number of parts that were pruned.</summary>
    public int PartsPruned { get; init; }

    /// <summary>Whether any pruning was performed.</summary>
    public bool WasPruned { get; init; }

    /// <summary>Tokens removed by pruning.</summary>
    public int TokensRemoved => TokensBefore - TokensAfter;
}
