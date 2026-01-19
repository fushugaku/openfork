using System.Text.Json;
using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;
using OpenFork.Core.Permissions;

namespace OpenFork.Storage.Repositories;

/// <summary>
/// Repository for custom agent storage with JSON serialization for complex types.
/// </summary>
public class CustomAgentRepository : ICustomAgentRepository
{
    private readonly SqliteConnectionFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public CustomAgentRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<Agent>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<AgentRow>(
            "SELECT * FROM custom_agents ORDER BY display_order, name");

        return rows.Select(DeserializeAgent).ToList();
    }

    public async Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<AgentRow>(
            "SELECT * FROM custom_agents WHERE id = @Id",
            new { Id = id.ToString() });

        return row != null ? DeserializeAgent(row) : null;
    }

    public async Task<Agent?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<AgentRow>(
            "SELECT * FROM custom_agents WHERE slug = @Slug",
            new { Slug = slug });

        return row != null ? DeserializeAgent(row) : null;
    }

    public async Task<Agent> CreateAsync(Agent agent, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        if (agent.Id == Guid.Empty)
            agent.Id = Guid.NewGuid();

        if (agent.CreatedAt == default)
            agent.CreatedAt = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(
            """
            INSERT INTO custom_agents (
                id, name, slug, description, category, provider_id, model_id,
                temperature, max_tokens, system_prompt, prompt_variables_json,
                use_default_system_prefix, execution_mode, max_iterations, timeout_seconds,
                can_spawn_subagents, allowed_subagent_types_json, tools_config_json,
                permissions_json, icon_emoji, color, display_order, is_visible,
                created_at, updated_at
            ) VALUES (
                @Id, @Name, @Slug, @Description, @Category, @ProviderId, @ModelId,
                @Temperature, @MaxTokens, @SystemPrompt, @PromptVariablesJson,
                @UseDefaultSystemPrefix, @ExecutionMode, @MaxIterations, @TimeoutSeconds,
                @CanSpawnSubagents, @AllowedSubagentTypesJson, @ToolsConfigJson,
                @PermissionsJson, @IconEmoji, @Color, @DisplayOrder, @IsVisible,
                @CreatedAt, @UpdatedAt
            )
            """,
            MapToRow(agent));

        return agent;
    }

    public async Task UpdateAsync(Agent agent, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        agent.UpdatedAt = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(
            """
            UPDATE custom_agents SET
                name = @Name, slug = @Slug, description = @Description,
                category = @Category, provider_id = @ProviderId, model_id = @ModelId,
                temperature = @Temperature, max_tokens = @MaxTokens,
                system_prompt = @SystemPrompt, prompt_variables_json = @PromptVariablesJson,
                use_default_system_prefix = @UseDefaultSystemPrefix,
                execution_mode = @ExecutionMode, max_iterations = @MaxIterations,
                timeout_seconds = @TimeoutSeconds, can_spawn_subagents = @CanSpawnSubagents,
                allowed_subagent_types_json = @AllowedSubagentTypesJson,
                tools_config_json = @ToolsConfigJson, permissions_json = @PermissionsJson,
                icon_emoji = @IconEmoji, color = @Color, display_order = @DisplayOrder,
                is_visible = @IsVisible, updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            MapToRow(agent));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(
            "DELETE FROM custom_agents WHERE id = @Id",
            new { Id = id.ToString() });
    }

    private static Agent DeserializeAgent(AgentRow row)
    {
        return new Agent
        {
            Id = Guid.Parse(row.Id),
            Name = row.Name,
            Slug = row.Slug,
            Description = row.Description ?? string.Empty,
            Category = Enum.Parse<AgentCategory>(row.Category),
            ProviderId = row.ProviderId ?? string.Empty,
            ModelId = row.ModelId ?? string.Empty,
            Temperature = row.Temperature,
            MaxTokens = row.MaxTokens,
            SystemPrompt = row.SystemPrompt ?? string.Empty,
            PromptVariables = string.IsNullOrEmpty(row.PromptVariablesJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(row.PromptVariablesJson, JsonOptions)
                  ?? new Dictionary<string, string>(),
            UseDefaultSystemPrefix = row.UseDefaultSystemPrefix,
            ExecutionMode = Enum.Parse<AgentExecutionMode>(row.ExecutionMode),
            MaxIterations = row.MaxIterations,
            Timeout = row.TimeoutSeconds.HasValue ? TimeSpan.FromSeconds(row.TimeoutSeconds.Value) : null,
            CanSpawnSubagents = row.CanSpawnSubagents,
            AllowedSubagentTypes = string.IsNullOrEmpty(row.AllowedSubagentTypesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(row.AllowedSubagentTypesJson, JsonOptions)
                  ?? new List<string>(),
            Tools = string.IsNullOrEmpty(row.ToolsConfigJson)
                ? new ToolConfiguration()
                : JsonSerializer.Deserialize<ToolConfiguration>(row.ToolsConfigJson, JsonOptions)
                  ?? new ToolConfiguration(),
            Permissions = string.IsNullOrEmpty(row.PermissionsJson)
                ? new PermissionRuleset()
                : JsonSerializer.Deserialize<PermissionRuleset>(row.PermissionsJson, JsonOptions)
                  ?? new PermissionRuleset(),
            IconEmoji = row.IconEmoji,
            Color = row.Color,
            DisplayOrder = row.DisplayOrder,
            IsVisible = row.IsVisible,
            CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
            UpdatedAt = string.IsNullOrEmpty(row.UpdatedAt) ? null : DateTimeOffset.Parse(row.UpdatedAt),
            IsBuiltIn = false
        };
    }

    private static object MapToRow(Agent agent)
    {
        return new
        {
            Id = agent.Id.ToString(),
            agent.Name,
            agent.Slug,
            agent.Description,
            Category = agent.Category.ToString(),
            ProviderId = agent.ProviderId,
            ModelId = agent.ModelId,
            agent.Temperature,
            agent.MaxTokens,
            agent.SystemPrompt,
            PromptVariablesJson = JsonSerializer.Serialize(agent.PromptVariables, JsonOptions),
            agent.UseDefaultSystemPrefix,
            ExecutionMode = agent.ExecutionMode.ToString(),
            agent.MaxIterations,
            TimeoutSeconds = agent.Timeout?.TotalSeconds,
            agent.CanSpawnSubagents,
            AllowedSubagentTypesJson = JsonSerializer.Serialize(agent.AllowedSubagentTypes, JsonOptions),
            ToolsConfigJson = JsonSerializer.Serialize(agent.Tools, JsonOptions),
            PermissionsJson = JsonSerializer.Serialize(agent.Permissions, JsonOptions),
            agent.IconEmoji,
            agent.Color,
            agent.DisplayOrder,
            agent.IsVisible,
            CreatedAt = agent.CreatedAt.ToString("O"),
            UpdatedAt = agent.UpdatedAt?.ToString("O")
        };
    }

    private class AgentRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = "Primary";
        public string? ProviderId { get; set; }
        public string? ModelId { get; set; }
        public double Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public string? SystemPrompt { get; set; }
        public string? PromptVariablesJson { get; set; }
        public bool UseDefaultSystemPrefix { get; set; }
        public string ExecutionMode { get; set; } = "Agentic";
        public int MaxIterations { get; set; }
        public double? TimeoutSeconds { get; set; }
        public bool CanSpawnSubagents { get; set; }
        public string? AllowedSubagentTypesJson { get; set; }
        public string? ToolsConfigJson { get; set; }
        public string? PermissionsJson { get; set; }
        public string? IconEmoji { get; set; }
        public string? Color { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsVisible { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
    }
}
