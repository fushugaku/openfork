namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// State snapshot for restoration/debugging.
/// Captures a point-in-time state of the conversation.
/// </summary>
public class SnapshotPart : MessagePart
{
    public override string Type => "snapshot";

    /// <summary>Label for the snapshot.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Description of what this snapshot captures.</summary>
    public string? Description { get; set; }

    /// <summary>State data (serialized as JSON).</summary>
    public Dictionary<string, object?>? State { get; set; }

    /// <summary>Optional git commit reference.</summary>
    public string? GitCommit { get; set; }
}
