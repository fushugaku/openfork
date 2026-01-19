namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Agent step boundary marker.
/// Represents a logical step in an agent's execution.
/// </summary>
public class StepPart : MessagePart
{
    public override string Type => "step";

    /// <summary>Step number (1-based).</summary>
    public int StepNumber { get; set; }

    /// <summary>Description of the step.</summary>
    public string? Description { get; set; }

    /// <summary>Current status of the step.</summary>
    public StepStatus Status { get; set; } = StepStatus.InProgress;
}

/// <summary>
/// Status of a step.
/// </summary>
public enum StepStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed
}
