using OpenFork.Core.Domain;

namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// AGENT EXECUTION EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when an agent execution starts.
/// </summary>
public record AgentExecutionStartedEvent : EventBase
{
    /// <summary>Agent being executed.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Agent name for display.</summary>
    public string AgentName { get; init; } = string.Empty;

    /// <summary>Session context.</summary>
    public long SessionId { get; init; }

    /// <summary>Execution mode being used.</summary>
    public AgentExecutionMode ExecutionMode { get; init; }

    /// <summary>Input/prompt being processed.</summary>
    public string? Input { get; init; }
}

/// <summary>
/// Raised when an agent iteration starts.
/// </summary>
public record AgentIterationStartedEvent : EventBase
{
    public long SessionId { get; init; }
    public Guid AgentId { get; init; }
    public int IterationNumber { get; init; }
    public int MaxIterations { get; init; }
    public string AgentName { get; init; } = string.Empty;
}

/// <summary>
/// Raised when an agent iteration completes.
/// </summary>
public record AgentIterationCompletedEvent : EventBase
{
    public long SessionId { get; init; }
    public Guid AgentId { get; init; }
    public int IterationNumber { get; init; }
    public int MaxIterations { get; init; }
    public int ToolCallCount { get; init; }
    public bool HasMoreWork { get; init; }
    public string? ToolName { get; init; }
}

/// <summary>
/// Raised when an agent execution completes successfully.
/// </summary>
public record AgentExecutionCompletedEvent : EventBase
{
    /// <summary>Agent ID.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Session ID.</summary>
    public long SessionId { get; init; }

    /// <summary>Whether execution was successful.</summary>
    public bool Success { get; init; }

    /// <summary>Number of iterations used.</summary>
    public int Iterations { get; init; }

    /// <summary>Total execution duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Total tokens used.</summary>
    public int? TokensUsed { get; init; }
}

/// <summary>
/// Raised when an agent execution fails.
/// </summary>
public record AgentExecutionFailedEvent : EventBase
{
    /// <summary>Agent ID.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Session ID.</summary>
    public long SessionId { get; init; }

    /// <summary>Error message.</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Exception type if available.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Iteration where failure occurred.</summary>
    public int? FailedAtIteration { get; init; }
}

/// <summary>
/// Raised when an agent reaches its maximum iteration limit.
/// </summary>
public record AgentMaxIterationsReachedEvent : EventBase
{
    public long SessionId { get; init; }
    public Guid AgentId { get; init; }
    public int MaxIterations { get; init; }
    public string AgentName { get; init; } = string.Empty;
}

/// <summary>
/// Raised when an agent is switched for a session.
/// </summary>
public record AgentSwitchedEvent : EventBase
{
    public long SessionId { get; init; }
    public Guid? PreviousAgentId { get; init; }
    public string? PreviousAgentName { get; init; }
    public Guid NewAgentId { get; init; }
    public string NewAgentName { get; init; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// SUBAGENT EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a subagent is spawned.
/// </summary>
public record SubagentSpawnedEvent : EventBase
{
    /// <summary>Parent agent that spawned the subagent.</summary>
    public Guid ParentAgentId { get; init; }

    /// <summary>Subagent that was spawned.</summary>
    public Guid SubagentId { get; init; }

    /// <summary>Subagent slug/type.</summary>
    public string SubagentSlug { get; init; } = string.Empty;

    /// <summary>Session ID.</summary>
    public long SessionId { get; init; }

    /// <summary>Task/prompt given to subagent.</summary>
    public string Task { get; init; } = string.Empty;
}

/// <summary>
/// Raised when a subagent completes its task.
/// </summary>
public record SubagentCompletedEvent : EventBase
{
    /// <summary>Parent agent ID.</summary>
    public Guid ParentAgentId { get; init; }

    /// <summary>Subagent ID.</summary>
    public Guid SubagentId { get; init; }

    /// <summary>Session ID.</summary>
    public long SessionId { get; init; }

    /// <summary>Whether subagent completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Result from subagent execution.</summary>
    public string? Result { get; init; }

    /// <summary>Error if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Execution duration.</summary>
    public TimeSpan Duration { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// AGENT REGISTRY EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when a custom agent is registered.
/// </summary>
public record AgentRegisteredEvent : EventBase
{
    /// <summary>Agent ID.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Agent name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Agent slug.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Agent category.</summary>
    public AgentCategory Category { get; init; }
}

/// <summary>
/// Raised when a custom agent is deleted.
/// </summary>
public record AgentDeletedEvent : EventBase
{
    /// <summary>Agent ID.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Agent slug.</summary>
    public string Slug { get; init; } = string.Empty;
}
