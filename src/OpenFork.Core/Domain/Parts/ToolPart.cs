namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Tool invocation with full lifecycle tracking.
/// Tracks the tool from request to completion with state transitions.
/// </summary>
public class ToolPart : MessagePart
{
    public override string Type => "tool";

    // Identity
    /// <summary>Unique identifier for this tool call (from LLM).</summary>
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>Name of the tool being invoked.</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Human-readable summary of what the tool is doing.</summary>
    public string Title { get; set; } = string.Empty;

    // State machine
    /// <summary>Current status of the tool execution.</summary>
    public ToolPartStatus Status { get; set; } = ToolPartStatus.Pending;

    // Input/Output
    /// <summary>JSON arguments passed to the tool.</summary>
    public string? Input { get; set; }

    /// <summary>Result content from the tool.</summary>
    public string? Output { get; set; }

    /// <summary>Whether the output was truncated (pruned).</summary>
    public bool IsPruned { get; set; }

    // Timing
    /// <summary>When tool execution started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When tool execution completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Duration of tool execution.</summary>
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;

    // Error handling
    /// <summary>Error message if tool execution failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Error code/type if tool execution failed.</summary>
    public string? ErrorCode { get; set; }

    // Attachments (for rich tool outputs)
    /// <summary>Attachments from the tool execution (files, etc.).</summary>
    public List<ToolAttachment>? Attachments { get; set; }

    // Disk spillover
    /// <summary>Path to full output if truncated to disk.</summary>
    public string? SpillPath { get; set; }
}

/// <summary>
/// Status of a tool part execution.
/// </summary>
public enum ToolPartStatus
{
    /// <summary>Created, not yet started.</summary>
    Pending,

    /// <summary>Actively executing.</summary>
    Running,

    /// <summary>Successfully finished.</summary>
    Completed,

    /// <summary>Failed with error.</summary>
    Error
}

/// <summary>
/// An attachment from a tool execution.
/// </summary>
public record ToolAttachment
{
    /// <summary>Type of attachment (file, image, chart, etc.).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Display name of the attachment.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Path to the attachment (if file-based).</summary>
    public string? Path { get; init; }

    /// <summary>MIME type of the attachment.</summary>
    public string? ContentType { get; init; }

    /// <summary>Size in bytes.</summary>
    public long? Size { get; init; }
}
