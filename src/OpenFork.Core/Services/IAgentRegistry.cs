using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

/// <summary>
/// Registry for managing agents (both built-in and custom).
/// </summary>
public interface IAgentRegistry
{
    // ═══════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Initializes the registry by loading custom agents from storage.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════
    // QUERY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets an agent by its unique ID.
    /// </summary>
    Agent? GetById(Guid id);

    /// <summary>
    /// Gets an agent by its URL-friendly slug.
    /// </summary>
    Agent? GetBySlug(string slug);

    /// <summary>
    /// Gets all registered agents.
    /// </summary>
    IReadOnlyList<Agent> GetAll();

    /// <summary>
    /// Gets agents by category.
    /// </summary>
    IReadOnlyList<Agent> GetByCategory(AgentCategory category);

    /// <summary>
    /// Gets all visible agents (for UI display).
    /// </summary>
    IReadOnlyList<Agent> GetVisible();

    /// <summary>
    /// Gets all subagent types.
    /// </summary>
    IReadOnlyList<Agent> GetSubagentTypes();

    /// <summary>
    /// Gets the default agent for new sessions.
    /// </summary>
    Agent GetDefault();

    // ═══════════════════════════════════════════════════════════════
    // MANAGEMENT (for custom agents)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Registers a new custom agent.
    /// </summary>
    Task<Agent> RegisterAsync(Agent agent, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing custom agent.
    /// </summary>
    Task UpdateAsync(Agent agent, CancellationToken ct = default);

    /// <summary>
    /// Deletes a custom agent.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════
    // VALIDATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates an agent configuration.
    /// </summary>
    bool ValidateAgent(Agent agent, out List<string> errors);

    /// <summary>
    /// Checks if a parent agent can spawn a specific subagent type.
    /// </summary>
    bool CanSpawnSubagent(Agent parent, string subagentSlug);
}
