# Permission System Implementation Guide

## Overview

The permission system controls what actions agents and tools can perform, providing security boundaries and user control over autonomous agent behavior.

---

## Architecture Analysis

### Current State (OpenFork)

```
┌─────────────────────────────────────────┐
│            OpenFork Today               │
│                                         │
│  ┌───────────────────────────────────┐  │
│  │    Tool Execution (Unrestricted)  │  │
│  │                                   │  │
│  │    tool.ExecuteAsync(...)         │  │
│  │              │                    │  │
│  │              ▼                    │  │
│  │    [No Permission Check]          │  │
│  │              │                    │  │
│  │              ▼                    │  │
│  │    Execute Immediately            │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

**Security Risks**:
- Any tool can be invoked without consent
- No ability to restrict dangerous operations
- No audit trail of permission decisions
- No per-agent capability boundaries

### Target State (opencode-aligned)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Permission System                             │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐     │
│  │                   Tool Invocation                       │     │
│  │                         │                               │     │
│  │                         ▼                               │     │
│  │  ┌──────────────────────────────────────────────────┐  │     │
│  │  │              Permission Resolver                  │  │     │
│  │  │  ┌────────────────────────────────────────────┐  │  │     │
│  │  │  │ 1. Match tool+resource against ruleset     │  │  │     │
│  │  │  │ 2. Evaluate rules (last match wins)        │  │  │     │
│  │  │  │ 3. Return action: allow/deny/ask           │  │  │     │
│  │  │  └────────────────────────────────────────────┘  │  │     │
│  │  └──────────────────────────────────────────────────┘  │     │
│  │                         │                               │     │
│  │           ┌─────────────┼─────────────┐                │     │
│  │           ▼             ▼             ▼                │     │
│  │       [ALLOW]        [ASK]         [DENY]              │     │
│  │           │             │             │                │     │
│  │           ▼             ▼             ▼                │     │
│  │       Execute      Prompt User    Return Error         │     │
│  │                    for Approval                        │     │
│  └────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────┘
```

---

## Domain Model

### Permission Entities

```csharp
namespace OpenFork.Core.Permissions;

/// <summary>
/// Action to take when a permission rule matches.
/// </summary>
public enum PermissionAction
{
    /// <summary>Allow the operation without prompting.</summary>
    Allow,

    /// <summary>Deny the operation.</summary>
    Deny,

    /// <summary>Ask the user for confirmation.</summary>
    Ask
}

/// <summary>
/// A single permission rule that matches tool invocations.
/// </summary>
public record PermissionRule
{
    /// <summary>
    /// Pattern to match against. Supports wildcards.
    /// Format: "tool:resource" or "tool:*" or "*:*"
    /// Examples:
    /// - "bash:*" - Any bash command
    /// - "bash:rm *" - rm commands
    /// - "edit:/etc/*" - Edit files in /etc
    /// - "read:*.env" - Read .env files
    /// - "*:*" - All operations
    /// </summary>
    public string Pattern { get; init; } = "*:*";

    /// <summary>
    /// Action to take when this rule matches.
    /// </summary>
    public PermissionAction Action { get; init; } = PermissionAction.Ask;

    /// <summary>
    /// Optional reason/description for this rule.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Priority for rule ordering (higher = evaluated later, wins on match).
    /// </summary>
    public int Priority { get; init; } = 0;
}

/// <summary>
/// A collection of permission rules forming a complete ruleset.
/// </summary>
public record PermissionRuleset
{
    /// <summary>
    /// Rules evaluated in order. Last matching rule wins.
    /// </summary>
    public IReadOnlyList<PermissionRule> Rules { get; init; } = Array.Empty<PermissionRule>();

    /// <summary>
    /// Default action when no rules match.
    /// </summary>
    public PermissionAction DefaultAction { get; init; } = PermissionAction.Ask;

    /// <summary>
    /// Name of this ruleset for identification.
    /// </summary>
    public string? Name { get; init; }
}

/// <summary>
/// Result of a permission check.
/// </summary>
public record PermissionCheckResult
{
    public PermissionAction Action { get; init; }
    public string? Reason { get; init; }
    public PermissionRule? MatchedRule { get; init; }
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
}

/// <summary>
/// Result of user permission prompt.
/// </summary>
public record PermissionPromptResult
{
    public bool Granted { get; init; }
    public bool RememberChoice { get; init; }
    public PermissionScope RememberScope { get; init; }
    public string? UserReason { get; init; }
}

public enum PermissionScope
{
    ThisCall,       // Just this one invocation
    ThisSession,    // For the rest of this session
    ThisPattern,    // For this pattern permanently
    Always          // Always for this tool
}
```

### Tool-Permission Mapping

```csharp
namespace OpenFork.Core.Permissions;

/// <summary>
/// Maps tool names to permission categories.
/// </summary>
public static class ToolPermissionMapping
{
    /// <summary>
    /// Maps tool names to their permission category.
    /// Edit-based tools share the "edit" permission.
    /// </summary>
    public static string GetPermissionCategory(string toolName) => toolName switch
    {
        "edit" => "edit",
        "multiedit" => "edit",
        "write" => "edit",
        _ => toolName
    };

    /// <summary>
    /// Extracts the resource identifier from tool arguments.
    /// </summary>
    public static string ExtractResource(string toolName, JsonNode arguments) => toolName switch
    {
        "bash" => arguments["command"]?.GetValue<string>() ?? "*",
        "read" => arguments["file_path"]?.GetValue<string>() ?? "*",
        "edit" or "write" or "multiedit" => arguments["file_path"]?.GetValue<string>() ?? "*",
        "glob" => arguments["pattern"]?.GetValue<string>() ?? "*",
        "grep" => arguments["path"]?.GetValue<string>() ?? "*",
        "webfetch" => arguments["url"]?.GetValue<string>() ?? "*",
        "websearch" => arguments["query"]?.GetValue<string>() ?? "*",
        "task" => arguments["subagent_type"]?.GetValue<string>() ?? "*",
        _ => "*"
    };
}
```

---

## Permission Service

```csharp
namespace OpenFork.Core.Services;

public interface IPermissionService
{
    /// <summary>
    /// Check if an operation is permitted.
    /// </summary>
    Task<PermissionCheckResult> CheckAsync(
        PermissionRuleset ruleset,
        string tool,
        JsonNode arguments,
        CancellationToken ct = default);

    /// <summary>
    /// Prompt the user for permission.
    /// </summary>
    Task<PermissionPromptResult> PromptAsync(
        PermissionCheckResult check,
        CancellationToken ct = default);

    /// <summary>
    /// Merge multiple rulesets (e.g., session + agent).
    /// </summary>
    PermissionRuleset MergeRulesets(params PermissionRuleset[] rulesets);

    /// <summary>
    /// Add a remembered permission to the session.
    /// </summary>
    Task RememberPermissionAsync(
        Guid sessionId,
        PermissionRule rule,
        PermissionScope scope,
        CancellationToken ct = default);

    /// <summary>
    /// Get effective permissions for a session (base + remembered).
    /// </summary>
    Task<PermissionRuleset> GetSessionPermissionsAsync(
        Guid sessionId,
        CancellationToken ct = default);
}

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _repository;
    private readonly IUserPromptService _promptService;
    private readonly ILogger<PermissionService> _logger;

    // In-memory session permission cache
    private readonly ConcurrentDictionary<Guid, List<PermissionRule>> _sessionRules = new();

    public PermissionService(
        IPermissionRepository repository,
        IUserPromptService promptService,
        ILogger<PermissionService> logger)
    {
        _repository = repository;
        _promptService = promptService;
        _logger = logger;
    }

    public Task<PermissionCheckResult> CheckAsync(
        PermissionRuleset ruleset,
        string tool,
        JsonNode arguments,
        CancellationToken ct = default)
    {
        var category = ToolPermissionMapping.GetPermissionCategory(tool);
        var resource = ToolPermissionMapping.ExtractResource(tool, arguments);
        var pattern = $"{category}:{resource}";

        _logger.LogDebug("Checking permission for pattern: {Pattern}", pattern);

        // Evaluate rules (last match wins)
        PermissionRule? matchedRule = null;
        foreach (var rule in ruleset.Rules.OrderBy(r => r.Priority))
        {
            if (PatternMatches(rule.Pattern, pattern))
            {
                matchedRule = rule;
            }
        }

        var action = matchedRule?.Action ?? ruleset.DefaultAction;
        var reason = matchedRule?.Reason ?? (action == PermissionAction.Ask
            ? $"Requires confirmation for {category}"
            : null);

        return Task.FromResult(new PermissionCheckResult
        {
            Action = action,
            Reason = reason,
            MatchedRule = matchedRule,
            Tool = tool,
            Resource = resource
        });
    }

    public async Task<PermissionPromptResult> PromptAsync(
        PermissionCheckResult check,
        CancellationToken ct = default)
    {
        var message = BuildPromptMessage(check);

        var response = await _promptService.PromptAsync(new UserPromptRequest
        {
            Title = "Permission Required",
            Message = message,
            Options = new[]
            {
                new PromptOption { Key = "y", Label = "Yes, allow this" },
                new PromptOption { Key = "n", Label = "No, deny this" },
                new PromptOption { Key = "a", Label = "Always allow this pattern" },
                new PromptOption { Key = "s", Label = "Allow for this session" }
            },
            DefaultOption = "n"
        }, ct);

        return response.Key switch
        {
            "y" => new PermissionPromptResult
            {
                Granted = true,
                RememberChoice = false,
                RememberScope = PermissionScope.ThisCall
            },
            "a" => new PermissionPromptResult
            {
                Granted = true,
                RememberChoice = true,
                RememberScope = PermissionScope.ThisPattern
            },
            "s" => new PermissionPromptResult
            {
                Granted = true,
                RememberChoice = true,
                RememberScope = PermissionScope.ThisSession
            },
            _ => new PermissionPromptResult
            {
                Granted = false,
                RememberChoice = false
            }
        };
    }

    public PermissionRuleset MergeRulesets(params PermissionRuleset[] rulesets)
    {
        var allRules = rulesets
            .Where(r => r != null)
            .SelectMany(r => r.Rules)
            .OrderBy(r => r.Priority)
            .ToList();

        // Use most restrictive default action
        var defaultAction = rulesets
            .Where(r => r != null)
            .Select(r => r.DefaultAction)
            .OrderByDescending(a => (int)a)  // Deny > Ask > Allow
            .FirstOrDefault();

        return new PermissionRuleset
        {
            Rules = allRules,
            DefaultAction = defaultAction,
            Name = "Merged"
        };
    }

    public async Task RememberPermissionAsync(
        Guid sessionId,
        PermissionRule rule,
        PermissionScope scope,
        CancellationToken ct = default)
    {
        if (scope == PermissionScope.ThisCall)
            return;  // Nothing to remember

        if (scope == PermissionScope.ThisSession)
        {
            // Add to in-memory session cache
            _sessionRules.AddOrUpdate(
                sessionId,
                _ => new List<PermissionRule> { rule },
                (_, list) => { list.Add(rule); return list; });
        }
        else
        {
            // Persist to database
            await _repository.AddRuleAsync(sessionId, rule, scope, ct);
        }
    }

    public async Task<PermissionRuleset> GetSessionPermissionsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        // Load persisted rules
        var persistedRules = await _repository.GetRulesAsync(sessionId, ct);

        // Get session-scoped rules
        var sessionRules = _sessionRules.GetValueOrDefault(sessionId) ?? new List<PermissionRule>();

        return new PermissionRuleset
        {
            Rules = persistedRules.Concat(sessionRules).ToList(),
            DefaultAction = PermissionAction.Ask
        };
    }

    private static bool PatternMatches(string pattern, string target)
    {
        // Support wildcards: * matches any sequence
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(target, regexPattern, RegexOptions.IgnoreCase);
    }

    private static string BuildPromptMessage(PermissionCheckResult check)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The agent wants to use **{check.Tool}**");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(check.Resource) && check.Resource != "*")
        {
            sb.AppendLine($"Resource: `{check.Resource}`");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(check.Reason))
        {
            sb.AppendLine($"Reason: {check.Reason}");
        }

        return sb.ToString();
    }
}
```

---

## Built-in Rulesets

### Agent Permission Profiles

```csharp
namespace OpenFork.Core.Permissions;

public static class BuiltInRulesets
{
    /// <summary>
    /// Permissive ruleset for primary/build agents.
    /// Most operations allowed, dangerous ones ask.
    /// </summary>
    public static readonly PermissionRuleset Primary = new()
    {
        Name = "Primary Agent",
        DefaultAction = PermissionAction.Ask,
        Rules = new[]
        {
            // File operations - mostly allowed
            new PermissionRule { Pattern = "read:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "glob:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "grep:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "list:*", Action = PermissionAction.Allow, Priority = 10 },

            // Edit operations - allowed in project
            new PermissionRule { Pattern = "edit:src/*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "edit:tests/*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "edit:docs/*", Action = PermissionAction.Allow, Priority = 10 },

            // Sensitive paths - ask
            new PermissionRule { Pattern = "edit:*.env*", Action = PermissionAction.Ask, Priority = 20,
                Reason = "Environment file may contain secrets" },
            new PermissionRule { Pattern = "edit:*secret*", Action = PermissionAction.Ask, Priority = 20,
                Reason = "File may contain secrets" },
            new PermissionRule { Pattern = "edit:*password*", Action = PermissionAction.Ask, Priority = 20,
                Reason = "File may contain passwords" },
            new PermissionRule { Pattern = "edit:*credential*", Action = PermissionAction.Ask, Priority = 20,
                Reason = "File may contain credentials" },

            // System paths - deny
            new PermissionRule { Pattern = "edit:/etc/*", Action = PermissionAction.Deny, Priority = 30,
                Reason = "System configuration files are protected" },
            new PermissionRule { Pattern = "edit:/usr/*", Action = PermissionAction.Deny, Priority = 30,
                Reason = "System files are protected" },
            new PermissionRule { Pattern = "edit:~/*", Action = PermissionAction.Ask, Priority = 25,
                Reason = "Modifying home directory files" },

            // Bash - categorized by danger level
            new PermissionRule { Pattern = "bash:ls *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:cat *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:pwd", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:echo *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:git *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:npm *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:dotnet *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:cargo *", Action = PermissionAction.Allow, Priority = 10 },

            // Dangerous bash - ask
            new PermissionRule { Pattern = "bash:rm *", Action = PermissionAction.Ask, Priority = 20,
                Reason = "Delete operation" },
            new PermissionRule { Pattern = "bash:sudo *", Action = PermissionAction.Ask, Priority = 25,
                Reason = "Elevated privileges requested" },
            new PermissionRule { Pattern = "bash:chmod *", Action = PermissionAction.Ask, Priority = 20,
                Reason = "Permission change" },
            new PermissionRule { Pattern = "bash:curl *", Action = PermissionAction.Ask, Priority = 15,
                Reason = "Network request" },
            new PermissionRule { Pattern = "bash:wget *", Action = PermissionAction.Ask, Priority = 15,
                Reason = "Network download" },

            // Very dangerous - deny by default
            new PermissionRule { Pattern = "bash:rm -rf /*", Action = PermissionAction.Deny, Priority = 100,
                Reason = "Destructive system operation" },
            new PermissionRule { Pattern = "bash::(){ :|:& };:", Action = PermissionAction.Deny, Priority = 100,
                Reason = "Fork bomb detected" },

            // Subagents - allow primary to spawn
            new PermissionRule { Pattern = "task:*", Action = PermissionAction.Allow, Priority = 10 },

            // Web operations - ask (external network)
            new PermissionRule { Pattern = "webfetch:*", Action = PermissionAction.Ask, Priority = 15,
                Reason = "External web request" },
            new PermissionRule { Pattern = "websearch:*", Action = PermissionAction.Allow, Priority = 10 },

            // User interaction - always allow
            new PermissionRule { Pattern = "question:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "todo:*", Action = PermissionAction.Allow, Priority = 10 }
        }
    };

    /// <summary>
    /// Restricted ruleset for explore/research agents.
    /// Read-only operations only.
    /// </summary>
    public static readonly PermissionRuleset Explorer = new()
    {
        Name = "Explorer Agent",
        DefaultAction = PermissionAction.Deny,
        Rules = new[]
        {
            // Read operations only
            new PermissionRule { Pattern = "read:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "glob:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "grep:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "list:*", Action = PermissionAction.Allow, Priority = 10 },

            // Sensitive files - deny even for reading
            new PermissionRule { Pattern = "read:*.env*", Action = PermissionAction.Deny, Priority = 20 },
            new PermissionRule { Pattern = "read:*secret*", Action = PermissionAction.Deny, Priority = 20 },
            new PermissionRule { Pattern = "read:*password*", Action = PermissionAction.Deny, Priority = 20 },

            // No write operations
            new PermissionRule { Pattern = "edit:*", Action = PermissionAction.Deny, Priority = 100 },
            new PermissionRule { Pattern = "bash:*", Action = PermissionAction.Deny, Priority = 100 },

            // No subagents
            new PermissionRule { Pattern = "task:*", Action = PermissionAction.Deny, Priority = 100 }
        }
    };

    /// <summary>
    /// Minimal ruleset for planner agents.
    /// Read + todo only.
    /// </summary>
    public static readonly PermissionRuleset Planner = new()
    {
        Name = "Planner Agent",
        DefaultAction = PermissionAction.Deny,
        Rules = new[]
        {
            new PermissionRule { Pattern = "read:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "glob:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "grep:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "list:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "todo:*", Action = PermissionAction.Allow, Priority = 10 },

            // Sensitive files - deny
            new PermissionRule { Pattern = "read:*.env*", Action = PermissionAction.Deny, Priority = 20 },
            new PermissionRule { Pattern = "read:*secret*", Action = PermissionAction.Deny, Priority = 20 }
        }
    };

    /// <summary>
    /// Research agent with web access.
    /// </summary>
    public static readonly PermissionRuleset Researcher = new()
    {
        Name = "Researcher Agent",
        DefaultAction = PermissionAction.Deny,
        Rules = new[]
        {
            new PermissionRule { Pattern = "webfetch:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "websearch:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "read:*", Action = PermissionAction.Allow, Priority = 10 }
        }
    };
}
```

---

## Integration with Tool Execution

### Tool Registry Enhancement

```csharp
// Enhanced tool execution with permission checks
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly IPermissionService _permissionService;
    private readonly ILogger<ToolRegistry> _logger;

    public async Task<ToolResult> ExecuteWithPermissionAsync(
        string toolName,
        JsonNode arguments,
        ToolContext context,
        PermissionRuleset permissions,
        CancellationToken ct = default)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return ToolResult.Failure($"Unknown tool: {toolName}");
        }

        // Check permission
        var check = await _permissionService.CheckAsync(permissions, toolName, arguments, ct);

        switch (check.Action)
        {
            case PermissionAction.Allow:
                _logger.LogDebug("Permission allowed for {Tool}:{Resource}",
                    check.Tool, check.Resource);
                break;

            case PermissionAction.Deny:
                _logger.LogWarning("Permission denied for {Tool}:{Resource}: {Reason}",
                    check.Tool, check.Resource, check.Reason);
                return ToolResult.Failure($"Permission denied: {check.Reason}");

            case PermissionAction.Ask:
                var prompt = await _permissionService.PromptAsync(check, ct);
                if (!prompt.Granted)
                {
                    return ToolResult.Failure("Permission denied by user");
                }

                // Remember if requested
                if (prompt.RememberChoice)
                {
                    var rule = new PermissionRule
                    {
                        Pattern = $"{check.Tool}:{check.Resource}",
                        Action = PermissionAction.Allow,
                        Priority = 50,
                        Reason = $"Approved by user: {prompt.UserReason}"
                    };
                    await _permissionService.RememberPermissionAsync(
                        context.SessionId, rule, prompt.RememberScope, ct);
                }
                break;
        }

        // Execute tool
        return await tool.ExecuteAsync(arguments, context, ct);
    }
}
```

### ChatService Integration

```csharp
// In ChatService tool execution loop
foreach (var toolCall in response.ToolCalls)
{
    var arguments = JsonNode.Parse(toolCall.Function.Arguments)
        ?? throw new InvalidOperationException("Invalid tool arguments");

    // Get effective permissions (session + agent merged)
    var sessionPermissions = await _permissionService.GetSessionPermissionsAsync(
        session.Id, ct);
    var agentPermissions = GetAgentPermissions(session.ActiveAgentId);
    var effectivePermissions = _permissionService.MergeRulesets(
        sessionPermissions, agentPermissions);

    // Execute with permission check
    var result = await _toolRegistry.ExecuteWithPermissionAsync(
        toolCall.Function.Name,
        arguments,
        toolContext,
        effectivePermissions,
        ct);

    // Add result to messages
    messages.Add(new ChatMessage
    {
        Role = "tool",
        ToolCallId = toolCall.Id,
        Content = result.Output
    });
}
```

---

## User Prompt Service

```csharp
namespace OpenFork.Core.Services;

public interface IUserPromptService
{
    Task<PromptResponse> PromptAsync(
        UserPromptRequest request,
        CancellationToken ct = default);
}

public record UserPromptRequest
{
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<PromptOption> Options { get; init; } = Array.Empty<PromptOption>();
    public string? DefaultOption { get; init; }
    public TimeSpan? Timeout { get; init; }
}

public record PromptOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public record PromptResponse
{
    public string Key { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
}

// TUI Implementation
public class ConsolePromptService : IUserPromptService
{
    public async Task<PromptResponse> PromptAsync(
        UserPromptRequest request,
        CancellationToken ct = default)
    {
        // Use Spectre.Console SelectionPrompt
        var prompt = new SelectionPrompt<PromptOption>()
            .Title($"[yellow]{request.Title}[/]\n{request.Message}")
            .AddChoices(request.Options)
            .UseConverter(o => $"[{o.Key}] {o.Label}");

        var selection = await Task.Run(() => AnsiConsole.Prompt(prompt), ct);

        return new PromptResponse { Key = selection.Key };
    }
}
```

---

## Database Schema

```sql
-- Permission rules table (for persistent rules)
CREATE TABLE IF NOT EXISTS PermissionRules (
    Id TEXT PRIMARY KEY,
    SessionId TEXT,           -- NULL for global rules
    Pattern TEXT NOT NULL,
    Action TEXT NOT NULL,     -- 'Allow', 'Deny', 'Ask'
    Reason TEXT,
    Priority INTEGER DEFAULT 0,
    Scope TEXT NOT NULL,      -- 'Session', 'Pattern', 'Always'
    CreatedAt TEXT NOT NULL,
    CreatedBy TEXT,           -- 'user' or 'system'
    FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_permission_session ON PermissionRules(SessionId);
CREATE INDEX IF NOT EXISTS idx_permission_pattern ON PermissionRules(Pattern);

-- Permission audit log
CREATE TABLE IF NOT EXISTS PermissionAudit (
    Id TEXT PRIMARY KEY,
    SessionId TEXT NOT NULL,
    Tool TEXT NOT NULL,
    Resource TEXT NOT NULL,
    Action TEXT NOT NULL,
    MatchedRule TEXT,
    UserDecision TEXT,        -- NULL if not prompted
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_audit_session ON PermissionAudit(SessionId);
CREATE INDEX IF NOT EXISTS idx_audit_time ON PermissionAudit(CreatedAt);
```

---

## Configuration

```json
// appsettings.json
{
  "Permissions": {
    "DefaultAction": "Ask",
    "EnableAuditLog": true,
    "PromptTimeoutSeconds": 60,
    "AutoApproveReadOperations": true,
    "AgentProfiles": {
      "coder": "Primary",
      "planner": "Planner",
      "explorer": "Explorer",
      "researcher": "Researcher"
    },
    "GlobalRules": [
      {
        "Pattern": "read:*.env*",
        "Action": "Ask",
        "Reason": "Environment file may contain secrets"
      },
      {
        "Pattern": "bash:rm -rf *",
        "Action": "Deny",
        "Reason": "Dangerous delete operation"
      }
    ]
  }
}
```

---

## Testing Strategy

```csharp
[Fact]
public async Task CheckPermission_AllowRule_ReturnsAllow()
{
    var ruleset = new PermissionRuleset
    {
        Rules = new[] { new PermissionRule { Pattern = "read:*", Action = PermissionAction.Allow } }
    };

    var result = await _service.CheckAsync(ruleset, "read",
        JsonNode.Parse("""{"file_path": "/src/test.cs"}""")!);

    Assert.Equal(PermissionAction.Allow, result.Action);
}

[Fact]
public async Task CheckPermission_LastMatchWins()
{
    var ruleset = new PermissionRuleset
    {
        Rules = new[]
        {
            new PermissionRule { Pattern = "bash:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:rm *", Action = PermissionAction.Deny, Priority = 20 }
        }
    };

    var result = await _service.CheckAsync(ruleset, "bash",
        JsonNode.Parse("""{"command": "rm -rf /tmp/test"}""")!);

    Assert.Equal(PermissionAction.Deny, result.Action);
}

[Fact]
public async Task MergeRulesets_UsesHigherPriority()
{
    // Verify merge behavior
}

[Fact]
public async Task RememberPermission_PersistsForScope()
{
    // Verify persistence behavior
}
```

---

## Security Considerations

1. **Default Deny for Unknown**: Unknown tools default to Ask or Deny
2. **No Rule Bypass**: Permission check is mandatory before tool execution
3. **Audit Trail**: All permission decisions are logged
4. **User Override**: Users can always override via prompt
5. **Pattern Sanitization**: Patterns are validated to prevent injection
6. **Timeout**: Prompts have configurable timeout
7. **Subagent Inheritance**: Subagents cannot exceed parent permissions

---

## Migration Path

1. Add PermissionService to DI
2. Add ConsolePromptService for TUI
3. Create database tables
4. Update ToolRegistry with permission checks
5. Configure agent profiles in settings
6. Add audit logging
7. Test permission flows
