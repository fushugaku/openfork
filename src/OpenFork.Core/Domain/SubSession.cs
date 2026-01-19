using OpenFork.Core.Permissions;

namespace OpenFork.Core.Domain;

/// <summary>
/// Represents a child session spawned by a parent agent via the task tool.
/// </summary>
public class SubSession
{
    /// <summary>Unique identifier for the subsession.</summary>
    public Guid Id { get; set; }

    /// <summary>Parent session that spawned this subsession.</summary>
    public long ParentSessionId { get; set; }

    /// <summary>Message ID in parent session that contains the task tool call.</summary>
    public long? ParentMessageId { get; set; }

    /// <summary>Agent slug/type for this subsession.</summary>
    public string AgentSlug { get; set; } = "general";

    /// <summary>Current status of the subsession.</summary>
    public SubSessionStatus Status { get; set; } = SubSessionStatus.Pending;

    /// <summary>The prompt/task given to the subagent.</summary>
    public string? Prompt { get; set; }

    /// <summary>Short description for display (3-5 words).</summary>
    public string? Description { get; set; }

    /// <summary>Result from the subagent execution.</summary>
    public string? Result { get; set; }

    /// <summary>Error message if execution failed.</summary>
    public string? Error { get; set; }

    /// <summary>Maximum iterations allowed for this subagent.</summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>Number of iterations actually used.</summary>
    public int IterationsUsed { get; set; }

    /// <summary>Effective permissions for this subsession (inherited + agent defaults).</summary>
    public PermissionRuleset EffectivePermissions { get; set; } = new();

    /// <summary>When the subsession was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the subsession completed (success, failure, or cancellation).</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Status of a subsession execution.
/// </summary>
public enum SubSessionStatus
{
    /// <summary>Created but not yet started.</summary>
    Pending,

    /// <summary>Waiting for a slot (concurrency limit reached).</summary>
    Queued,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Completed successfully.</summary>
    Completed,

    /// <summary>Failed with an error.</summary>
    Failed,

    /// <summary>Cancelled by user or parent.</summary>
    Cancelled
}
