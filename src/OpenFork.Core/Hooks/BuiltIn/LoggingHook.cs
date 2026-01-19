using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Hooks.BuiltIn;

/// <summary>
/// Built-in hook that logs execution details.
/// </summary>
public class LoggingHook : IHook
{
    private readonly ILogger _logger;

    public string Id { get; }
    public string Name => $"Execution Logger ({Trigger})";
    public HookTrigger Trigger { get; }
    public int Priority => 0; // Run first
    public bool Enabled => true;

    public LoggingHook(HookTrigger trigger, ILogger logger)
    {
        Id = $"builtin-logging-{trigger.ToString().ToLowerInvariant()}";
        Trigger = trigger;
        _logger = logger;
    }

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        var logMessage = Trigger switch
        {
            HookTrigger.PreTool => $"Executing tool: {context.ToolName}",
            HookTrigger.PostTool => $"Tool {context.ToolName} completed in {context.Duration?.TotalMilliseconds:F1}ms",
            HookTrigger.PreCommand => $"Running command: {Truncate(context.Command, 100)}",
            HookTrigger.PostCommand => $"Command exited with code {context.ExitCode}",
            HookTrigger.PreEdit => $"Editing file: {context.FilePath}",
            HookTrigger.PostEdit => $"File edited: {context.FilePath}",
            HookTrigger.PreMessage => $"Sending message ({context.Role})",
            HookTrigger.PostMessage => $"Received response",
            HookTrigger.SessionStart => $"Session started",
            HookTrigger.SessionEnd => $"Session ended",
            HookTrigger.PreAgentLoop => $"Agent loop iteration {context.IterationNumber}",
            HookTrigger.PostAgentLoop => $"Agent loop iteration {context.IterationNumber} completed",
            HookTrigger.MaxIterations => $"Max iterations ({context.MaxIterations}) reached",
            HookTrigger.OnError => $"Error: {context.ErrorMessage}",
            HookTrigger.OnWarning => $"Warning: {context.ErrorMessage}",
            _ => $"Hook trigger: {Trigger}"
        };

        _logger.LogDebug("{Message}", logMessage);

        return Task.FromResult(HookResult.Ok());
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null) return null;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
