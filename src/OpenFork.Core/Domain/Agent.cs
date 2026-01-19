using OpenFork.Core.Permissions;

namespace OpenFork.Core.Domain;

/// <summary>
/// Complete agent configuration with all capabilities.
/// </summary>
public class Agent
{
    // ═══════════════════════════════════════════════════════════════
    // IDENTITY
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-friendly identifier.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Description of the agent's purpose.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Agent category (Primary, Subagent, Hidden).</summary>
    public AgentCategory Category { get; set; } = AgentCategory.Primary;

    // ═══════════════════════════════════════════════════════════════
    // LLM CONFIGURATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Provider key for LLM.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Model identifier.</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Temperature for generation.</summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>Maximum tokens for response.</summary>
    public int? MaxTokens { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // PROMPTING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>System prompt for the agent.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>Variables for prompt templating.</summary>
    public Dictionary<string, string> PromptVariables { get; set; } = new();

    /// <summary>Whether to prepend default system prefix.</summary>
    public bool UseDefaultSystemPrefix { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════
    // EXECUTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Execution mode (Agentic, SingleShot, Streaming, Planning).</summary>
    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.Agentic;

    /// <summary>Maximum iterations for agentic execution.</summary>
    public int MaxIterations { get; set; } = 30;

    /// <summary>
    /// Maximum concurrent instances of this agent that can run simultaneously.
    /// Only applies to subagents. 0 means unlimited, 1 means sequential execution.
    /// When limit is reached, additional requests are queued.
    /// </summary>
    public int MaxConcurrentInstances { get; set; } = 1;

    /// <summary>Timeout for execution.</summary>
    public TimeSpan? Timeout { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // CAPABILITIES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Whether this agent can spawn subagents.</summary>
    public bool CanSpawnSubagents { get; set; }

    /// <summary>Allowed subagent type slugs (empty = all allowed).</summary>
    public List<string> AllowedSubagentTypes { get; set; } = new();

    // ═══════════════════════════════════════════════════════════════
    // TOOLS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Tool configuration for this agent.</summary>
    public ToolConfiguration Tools { get; set; } = new();

    // ═══════════════════════════════════════════════════════════════
    // PERMISSIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Permission ruleset for this agent.</summary>
    public PermissionRuleset Permissions { get; set; } = new();

    // ═══════════════════════════════════════════════════════════════
    // UI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Icon emoji for display.</summary>
    public string? IconEmoji { get; set; }

    /// <summary>Color for display (hex).</summary>
    public string? Color { get; set; }

    /// <summary>Display order in lists.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Whether agent is visible in UI.</summary>
    public bool IsVisible { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════
    // METADATA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>When the agent was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the agent was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Whether this is a built-in agent.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>
/// Tool configuration for an agent.
/// </summary>
public class ToolConfiguration
{
    /// <summary>Mode for tool filtering.</summary>
    public ToolFilterMode Mode { get; set; } = ToolFilterMode.AllExcept;

    /// <summary>Tools to include (if Mode is OnlyThese) or exclude (if Mode is AllExcept).</summary>
    public List<string> ToolList { get; set; } = new();

    /// <summary>Per-tool configuration overrides.</summary>
    public Dictionary<string, ToolOverride> Overrides { get; set; } = new();
}

/// <summary>
/// Per-tool configuration override.
/// </summary>
public record ToolOverride
{
    /// <summary>Override max output length for this tool.</summary>
    public int? MaxOutputLength { get; init; }

    /// <summary>Override timeout for this tool.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Default arguments to merge with invocation.</summary>
    public Dictionary<string, object>? DefaultArguments { get; init; }
}
