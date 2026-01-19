using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

/// <summary>
/// Represents a pipeline tool definition loaded from a *.tool.json file.
/// Pipeline tools execute a sequence of subagent steps when invoked.
/// </summary>
public class PipelineToolDefinition
{
    /// <summary>
    /// Tool identifier used in tool calls.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description shown to the LLM for tool selection.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema for tool arguments (standard OpenAI format).
    /// </summary>
    [JsonPropertyName("parameters")]
    public JsonElement? Parameters { get; set; }

    /// <summary>
    /// Array of pipeline steps to execute sequentially.
    /// </summary>
    [JsonPropertyName("pipeline")]
    public List<PipelineStepDefinition> Pipeline { get; set; } = new();
}

/// <summary>
/// Represents a single step in a pipeline tool's execution sequence.
/// A step can execute either an agent (subagent) or a tool.
/// </summary>
public class PipelineStepDefinition
{
    /// <summary>
    /// Step type: "agent" or "tool". Defaults to "agent" if not specified.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "agent";

    /// <summary>
    /// Subagent slug to execute (when type is "agent").
    /// </summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; set; }

    /// <summary>
    /// Tool name to execute (when type is "tool").
    /// </summary>
    [JsonPropertyName("tool")]
    public string? Tool { get; set; }

    /// <summary>
    /// For agent steps: Prompt template with {{param}} placeholders.
    /// For tool steps: Arguments template (JSON) with {{param}} placeholders.
    /// </summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Alias for prompt - used for tool arguments to make JSON more readable.
    /// </summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    /// <summary>
    /// How to pass context to this step.
    /// "full" = all history from previous steps, "last" = only previous step's result, "none" = no context.
    /// </summary>
    [JsonPropertyName("handoff")]
    public string Handoff { get; set; } = "last";

    /// <summary>
    /// Optional name for this step (used in output).
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets the effective arguments/prompt for this step.
    /// </summary>
    public string GetEffectivePrompt() => Arguments ?? Prompt;

    /// <summary>
    /// Determines if this is a tool step.
    /// </summary>
    public bool IsTool => Type.Equals("tool", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(Tool);

    /// <summary>
    /// Gets the target (agent slug or tool name).
    /// </summary>
    public string GetTarget() => IsTool ? Tool ?? string.Empty : Agent ?? string.Empty;
}
