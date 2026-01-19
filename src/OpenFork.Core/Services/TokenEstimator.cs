using OpenFork.Core.Chat;
using OpenFork.Core.Constants;
using OpenFork.Core.Domain;
using OpenFork.Core.Domain.Parts;

namespace OpenFork.Core.Services;

/// <summary>
/// Estimates token counts using character-based heuristics.
/// For more accuracy, consider using tiktoken or similar.
/// </summary>
public class TokenEstimator : ITokenEstimator
{
    private const int MessageOverhead = 4;  // Role marker overhead per message

    /// <inheritdoc />
    public int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / TokenConstants.CharsPerToken);
    }

    /// <inheritdoc />
    public int EstimatePartTokens(MessagePart part)
    {
        return part switch
        {
            TextPart tp => EstimateTokens(tp.Content),
            ToolPart toolPart => EstimateTokens(toolPart.Input) + EstimateTokens(toolPart.Output),
            CompactionPart cp => EstimateTokens(cp.Summary),
            ReasoningPart rp => EstimateTokens(rp.Content),
            FilePart fp => EstimateTokens(fp.FileName) + EstimateTokens(fp.Content),
            PatchPart pp => EstimateTokens(pp.FilePath) + EstimateTokens(pp.UnifiedDiff),
            StepPart sp => EstimateTokens(sp.Description),
            SubtaskPart stp => EstimateTokens(stp.Prompt),
            AgentPart ap => EstimateTokens(ap.AgentName) + EstimateTokens(ap.Reason),
            RetryPart rtp => EstimateTokens(rtp.Reason) + EstimateTokens(rtp.OriginalError),
            SnapshotPart snp => EstimateTokens(snp.Label) + EstimateTokens(snp.Description),
            _ => 0
        };
    }

    /// <inheritdoc />
    public int EstimateMessageTokens(Message message)
    {
        var tokens = EstimateTokens(message.Content);

        // Add overhead for role/structure
        tokens += MessageOverhead;

        // Add tool calls if present
        if (!string.IsNullOrEmpty(message.ToolCallsJson))
        {
            tokens += EstimateTokens(message.ToolCallsJson);
        }

        return tokens;
    }

    /// <inheritdoc />
    public int EstimateRequestTokens(ChatCompletionRequest request)
    {
        var tokens = 0;

        // System prompt
        if (request.Messages.FirstOrDefault(m => m.Role == "system") is { } systemMsg)
        {
            tokens += EstimateTokens(systemMsg.Content);
        }

        // Messages
        foreach (var message in request.Messages)
        {
            tokens += EstimateTokens(message.Content);
            tokens += MessageOverhead;

            if (message.ToolCalls != null)
            {
                foreach (var tc in message.ToolCalls)
                {
                    tokens += EstimateTokens(tc.Function.Name);
                    tokens += EstimateTokens(tc.Function.Arguments);
                }
            }
        }

        // Tools schema
        if (request.Tools != null)
        {
            foreach (var tool in request.Tools)
            {
                tokens += EstimateTokens(tool.Function.Name);
                tokens += EstimateTokens(tool.Function.Description);
                tokens += EstimateTokens(tool.Function.Parameters?.ToString());
            }
        }

        return tokens;
    }

    /// <inheritdoc />
    public int EstimatePartsTokens(IEnumerable<MessagePart> parts)
    {
        return parts.Sum(EstimatePartTokens);
    }
}
