namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Code diff/patch representation.
/// </summary>
public class PatchPart : MessagePart
{
    public override string Type => "patch";

    /// <summary>Path to the file being patched.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Original content before the change.</summary>
    public string? OldContent { get; set; }

    /// <summary>New content after the change.</summary>
    public string? NewContent { get; set; }

    /// <summary>Unified diff format of the change.</summary>
    public string? UnifiedDiff { get; set; }

    /// <summary>Number of lines added.</summary>
    public int Additions { get; set; }

    /// <summary>Number of lines deleted.</summary>
    public int Deletions { get; set; }
}
