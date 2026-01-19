using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for managing subagent executions via the task tool.
/// </summary>
public interface ISubagentService
{
    /// <summary>
    /// Creates a new subsession for a subagent task.
    /// </summary>
    Task<SubSession> CreateSubSessionAsync(
        long parentSessionId,
        long? parentMessageId,
        string agentSlug,
        string prompt,
        string? description = null,
        int maxIterations = 10,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a pending subsession.
    /// </summary>
    Task<SubSession> ExecuteSubSessionAsync(
        Guid subSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a subsession by ID.
    /// </summary>
    Task<SubSession?> GetSubSessionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Gets all subsessions for a parent session.
    /// </summary>
    Task<IReadOnlyList<SubSession>> GetSubSessionsForParentAsync(
        long parentSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels a running or pending subsession.
    /// </summary>
    Task CancelSubSessionAsync(Guid subSessionId, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Checks if the given agent can spawn a specific subagent type.
    /// </summary>
    bool CanSpawnSubagent(Agent parentAgent, string subagentSlug);

    /// <summary>
    /// Gets the number of currently running instances for an agent type.
    /// </summary>
    int GetRunningCount(string agentSlug);

    /// <summary>
    /// Gets the number of queued executions for an agent type.
    /// </summary>
    int GetQueueDepth(string agentSlug);

    /// <summary>
    /// Gets concurrency status for all active agent types.
    /// </summary>
    IReadOnlyDictionary<string, AgentConcurrencyStatus> GetConcurrencyStatus();
}
