# Hooks System Implementation Guide

## Overview

The hooks system provides extensibility points for pre/post execution events, enabling custom logic, logging, validation, and third-party integrations.

---

## Architecture Analysis

### Current State (OpenFork)

```
┌─────────────────────────────────────────┐
│          No Hook System                  │
│                                          │
│   Tool Call ────► Execute ────► Result  │
│                                          │
│   (No interception points)              │
└─────────────────────────────────────────┘
```

### Target State (Hooks-enabled)

```
┌─────────────────────────────────────────────────────────────────┐
│                      Hook System Architecture                    │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    Hook Pipeline                            │ │
│  │                                                             │ │
│  │   Event ──► Pre-Hooks ──► Action ──► Post-Hooks ──► Done   │ │
│  │               │                           │                 │ │
│  │               ▼                           ▼                 │ │
│  │         [Can Cancel]              [Can Modify Result]      │ │
│  │         [Can Modify]              [Logging/Metrics]        │ │
│  │         [Validation]              [Learning]               │ │
│  │                                                             │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    Hook Types                               │ │
│  │                                                             │ │
│  │  • pre-tool      • pre-edit       • pre-session            │ │
│  │  • post-tool     • post-edit      • post-session           │ │
│  │  • pre-command   • pre-message    • on-error               │ │
│  │  • post-command  • post-message   • on-warning             │ │
│  │                                                             │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

---

## Domain Model

### Hook Definitions

```csharp
namespace OpenFork.Core.Hooks;

/// <summary>
/// Defines when a hook should be executed.
/// </summary>
public enum HookTrigger
{
    // Tool hooks
    PreTool,        // Before any tool execution
    PostTool,       // After tool execution

    // Specific tool hooks
    PreEdit,        // Before file edit operations
    PostEdit,       // After file edit operations
    PreCommand,     // Before bash/command execution
    PostCommand,    // After bash/command execution

    // Message hooks
    PreMessage,     // Before sending message to LLM
    PostMessage,    // After receiving LLM response

    // Session hooks
    SessionStart,   // When session begins
    SessionEnd,     // When session ends

    // Error hooks
    OnError,        // When error occurs
    OnWarning,      // When warning is raised

    // Agent hooks
    PreAgentLoop,   // Before agent iteration
    PostAgentLoop,  // After agent iteration
    MaxIterations   // When max iterations reached
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
    public Guid SessionId { get; set; }
    public Guid? MessageId { get; set; }
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

    // Timing
    public DateTimeOffset StartTime { get; set; }
    public TimeSpan? Duration { get; set; }

    // Shared data between hooks
    public Dictionary<string, object> Data { get; set; } = new();
}
```

### Hook Configuration

```csharp
namespace OpenFork.Core.Hooks;

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
```

---

## Hook Service

```csharp
namespace OpenFork.Core.Services;

public interface IHookService
{
    /// <summary>Register a hook.</summary>
    void Register(IHook hook);

    /// <summary>Unregister a hook.</summary>
    void Unregister(string hookId);

    /// <summary>Execute all hooks for a trigger.</summary>
    Task<HookPipelineResult> ExecuteAsync(
        HookTrigger trigger,
        HookContext context,
        CancellationToken ct = default);

    /// <summary>Get all registered hooks.</summary>
    IReadOnlyList<IHook> GetHooks(HookTrigger? trigger = null);
}

public record HookPipelineResult
{
    public bool AllSucceeded { get; init; }
    public bool ShouldContinue { get; init; }
    public HookContext FinalContext { get; init; } = null!;
    public List<HookExecutionRecord> ExecutionRecords { get; init; } = new();
}

public record HookExecutionRecord
{
    public string HookId { get; init; } = string.Empty;
    public string HookName { get; init; } = string.Empty;
    public HookResult Result { get; init; } = null!;
    public TimeSpan Duration { get; init; }
}

public class HookService : IHookService
{
    private readonly ConcurrentDictionary<string, IHook> _hooks = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger<HookService> _logger;

    public HookService(IEventBus eventBus, ILogger<HookService> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    public void Register(IHook hook)
    {
        _hooks[hook.Id] = hook;
        _logger.LogDebug("Registered hook: {Name} ({Trigger})", hook.Name, hook.Trigger);
    }

    public void Unregister(string hookId)
    {
        _hooks.TryRemove(hookId, out _);
    }

    public async Task<HookPipelineResult> ExecuteAsync(
        HookTrigger trigger,
        HookContext context,
        CancellationToken ct = default)
    {
        var hooks = _hooks.Values
            .Where(h => h.Trigger == trigger && h.Enabled)
            .OrderBy(h => h.Priority)
            .ToList();

        if (hooks.Count == 0)
        {
            return new HookPipelineResult
            {
                AllSucceeded = true,
                ShouldContinue = true,
                FinalContext = context
            };
        }

        var records = new List<HookExecutionRecord>();
        var currentContext = context;
        var allSucceeded = true;
        var shouldContinue = true;

        foreach (var hook in hooks)
        {
            if (!shouldContinue && trigger.ToString().StartsWith("Pre"))
            {
                // Pre-hook cancelled, skip remaining
                break;
            }

            var startTime = DateTimeOffset.UtcNow;

            try
            {
                var result = await hook.ExecuteAsync(currentContext, ct);

                records.Add(new HookExecutionRecord
                {
                    HookId = hook.Id,
                    HookName = hook.Name,
                    Result = result,
                    Duration = DateTimeOffset.UtcNow - startTime
                });

                if (!result.Success)
                {
                    allSucceeded = false;
                    _logger.LogWarning("Hook {Name} failed: {Error}", hook.Name, result.Error);
                }

                if (!result.Continue)
                {
                    shouldContinue = false;
                    _logger.LogInformation("Hook {Name} cancelled pipeline: {Reason}",
                        hook.Name, result.Error);
                }

                // Apply modified context
                if (result.ModifiedContext != null)
                {
                    currentContext = result.ModifiedContext;
                }

                // Merge data
                if (result.Data != null)
                {
                    foreach (var (key, value) in result.Data)
                    {
                        currentContext.Data[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hook {Name} threw exception", hook.Name);
                allSucceeded = false;

                records.Add(new HookExecutionRecord
                {
                    HookId = hook.Id,
                    HookName = hook.Name,
                    Result = HookResult.Fail(ex.Message),
                    Duration = DateTimeOffset.UtcNow - startTime
                });
            }
        }

        await _eventBus.PublishAsync(new HookPipelineExecutedEvent
        {
            Trigger = trigger,
            HooksExecuted = records.Count,
            AllSucceeded = allSucceeded,
            ShouldContinue = shouldContinue
        }, ct);

        return new HookPipelineResult
        {
            AllSucceeded = allSucceeded,
            ShouldContinue = shouldContinue,
            FinalContext = currentContext,
            ExecutionRecords = records
        };
    }

    public IReadOnlyList<IHook> GetHooks(HookTrigger? trigger = null)
    {
        var query = _hooks.Values.AsEnumerable();
        if (trigger.HasValue)
        {
            query = query.Where(h => h.Trigger == trigger.Value);
        }
        return query.OrderBy(h => h.Priority).ToList();
    }
}
```

---

## Built-in Hooks

### Logging Hook

```csharp
namespace OpenFork.Core.Hooks.BuiltIn;

public class LoggingHook : IHook
{
    public string Id => "builtin-logging";
    public string Name => "Execution Logger";
    public HookTrigger Trigger { get; }
    public int Priority => 0;  // Run first
    public bool Enabled => true;

    private readonly ILogger<LoggingHook> _logger;

    public LoggingHook(HookTrigger trigger, ILogger<LoggingHook> logger)
    {
        Trigger = trigger;
        _logger = logger;
    }

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        var logMessage = Trigger switch
        {
            HookTrigger.PreTool => $"Executing tool: {context.ToolName}",
            HookTrigger.PostTool => $"Tool {context.ToolName} completed in {context.Duration?.TotalMilliseconds}ms",
            HookTrigger.PreCommand => $"Running command: {context.Command?.Truncate(100)}",
            HookTrigger.PostCommand => $"Command exited with code {context.ExitCode}",
            HookTrigger.PreEdit => $"Editing file: {context.FilePath}",
            HookTrigger.PostEdit => $"File edited: {context.FilePath}",
            HookTrigger.OnError => $"Error: {context.ErrorMessage}",
            _ => $"Hook trigger: {Trigger}"
        };

        _logger.LogInformation("{Message}", logMessage);

        return Task.FromResult(HookResult.Ok());
    }
}
```

### Metrics Hook

```csharp
namespace OpenFork.Core.Hooks.BuiltIn;

public class MetricsHook : IHook
{
    public string Id => "builtin-metrics";
    public string Name => "Metrics Collector";
    public HookTrigger Trigger { get; }
    public int Priority => 1000;  // Run last
    public bool Enabled => true;

    private readonly IMetricsCollector _metrics;

    public MetricsHook(HookTrigger trigger, IMetricsCollector metrics)
    {
        Trigger = trigger;
        _metrics = metrics;
    }

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        switch (Trigger)
        {
            case HookTrigger.PostTool:
                _metrics.RecordHistogram(
                    "tool_execution_duration_ms",
                    context.Duration?.TotalMilliseconds ?? 0,
                    new() { ["tool"] = context.ToolName ?? "unknown" });
                _metrics.IncrementCounter(
                    "tool_executions_total",
                    new() { ["tool"] = context.ToolName ?? "unknown" });
                break;

            case HookTrigger.PostCommand:
                _metrics.RecordHistogram(
                    "command_execution_duration_ms",
                    context.Duration?.TotalMilliseconds ?? 0);
                _metrics.IncrementCounter(
                    "command_executions_total",
                    new() { ["exit_code"] = context.ExitCode?.ToString() ?? "unknown" });
                break;

            case HookTrigger.OnError:
                _metrics.IncrementCounter("errors_total");
                break;
        }

        return Task.FromResult(HookResult.Ok());
    }
}
```

### Validation Hook

```csharp
namespace OpenFork.Core.Hooks.BuiltIn;

public class CommandValidationHook : IHook
{
    public string Id => "builtin-command-validation";
    public string Name => "Command Validator";
    public HookTrigger Trigger => HookTrigger.PreCommand;
    public int Priority => 10;
    public bool Enabled => true;

    private static readonly HashSet<string> DangerousCommands = new()
    {
        "rm -rf /",
        "rm -rf /*",
        ":(){ :|:& };:",  // Fork bomb
        "dd if=/dev/zero of=/dev/sda",
        "mkfs.",
        "> /dev/sda"
    };

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        var command = context.Command ?? "";

        // Check for dangerous commands
        foreach (var dangerous in DangerousCommands)
        {
            if (command.Contains(dangerous, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(HookResult.Cancel(
                    $"Dangerous command pattern detected: {dangerous}"));
            }
        }

        // Check for sudo without user acknowledgment
        if (command.StartsWith("sudo ", StringComparison.OrdinalIgnoreCase))
        {
            // Could trigger permission check instead of cancelling
            context.Data["requires_elevation"] = true;
        }

        return Task.FromResult(HookResult.Ok());
    }
}
```

### File Backup Hook

```csharp
namespace OpenFork.Core.Hooks.BuiltIn;

public class FileBackupHook : IHook
{
    public string Id => "builtin-file-backup";
    public string Name => "File Backup";
    public HookTrigger Trigger => HookTrigger.PreEdit;
    public int Priority => 50;
    public bool Enabled => true;

    private readonly string _backupDirectory;
    private readonly ILogger<FileBackupHook> _logger;

    public FileBackupHook(IOptions<HookSettings> settings, ILogger<FileBackupHook> logger)
    {
        _backupDirectory = settings.Value.BackupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "openfork", "backups");
        _logger = logger;
        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(context.FilePath) || !File.Exists(context.FilePath))
        {
            return HookResult.Ok();
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = Path.GetFileName(context.FilePath);
            var backupName = $"{fileName}.{timestamp}.bak";
            var backupPath = Path.Combine(_backupDirectory, backupName);

            File.Copy(context.FilePath, backupPath, overwrite: true);

            context.Data["backup_path"] = backupPath;
            _logger.LogDebug("Created backup: {Path}", backupPath);

            return HookResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create backup for {Path}", context.FilePath);
            // Don't block edit, just log warning
            return HookResult.Ok();
        }
    }
}
```

---

## Custom Hook Types

### Command Hook

```csharp
namespace OpenFork.Core.Hooks;

public class CommandHook : IHook
{
    private readonly HookConfig _config;
    private readonly ILogger<CommandHook> _logger;

    public string Id => _config.Id;
    public string Name => _config.Name;
    public HookTrigger Trigger => _config.Trigger;
    public int Priority => _config.Priority;
    public bool Enabled => _config.Enabled;

    public CommandHook(HookConfig config, ILogger<CommandHook> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.Command))
        {
            return HookResult.Fail("No command configured");
        }

        // Build environment with context data
        var env = new Dictionary<string, string>
        {
            ["HOOK_TRIGGER"] = Trigger.ToString(),
            ["HOOK_SESSION_ID"] = context.SessionId.ToString(),
            ["HOOK_TOOL_NAME"] = context.ToolName ?? "",
            ["HOOK_FILE_PATH"] = context.FilePath ?? "",
            ["HOOK_COMMAND"] = context.Command ?? ""
        };

        // Pass context as JSON via stdin
        var contextJson = JsonSerializer.Serialize(context);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_config.Timeout);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bash",
                    Arguments = $"-c \"{_config.Command}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            foreach (var (key, value) in env)
            {
                process.StartInfo.EnvironmentVariables[key] = value;
            }

            process.Start();

            await process.StandardInput.WriteLineAsync(contextJson);
            process.StandardInput.Close();

            await process.WaitForExitAsync(cts.Token);

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Command hook {Name} exited with code {Code}: {Error}",
                    Name, process.ExitCode, error);

                if (_config.ContinueOnError)
                {
                    return HookResult.Ok();
                }

                return HookResult.Fail(error);
            }

            // Parse output for special directives
            if (output.Contains("HOOK_CANCEL:"))
            {
                var reason = output.Split("HOOK_CANCEL:").LastOrDefault()?.Trim();
                return HookResult.Cancel(reason ?? "Cancelled by hook");
            }

            return HookResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command hook {Name} failed", Name);
            return _config.ContinueOnError ? HookResult.Ok() : HookResult.Fail(ex.Message);
        }
    }
}
```

### Webhook Hook

```csharp
namespace OpenFork.Core.Hooks;

public class WebhookHook : IHook
{
    private readonly HookConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookHook> _logger;

    public string Id => _config.Id;
    public string Name => _config.Name;
    public HookTrigger Trigger => _config.Trigger;
    public int Priority => _config.Priority;
    public bool Enabled => _config.Enabled;

    public WebhookHook(
        HookConfig config,
        HttpClient httpClient,
        ILogger<WebhookHook> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.WebhookUrl))
        {
            return HookResult.Fail("No webhook URL configured");
        }

        var payload = new
        {
            trigger = Trigger.ToString(),
            timestamp = DateTimeOffset.UtcNow,
            sessionId = context.SessionId,
            data = new
            {
                toolName = context.ToolName,
                filePath = context.FilePath,
                command = context.Command,
                error = context.ErrorMessage
            }
        };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_config.Timeout);

            var response = await _httpClient.PostAsJsonAsync(
                _config.WebhookUrl,
                payload,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook {Name} returned {Status}",
                    Name, response.StatusCode);

                if (_config.ContinueOnError)
                    return HookResult.Ok();

                return HookResult.Fail($"Webhook returned {response.StatusCode}");
            }

            return HookResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook hook {Name} failed", Name);
            return _config.ContinueOnError ? HookResult.Ok() : HookResult.Fail(ex.Message);
        }
    }
}
```

---

## Integration with Tool Execution

```csharp
// In ToolRegistry or ChatService
public async Task<ToolResult> ExecuteToolWithHooksAsync(
    string toolName,
    JsonNode arguments,
    ToolContext toolContext,
    CancellationToken ct = default)
{
    // Pre-hook
    var preContext = new HookContext
    {
        SessionId = toolContext.SessionId,
        ToolName = toolName,
        ToolArguments = arguments,
        StartTime = DateTimeOffset.UtcNow
    };

    var preResult = await _hookService.ExecuteAsync(HookTrigger.PreTool, preContext, ct);

    if (!preResult.ShouldContinue)
    {
        return ToolResult.Failure($"Blocked by pre-hook: {preResult.ExecutionRecords.LastOrDefault()?.Result.Error}");
    }

    // Execute tool
    var startTime = DateTimeOffset.UtcNow;
    var result = await ExecuteToolAsync(toolName, arguments, toolContext, ct);
    var duration = DateTimeOffset.UtcNow - startTime;

    // Post-hook
    var postContext = new HookContext
    {
        SessionId = toolContext.SessionId,
        ToolName = toolName,
        ToolArguments = arguments,
        ToolResult = result,
        Duration = duration,
        StartTime = startTime
    };

    await _hookService.ExecuteAsync(HookTrigger.PostTool, postContext, ct);

    return result;
}
```

---

## Configuration

### appsettings.json

```json
{
  "Hooks": {
    "Enabled": true,
    "BackupDirectory": "data/backups",
    "BuiltIn": {
      "Logging": true,
      "Metrics": true,
      "CommandValidation": true,
      "FileBackup": true
    },
    "Custom": [
      {
        "Id": "notify-slack",
        "Name": "Slack Notification",
        "Trigger": "OnError",
        "Type": "Webhook",
        "WebhookUrl": "${SLACK_WEBHOOK_URL}",
        "Priority": 500,
        "Enabled": true
      },
      {
        "Id": "audit-log",
        "Name": "Audit Logger",
        "Trigger": "PostEdit",
        "Type": "Command",
        "Command": "echo \"$HOOK_FILE_PATH edited\" >> /var/log/openfork-audit.log",
        "Priority": 900,
        "ContinueOnError": true
      }
    ]
  }
}
```

### .openfork/hooks.json (Project-level)

```json
{
  "hooks": [
    {
      "id": "lint-on-edit",
      "name": "ESLint Check",
      "trigger": "PostEdit",
      "type": "Command",
      "command": "npx eslint --fix \"$HOOK_FILE_PATH\"",
      "pattern": "*.{js,ts,jsx,tsx}",
      "priority": 100
    },
    {
      "id": "test-on-edit",
      "name": "Run Related Tests",
      "trigger": "PostEdit",
      "type": "Command",
      "command": "npm test -- --findRelatedTests \"$HOOK_FILE_PATH\"",
      "pattern": "src/**/*.ts",
      "priority": 200,
      "continueOnError": true
    }
  ]
}
```

---

## Hook Loader

```csharp
namespace OpenFork.Core.Services;

public class HookLoader
{
    private readonly IHookService _hookService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<HookSettings> _settings;
    private readonly ILogger<HookLoader> _logger;

    public async Task LoadHooksAsync(CancellationToken ct = default)
    {
        // Load built-in hooks
        LoadBuiltInHooks();

        // Load from settings
        foreach (var config in _settings.Value.Custom)
        {
            var hook = CreateHookFromConfig(config);
            if (hook != null)
            {
                _hookService.Register(hook);
            }
        }

        // Load project-level hooks
        await LoadProjectHooksAsync(ct);
    }

    private void LoadBuiltInHooks()
    {
        if (_settings.Value.BuiltIn.Logging)
        {
            foreach (HookTrigger trigger in Enum.GetValues<HookTrigger>())
            {
                _hookService.Register(new LoggingHook(
                    trigger,
                    _serviceProvider.GetRequiredService<ILogger<LoggingHook>>()));
            }
        }

        if (_settings.Value.BuiltIn.Metrics)
        {
            var metricsHooks = new[] {
                HookTrigger.PostTool,
                HookTrigger.PostCommand,
                HookTrigger.OnError
            };

            foreach (var trigger in metricsHooks)
            {
                _hookService.Register(new MetricsHook(
                    trigger,
                    _serviceProvider.GetRequiredService<IMetricsCollector>()));
            }
        }

        if (_settings.Value.BuiltIn.CommandValidation)
        {
            _hookService.Register(new CommandValidationHook());
        }

        if (_settings.Value.BuiltIn.FileBackup)
        {
            _hookService.Register(new FileBackupHook(
                _settings,
                _serviceProvider.GetRequiredService<ILogger<FileBackupHook>>()));
        }
    }

    private IHook? CreateHookFromConfig(HookConfig config)
    {
        return config.Type switch
        {
            HookType.Command => new CommandHook(
                config,
                _serviceProvider.GetRequiredService<ILogger<CommandHook>>()),

            HookType.Webhook => new WebhookHook(
                config,
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<WebhookHook>>()),

            _ => null
        };
    }

    private async Task LoadProjectHooksAsync(CancellationToken ct)
    {
        var projectHooksPath = Path.Combine(Directory.GetCurrentDirectory(), ".openfork", "hooks.json");

        if (File.Exists(projectHooksPath))
        {
            var json = await File.ReadAllTextAsync(projectHooksPath, ct);
            var projectHooks = JsonSerializer.Deserialize<ProjectHooksConfig>(json);

            foreach (var config in projectHooks?.Hooks ?? Array.Empty<HookConfig>())
            {
                var hook = CreateHookFromConfig(config);
                if (hook != null)
                {
                    _hookService.Register(hook);
                    _logger.LogInformation("Loaded project hook: {Name}", config.Name);
                }
            }
        }
    }
}
```

---

## Testing

```csharp
[Fact]
public async Task PreHook_CanCancelExecution()
{
    var hookService = new HookService(Mock.Of<IEventBus>(), NullLogger<HookService>.Instance);

    hookService.Register(new TestHook(HookTrigger.PreTool, _ =>
        Task.FromResult(HookResult.Cancel("Test cancellation"))));

    var result = await hookService.ExecuteAsync(HookTrigger.PreTool, new HookContext());

    Assert.False(result.ShouldContinue);
}

[Fact]
public async Task PostHook_ReceivesToolResult()
{
    var capturedContext = default(HookContext);

    var hookService = new HookService(Mock.Of<IEventBus>(), NullLogger<HookService>.Instance);

    hookService.Register(new TestHook(HookTrigger.PostTool, ctx =>
    {
        capturedContext = ctx;
        return Task.FromResult(HookResult.Ok());
    }));

    await hookService.ExecuteAsync(HookTrigger.PostTool, new HookContext
    {
        ToolName = "read",
        ToolResult = ToolResult.Success("File contents"),
        Duration = TimeSpan.FromMilliseconds(50)
    });

    Assert.Equal("read", capturedContext?.ToolName);
    Assert.Equal(50, capturedContext?.Duration?.TotalMilliseconds);
}

[Fact]
public async Task HooksExecuteInPriorityOrder()
{
    var executionOrder = new List<string>();

    var hookService = new HookService(Mock.Of<IEventBus>(), NullLogger<HookService>.Instance);

    hookService.Register(new TestHook("high", HookTrigger.PreTool, 100, _ =>
    {
        executionOrder.Add("high");
        return Task.FromResult(HookResult.Ok());
    }));

    hookService.Register(new TestHook("low", HookTrigger.PreTool, 10, _ =>
    {
        executionOrder.Add("low");
        return Task.FromResult(HookResult.Ok());
    }));

    await hookService.ExecuteAsync(HookTrigger.PreTool, new HookContext());

    Assert.Equal(new[] { "low", "high" }, executionOrder);
}
```

---

## Migration Path

1. Add IHookService to DI
2. Create built-in hooks
3. Integrate with ToolRegistry
4. Add configuration schema
5. Implement project-level hooks loading
6. Create hook management UI
