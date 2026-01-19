namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Agent invocation/handoff marker.
/// Records when an agent is invoked or switched.
/// </summary>
public class AgentPart : MessagePart
{
    public override string Type => "agent";

    /// <summary>Type of agent being invoked.</summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>Display name of the agent.</summary>
    public string? AgentName { get; set; }

    /// <summary>ID of the target agent (if specific).</summary>
    public Guid? TargetAgentId { get; set; }

    /// <summary>Reason for invoking the agent.</summary>
    public string? Reason { get; set; }
}
