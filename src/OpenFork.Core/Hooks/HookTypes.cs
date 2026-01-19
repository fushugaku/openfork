using System.Text.Json.Nodes;
using OpenFork.Core.Tools;

namespace OpenFork.Core.Hooks;

/// <summary>
/// Defines when a hook should be executed.
/// </summary>
public enum HookTrigger
{
    // Tool hooks
    PreTool,
    PostTool,

    // Specific tool hooks
    PreEdit,
    PostEdit,
    PreCommand,
    PostCommand,

    // Message hooks
    PreMessage,
    PostMessage,

    // Session hooks
    SessionStart,
    SessionEnd,

    // Error hooks
    OnError,
    OnWarning,

    // Agent hooks
    PreAgentLoop,
    PostAgentLoop,
    MaxIterations
}

/// <summary>
/// Base interface for all hooks.
/// </summary>
public interface IHook
{
    /// <summary>Unique identifier for this hook.</summary>
    string Id { get; }

    /// <summary>Human-readable name.</summary>
    string Name { get; }

    /// <summary>When this hook triggers.</summary>
    HookTrigger Trigger { get; }

    /// <summary>Execution priority (lower = earlier).</summary>
    int Priority { get; }

    /// <summary>Whether this hook is enabled.</summary>
    bool Enabled { get; }

    /// <summary>Execute the hook.</summary>
    Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default);
}

/// <summary>
/// Result of hook execution.
/// </summary>
public record HookResult
{
    /// <summary>Whether the hook succeeded.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Whether to continue with the action (for pre-hooks).</summary>
    public bool Continue { get; init; } = true;

    /// <summary>Modified context (if hook changed something).</summary>
    public HookContext? ModifiedContext { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Data to pass to subsequent hooks.</summary>
    public Dictionary<string, object>? Data { get; init; }

    public static HookResult Ok() => new();
    public static HookResult Cancel(string reason) => new() { Continue = false, Error = reason };
    public static HookResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>
/// Context passed to hooks.
/// </summary>
public class HookContext
{
    // Identity
    public long? SessionId { get; set; }
    public long? MessageId { get; set; }
    public Guid? AgentId { get; set; }

    // Tool context (for tool hooks)
    public string? ToolName { get; set; }
    public JsonNode? ToolArguments { get; set; }
    public ToolResult? ToolResult { get; set; }

    // Command context (for command hooks)
    public string? Command { get; set; }
    public int? ExitCode { get; set; }
    public string? CommandOutput { get; set; }

    // File context (for edit hooks)
    public string? FilePath { get; set; }
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }

    // Message context (for message hooks)
    public string? MessageContent { get; set; }
    public string? Role { get; set; }

    // Error context
    public Exception? Exception { get; set; }
    public string? ErrorMessage { get; set; }

    // Agent context
    public int? IterationNumber { get; set; }
    public int? MaxIterations { get; set; }

    // Timing
    public DateTimeOffset StartTime { get; set; }
    public TimeSpan? Duration { get; set; }

    // Shared data between hooks
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Configuration for a hook.
/// </summary>
public class HookConfig
{
    /// <summary>Unique ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Hook name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hook trigger.</summary>
    public HookTrigger Trigger { get; set; }

    /// <summary>Execution priority.</summary>
    public int Priority { get; set; } = 100;

    /// <summary>Whether enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hook type.</summary>
    public HookType Type { get; set; }

    /// <summary>For command hooks: the command to execute.</summary>
    public string? Command { get; set; }

    /// <summary>For script hooks: the script path.</summary>
    public string? ScriptPath { get; set; }

    /// <summary>For webhook hooks: the URL.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Pattern filter (glob for files, regex for tools).</summary>
    public string? Pattern { get; set; }

    /// <summary>Timeout for hook execution.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Continue even if hook fails.</summary>
    public bool ContinueOnError { get; set; } = true;
}

public enum HookType
{
    /// <summary>Built-in hook (logging, metrics, etc.).</summary>
    BuiltIn,

    /// <summary>Shell command hook.</summary>
    Command,

    /// <summary>Script file hook (JS/TS).</summary>
    Script,

    /// <summary>Webhook URL hook.</summary>
    Webhook,

    /// <summary>Custom .NET hook.</summary>
    Custom
}
