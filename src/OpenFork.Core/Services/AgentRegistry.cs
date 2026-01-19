using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Agents;
using OpenFork.Core.Config;
using OpenFork.Core.Domain;
using OpenFork.Core.Permissions;

namespace OpenFork.Core.Services;

/// <summary>
/// Registry service for managing agents.
/// Loads built-in agents and agents defined in appsettings.json.
/// </summary>
public class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<Guid, Agent> _agents = new();
    private readonly ConcurrentDictionary<string, Agent> _agentsBySlug = new();
    private readonly AppSettings _settings;
    private readonly IProviderResolver _providerResolver;
    private readonly ILogger<AgentRegistry> _logger;
    private readonly string _defaultAgentSlug;
    private bool _initialized;

    public AgentRegistry(AppSettings settings, IProviderResolver providerResolver, ILogger<AgentRegistry> logger)
    {
        _settings = settings;
        _providerResolver = providerResolver;
        _logger = logger;
        _defaultAgentSlug = settings.DefaultAgentSlug ?? "coder";

        // Register built-in agents
        foreach (var agent in BuiltInAgents.All)
        {
            _agents[agent.Id] = agent;
            _agentsBySlug[agent.Slug] = agent;
        }
    }

    /// <summary>
    /// Initializes the registry by loading agents from appsettings.json.
    /// Config agents override built-in agents with the same slug.
    /// </summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return Task.CompletedTask;

        try
        {
            var configAgents = LoadAgentsFromConfig();
            foreach (var agent in configAgents)
            {
                // Remove existing agent with same slug (for overrides)
                if (_agentsBySlug.TryGetValue(agent.Slug, out var existing))
                {
                    _agents.TryRemove(existing.Id, out _);
                }

                _agents[agent.Id] = agent;
                _agentsBySlug[agent.Slug] = agent;
            }

            _logger.LogInformation(
                "Agent registry initialized: {BuiltIn} built-in, {Config} from config",
                BuiltInAgents.All.Count,
                configAgents.Count);

            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize agent registry");
            _initialized = true; // Mark as initialized to avoid repeated failures
        }

        return Task.CompletedTask;
    }

    private List<Agent> LoadAgentsFromConfig()
    {
        var agents = new List<Agent>();

        foreach (var config in _settings.Agents)
        {
            try
            {
                var agent = MapConfigToAgent(config);
                agents.Add(agent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load agent '{Name}' from config", config.Name);
            }
        }

        return agents;
    }

    private Agent MapConfigToAgent(AgentProfileConfig config)
    {
        var slug = config.Slug ?? GenerateSlug(config.Name);
        var isSubagent = config.Type.Equals("subagent", StringComparison.OrdinalIgnoreCase);

        // Try to find existing built-in agent to get ID for overrides
        var existingId = _agentsBySlug.TryGetValue(slug, out var existing)
            ? existing.Id
            : Guid.NewGuid();

        // Resolve model name to provider
        var modelName = config.Model ?? _settings.DefaultModel ?? string.Empty;
        var (providerId, modelId) = ResolveModelToProvider(modelName);

        var agent = new Agent
        {
            Id = existingId,
            Name = config.Name,
            Slug = slug,
            Description = config.Description ?? string.Empty,
            Category = isSubagent ? AgentCategory.Subagent : AgentCategory.Primary,
            ProviderId = providerId,
            ModelId = modelId,
            Temperature = config.Temperature,
            MaxTokens = config.MaxTokens,
            SystemPrompt = config.SystemPrompt,
            UseDefaultSystemPrefix = config.UseDefaultSystemPrefix,
            ExecutionMode = ParseExecutionMode(config.ExecutionMode),
            MaxIterations = config.MaxIterations > 0 ? config.MaxIterations : (isSubagent ? 15 : 30),
            MaxConcurrentInstances = config.MaxConcurrentInstances > 0 ? config.MaxConcurrentInstances : (isSubagent ? 1 : 0),
            CanSpawnSubagents = !isSubagent,
            AllowedSubagentTypes = config.Subagents ?? new List<string>(),
            Tools = MapToolsConfig(config.Tools),
            Permissions = isSubagent ? BuiltInRulesets.Explorer : BuiltInRulesets.Primary,
            IconEmoji = config.Icon,
            Color = config.Color,
            DisplayOrder = config.DisplayOrder,
            IsVisible = config.IsVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            IsBuiltIn = false
        };

        return agent;
    }

    private (string ProviderId, string ModelId) ResolveModelToProvider(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            // Fall back to default provider if no model specified
            return (_settings.DefaultProviderKey ?? string.Empty, string.Empty);
        }

        var resolved = _providerResolver.ResolveByModel(modelName);
        if (resolved.HasValue)
        {
            return (resolved.Value.ProviderKey, resolved.Value.Model.Name);
        }

        // Model not found in any provider - log warning and use as-is with default provider
        _logger.LogWarning("Model '{ModelName}' not found in any provider configuration", modelName);
        return (_settings.DefaultProviderKey ?? string.Empty, modelName);
    }

    private static string GenerateSlug(string name)
    {
        return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }

    private static AgentExecutionMode ParseExecutionMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "singleshot" => AgentExecutionMode.SingleShot,
            "streaming" => AgentExecutionMode.Streaming,
            "planning" => AgentExecutionMode.Planning,
            _ => AgentExecutionMode.Agentic
        };
    }

    private static ToolConfiguration MapToolsConfig(ToolsConfig? config)
    {
        if (config == null)
        {
            return new ToolConfiguration
            {
                Mode = ToolFilterMode.AllExcept,
                ToolList = new List<string>()
            };
        }

        var mode = config.Mode.ToLowerInvariant() switch
        {
            "all" => ToolFilterMode.All,
            "allexcept" => ToolFilterMode.AllExcept,
            "onlythese" => ToolFilterMode.OnlyThese,
            "none" => ToolFilterMode.None,
            _ => ToolFilterMode.AllExcept
        };

        return new ToolConfiguration
        {
            Mode = mode,
            ToolList = config.List ?? new List<string>()
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // QUERY
    // ═══════════════════════════════════════════════════════════════

    public Agent? GetById(Guid id) =>
        _agents.TryGetValue(id, out var agent) ? agent : null;

    public Agent? GetBySlug(string slug) =>
        _agentsBySlug.TryGetValue(slug, out var agent) ? agent : null;

    public IReadOnlyList<Agent> GetAll() =>
        _agents.Values.ToList();

    public IReadOnlyList<Agent> GetByCategory(AgentCategory category) =>
        _agents.Values.Where(a => a.Category == category).ToList();

    public IReadOnlyList<Agent> GetVisible() =>
        _agents.Values
            .Where(a => a.IsVisible)
            .OrderBy(a => a.DisplayOrder)
            .ToList();

    public IReadOnlyList<Agent> GetSubagentTypes() =>
        _agents.Values
            .Where(a => a.Category == AgentCategory.Subagent)
            .ToList();

    public Agent GetDefault()
    {
        var agent = GetBySlug(_defaultAgentSlug);
        if (agent != null) return agent;

        // Fall back to Coder if configured default not found
        return BuiltInAgents.Coder;
    }

    // ═══════════════════════════════════════════════════════════════
    // MANAGEMENT (config-based agents are read-only at runtime)
    // ═══════════════════════════════════════════════════════════════

    public Task<Agent> RegisterAsync(Agent agent, CancellationToken ct = default)
    {
        throw new NotSupportedException("Agents are defined in appsettings.json. Runtime registration is not supported.");
    }

    public Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        throw new NotSupportedException("Agents are defined in appsettings.json. Runtime updates are not supported.");
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        throw new NotSupportedException("Agents are defined in appsettings.json. Runtime deletion is not supported.");
    }

    // ═══════════════════════════════════════════════════════════════
    // VALIDATION
    // ═══════════════════════════════════════════════════════════════

    public bool ValidateAgent(Agent agent, out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(agent.Name))
            errors.Add("Name is required");

        if (string.IsNullOrWhiteSpace(agent.Slug))
        {
            errors.Add("Slug is required");
        }
        else if (!Regex.IsMatch(agent.Slug, @"^[a-z0-9-]+$"))
        {
            errors.Add("Slug must be lowercase alphanumeric with hyphens only");
        }

        if (agent.MaxIterations < 1)
            errors.Add("MaxIterations must be at least 1");

        return errors.Count == 0;
    }

    public bool CanSpawnSubagent(Agent parent, string subagentSlug)
    {
        // Check if parent can spawn subagents
        if (!parent.CanSpawnSubagents)
            return false;

        // Check if subagent exists
        var subagent = GetBySlug(subagentSlug);
        if (subagent == null)
            return false;

        // Check if it's actually a subagent type
        if (subagent.Category != AgentCategory.Subagent)
            return false;

        // Empty list means all subagents allowed
        if (parent.AllowedSubagentTypes.Count == 0)
            return true;

        return parent.AllowedSubagentTypes.Contains(subagentSlug);
    }
}
