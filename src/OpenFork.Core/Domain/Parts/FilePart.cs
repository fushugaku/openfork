namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// File attachment or reference in a message.
/// </summary>
public class FilePart : MessagePart
{
    public override string Type => "file";

    /// <summary>Path to the file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Display name of the file.</summary>
    public string? FileName { get; set; }

    /// <summary>MIME type of the file.</summary>
    public string? ContentType { get; set; }

    /// <summary>Size of the file in bytes.</summary>
    public long? Size { get; set; }

    /// <summary>Inline content if small enough.</summary>
    public string? Content { get; set; }

    /// <summary>Whether content is stored inline vs referenced.</summary>
    public bool IsInline { get; set; }
}
