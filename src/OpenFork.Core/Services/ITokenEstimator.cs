using OpenFork.Core.Chat;
using OpenFork.Core.Domain;
using OpenFork.Core.Domain.Parts;

namespace OpenFork.Core.Services;

/// <summary>
/// Estimates token counts for text, messages, and parts.
/// </summary>
public interface ITokenEstimator
{
    /// <summary>Estimate tokens for raw text.</summary>
    int EstimateTokens(string? text);

    /// <summary>Estimate tokens for a message part.</summary>
    int EstimatePartTokens(MessagePart part);

    /// <summary>Estimate tokens for a message.</summary>
    int EstimateMessageTokens(Message message);

    /// <summary>Estimate tokens for a chat completion request.</summary>
    int EstimateRequestTokens(ChatCompletionRequest request);

    /// <summary>Estimate tokens for a list of parts.</summary>
    int EstimatePartsTokens(IEnumerable<MessagePart> parts);
}
