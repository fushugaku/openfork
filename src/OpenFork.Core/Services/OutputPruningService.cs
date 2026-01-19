using Microsoft.Extensions.Logging;
using OpenFork.Core.Constants;
using OpenFork.Core.Domain.Parts;

namespace OpenFork.Core.Services;

/// <summary>
/// Layer 2: Prunes old tool outputs when approaching context limit.
/// Protects recent content and ensures minimum reduction is achieved.
/// </summary>
public class OutputPruningService : IOutputPruningService
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<OutputPruningService> _logger;

    public OutputPruningService(
        ITokenEstimator tokenEstimator,
        ILogger<OutputPruningService> logger)
    {
        _tokenEstimator = tokenEstimator;
        _logger = logger;
    }

    /// <inheritdoc />
    public PruneResult PruneMessageParts(
        IReadOnlyList<MessagePart> parts,
        int currentTokens,
        int contextLimit)
    {
        // Calculate available space (context minus reserved for output)
        var availableForInput = contextLimit - TokenConstants.DefaultMaxOutputTokens;

        // Check if pruning is needed
        if (currentTokens < availableForInput)
        {
            return NoPruningNeeded(parts, currentTokens);
        }

        // Don't prune if we don't have enough tokens yet (protection threshold)
        if (currentTokens < TokenConstants.PruneProtectTokens)
        {
            return NoPruningNeeded(parts, currentTokens);
        }

        _logger.LogInformation(
            "Starting output pruning: {Current:N0} tokens, target reduction: {Target:N0}",
            currentTokens,
            TokenConstants.PruneMinimumTokens);

        var prunedParts = new List<MessagePart>(parts.Count);
        var tokensRemoved = 0;
        var partsPruned = 0;

        // Find protection boundary - protect recent content
        var protectedIndex = FindProtectionBoundary(parts);

        // Process parts from oldest to newest
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];

            // Protect recent parts
            if (i >= protectedIndex)
            {
                prunedParts.Add(part);
                continue;
            }

            // Only prune tool output parts with large outputs
            if (part is ToolPart toolPart &&
                !toolPart.IsPruned &&
                toolPart.Output?.Length > TokenConstants.PruneOutputRetainChars)
            {
                var prunedOutput = toolPart.Output[..TokenConstants.PruneOutputRetainChars];
                var originalTokens = _tokenEstimator.EstimateTokens(toolPart.Output);
                var prunedTokens = _tokenEstimator.EstimateTokens(prunedOutput);

                // Clone and modify
                var prunedToolPart = new ToolPart
                {
                    Id = toolPart.Id,
                    MessageId = toolPart.MessageId,
                    OrderIndex = toolPart.OrderIndex,
                    CreatedAt = toolPart.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    ToolCallId = toolPart.ToolCallId,
                    ToolName = toolPart.ToolName,
                    Title = toolPart.Title,
                    Status = toolPart.Status,
                    Input = toolPart.Input,
                    Output = prunedOutput + $"\n\n[Output pruned: kept first {TokenConstants.PruneOutputRetainChars:N0} chars]",
                    IsPruned = true,
                    StartedAt = toolPart.StartedAt,
                    CompletedAt = toolPart.CompletedAt,
                    ErrorMessage = toolPart.ErrorMessage,
                    ErrorCode = toolPart.ErrorCode,
                    Attachments = toolPart.Attachments,
                    SpillPath = toolPart.SpillPath
                };

                prunedParts.Add(prunedToolPart);
                tokensRemoved += originalTokens - prunedTokens;
                partsPruned++;

                // Stop if we've removed enough
                if (tokensRemoved >= TokenConstants.PruneMinimumTokens)
                {
                    // Add remaining parts unpruned
                    for (int j = i + 1; j < parts.Count; j++)
                    {
                        prunedParts.Add(parts[j]);
                    }
                    break;
                }
            }
            else
            {
                prunedParts.Add(part);
            }
        }

        var tokensAfter = currentTokens - tokensRemoved;

        _logger.LogInformation(
            "Pruning complete: {Before:N0} → {After:N0} tokens ({Removed:N0} removed, {Parts} parts pruned)",
            currentTokens, tokensAfter, tokensRemoved, partsPruned);

        return new PruneResult
        {
            PrunedParts = prunedParts,
            TokensBefore = currentTokens,
            TokensAfter = tokensAfter,
            PartsPruned = partsPruned,
            WasPruned = tokensRemoved > 0
        };
    }

    private int FindProtectionBoundary(IReadOnlyList<MessagePart> parts)
    {
        // Find the index where protection starts (from the end)
        // Parts from this index onwards are protected from pruning
        var protectedTokens = 0;

        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var partTokens = _tokenEstimator.EstimatePartTokens(parts[i]);
            if (protectedTokens + partTokens > TokenConstants.PruneProtectTokens)
            {
                return i + 1;
            }
            protectedTokens += partTokens;
        }

        return 0;  // Protect nothing if total is under threshold
    }

    private static PruneResult NoPruningNeeded(IReadOnlyList<MessagePart> parts, int currentTokens)
    {
        return new PruneResult
        {
            PrunedParts = parts.ToList(),
            TokensBefore = currentTokens,
            TokensAfter = currentTokens,
            PartsPruned = 0,
            WasPruned = false
        };
    }
}
