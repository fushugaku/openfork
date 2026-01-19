namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Chain-of-thought reasoning from the model.
/// Contains the model's internal reasoning process if the model supports it.
/// </summary>
public class ReasoningPart : MessagePart
{
    public override string Type => "reasoning";

    /// <summary>The reasoning content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Whether the reasoning is visible in the UI.</summary>
    public bool IsVisible { get; set; } = true;
}
