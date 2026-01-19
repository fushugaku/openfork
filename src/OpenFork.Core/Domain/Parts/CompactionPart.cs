namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Compaction boundary marker.
/// Indicates where message history was summarized.
/// </summary>
public class CompactionPart : MessagePart
{
    public override string Type => "compaction";

    /// <summary>Summary of the compacted messages.</summary>
    public string? Summary { get; set; }

    /// <summary>Number of messages that were compacted.</summary>
    public int CompactedMessageCount { get; set; }

    /// <summary>Total tokens in the compacted messages.</summary>
    public int CompactedTokenCount { get; set; }

    /// <summary>When the compaction occurred.</summary>
    public DateTimeOffset CompactedAt { get; set; }
}
