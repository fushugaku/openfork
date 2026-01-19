namespace OpenFork.Core.Domain;

public class Message
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long? AgentId { get; set; }
    public long? PipelineStepId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ToolCallsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Whether this message has been compacted (summarized).
    /// Compacted messages are not loaded in normal conversation flow.
    /// </summary>
    public bool IsCompacted { get; set; }
}
