using OpenFork.Core.Abstractions;
using OpenFork.Core.Config;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

/// <summary>
/// Manages agents loaded from appsettings.json configuration.
/// Agents are identified by name and have synthetic IDs for compatibility.
/// </summary>
public class AgentService
{
    private readonly List<AgentProfile> _agents;
    private readonly Dictionary<string, AgentProfile> _agentsByName;

    public AgentService(AppSettings settings, IProviderResolver providerResolver)
    {
        _agents = new List<AgentProfile>();
        _agentsByName = new Dictionary<string, AgentProfile>(StringComparer.OrdinalIgnoreCase);

        // Load agents from settings and assign synthetic IDs
        for (int i = 0; i < settings.Agents.Count; i++)
        {
            var config = settings.Agents[i];

            // Resolve model name to provider
            var modelName = config.Model ?? settings.DefaultModel ?? "";
            var (providerKey, resolvedModel) = ResolveModelToProvider(providerResolver, modelName, settings.DefaultProviderKey);

            var agent = new AgentProfile
            {
                Id = i + 1, // Synthetic ID (1-based)
                Name = config.Name,
                SystemPrompt = config.SystemPrompt,
                ProviderKey = providerKey,
                Model = resolvedModel,
                MaxIterations = config.MaxIterations,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _agents.Add(agent);
            _agentsByName[agent.Name] = agent;
        }
    }

    private static (string ProviderKey, string Model) ResolveModelToProvider(
        IProviderResolver resolver, string modelName, string? defaultProviderKey)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return (defaultProviderKey ?? "", "");
        }

        var resolved = resolver.ResolveByModel(modelName);
        if (resolved.HasValue)
        {
            return (resolved.Value.ProviderKey, resolved.Value.Model.Name);
        }

        // Model not found - use as-is with default provider
        return (defaultProviderKey ?? "", modelName);
    }

    public Task<List<AgentProfile>> ListAsync() => Task.FromResult(_agents.ToList());

    public Task<AgentProfile?> GetAsync(long id) =>
        Task.FromResult(_agents.FirstOrDefault(a => a.Id == id));

    public Task<AgentProfile?> GetByNameAsync(string name) =>
        Task.FromResult(_agentsByName.GetValueOrDefault(name));

    // These operations are no-ops for settings-based agents
    public Task<AgentProfile> UpsertAsync(AgentProfile agent)
    {
        // Settings-based agents are read-only
        // Return the agent as-is for compatibility
        return Task.FromResult(agent);
    }

    public Task DeleteAsync(long id)
    {
        // Settings-based agents cannot be deleted
        return Task.CompletedTask;
    }
}
