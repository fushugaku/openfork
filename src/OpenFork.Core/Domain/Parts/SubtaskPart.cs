namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Subtask/subagent reference.
/// Tracks a spawned subagent and its execution.
/// </summary>
public class SubtaskPart : MessagePart
{
    public override string Type => "subtask";

    /// <summary>ID of the sub-session created for this subtask.</summary>
    public Guid SubSessionId { get; set; }

    /// <summary>Type of agent handling the subtask.</summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>The prompt/task given to the subagent.</summary>
    public string? Prompt { get; set; }

    /// <summary>Current status of the subtask.</summary>
    public SubtaskStatus Status { get; set; } = SubtaskStatus.Pending;

    /// <summary>Result from the subtask if completed.</summary>
    public string? Result { get; set; }

    /// <summary>Error message if the subtask failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Status of a subtask.
/// </summary>
public enum SubtaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
