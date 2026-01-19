namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Plain text content from LLM response.
/// </summary>
public class TextPart : MessagePart
{
    public override string Type => "text";

    /// <summary>The text content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The type of text content.</summary>
    public TextContentType ContentType { get; set; } = TextContentType.Markdown;
}

/// <summary>
/// Type of text content.
/// </summary>
public enum TextContentType
{
    /// <summary>Plain text.</summary>
    Plain,

    /// <summary>Markdown formatted text.</summary>
    Markdown,

    /// <summary>Code content.</summary>
    Code
}
