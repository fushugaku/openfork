namespace OpenFork.Core.Config;

/// <summary>
/// Agent configuration from appsettings.json.
/// Supports both main agents and subagents with clear type distinction.
/// </summary>
public class AgentProfileConfig
{
    /// <summary>Display name for the agent.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-friendly identifier. If not provided, derived from Name.</summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Agent type: "main" or "subagent".
    /// - "main": Primary agents visible to users, can spawn subagents.
    /// - "subagent": Spawned by main agents via Task tool, cannot spawn their own subagents.
    /// </summary>
    public string Type { get; set; } = "main";

    /// <summary>Description of the agent's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// List of subagent slugs this agent can spawn (only for type="main").
    /// Empty list means all subagents are allowed.
    /// </summary>
    public List<string>? Subagents { get; set; }

    /// <summary>
    /// Model name from any provider's AvailableModels.
    /// The system will automatically find which provider has this model.
    /// If not specified, uses DefaultModel from settings.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>Temperature for generation (0.0-2.0).</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Maximum tokens for response.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Maximum iterations for agentic execution. 0 means use default.</summary>
    public int MaxIterations { get; set; } = 0;

    /// <summary>
    /// Maximum concurrent instances of this subagent that can run simultaneously.
    /// Only applies to subagents. 0 means unlimited, 1 means sequential execution.
    /// When limit is reached, additional requests are queued.
    /// </summary>
    public int MaxConcurrentInstances { get; set; } = 1;

    /// <summary>System prompt for the agent.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>Whether to prepend default system prefix.</summary>
    public bool UseDefaultSystemPrefix { get; set; } = true;

    /// <summary>
    /// Execution mode: "agentic", "singleshot", "streaming", "planning".
    /// Default: "agentic".
    /// </summary>
    public string ExecutionMode { get; set; } = "agentic";

    /// <summary>Tool configuration for this agent.</summary>
    public ToolsConfig? Tools { get; set; }

    /// <summary>Icon emoji for display.</summary>
    public string? Icon { get; set; }

    /// <summary>Color for display (hex, e.g., "#3B82F6").</summary>
    public string? Color { get; set; }

    /// <summary>Display order in lists (lower = first).</summary>
    public int DisplayOrder { get; set; } = 100;

    /// <summary>Whether agent is visible in UI.</summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Tool configuration for an agent.
/// </summary>
public class ToolsConfig
{
    /// <summary>
    /// Tool filtering mode:
    /// - "All": All tools available
    /// - "AllExcept": All except tools in List
    /// - "OnlyThese": Only tools in List
    /// - "None": No tools
    /// </summary>
    public string Mode { get; set; } = "All";

    /// <summary>
    /// Tools to include (if Mode is "OnlyThese") or exclude (if Mode is "AllExcept").
    /// </summary>
    public List<string>? List { get; set; }
}
