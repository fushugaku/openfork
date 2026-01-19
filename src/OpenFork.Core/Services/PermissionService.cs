using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Events;
using OpenFork.Core.Permissions;

namespace OpenFork.Core.Services;

/// <summary>
/// Implementation of the permission service with pattern matching and user prompting.
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly IUserPromptService _promptService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PermissionService> _logger;

    // In-memory session permission cache
    private readonly ConcurrentDictionary<Guid, List<PermissionRule>> _sessionRules = new();

    // Cache for compiled regex patterns
    private readonly ConcurrentDictionary<string, Regex> _patternCache = new();

    public PermissionService(
        IUserPromptService promptService,
        IEventBus eventBus,
        ILogger<PermissionService> logger)
    {
        _promptService = promptService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public Task<PermissionCheckResult> CheckAsync(
        PermissionRuleset ruleset,
        string tool,
        JsonNode? arguments,
        CancellationToken ct = default)
    {
        var pattern = ToolPermissionMapping.BuildPattern(tool, arguments);

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
            ? $"Requires confirmation for {tool}"
            : null);

        _logger.LogDebug("Permission check result: {Action} for {Tool}:{Resource}",
            action, tool, ToolPermissionMapping.ExtractResource(tool, arguments));

        return Task.FromResult(new PermissionCheckResult
        {
            Action = action,
            Reason = reason,
            MatchedRule = matchedRule,
            Tool = tool,
            Resource = ToolPermissionMapping.ExtractResource(tool, arguments)
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
                new PromptOption { Key = "y", Label = "Yes, allow this", Description = "Allow this specific operation" },
                new PromptOption { Key = "n", Label = "No, deny this", Description = "Deny this operation" },
                new PromptOption { Key = "a", Label = "Always allow this pattern", Description = "Remember for future operations" },
                new PromptOption { Key = "s", Label = "Allow for this session", Description = "Allow for this session only" }
            },
            DefaultOption = "n",
            Timeout = TimeSpan.FromMinutes(5)
        }, ct);

        if (response.TimedOut || response.Cancelled)
        {
            return new PermissionPromptResult
            {
                Granted = false,
                RememberChoice = false
            };
        }

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
            .OrderByDescending(a => (int)a) // Deny > Ask > Allow
            .FirstOrDefault();

        return new PermissionRuleset
        {
            Rules = allRules,
            DefaultAction = defaultAction,
            Name = "Merged"
        };
    }

    public Task RememberPermissionAsync(
        Guid sessionId,
        PermissionRule rule,
        PermissionScope scope,
        CancellationToken ct = default)
    {
        if (scope == PermissionScope.ThisCall)
            return Task.CompletedTask; // Nothing to remember

        if (scope == PermissionScope.ThisSession)
        {
            // Add to in-memory session cache
            _sessionRules.AddOrUpdate(
                sessionId,
                _ => new List<PermissionRule> { rule },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(rule);
                    }
                    return list;
                });

            _logger.LogInformation("Remembered permission for session {SessionId}: {Pattern} = {Action}",
                sessionId, rule.Pattern, rule.Action);
        }
        else
        {
            // For persistent rules, we'd store in database
            // For now, treat as session-scoped
            _sessionRules.AddOrUpdate(
                sessionId,
                _ => new List<PermissionRule> { rule },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(rule);
                    }
                    return list;
                });

            _logger.LogInformation("Remembered persistent permission: {Pattern} = {Action}",
                rule.Pattern, rule.Action);
        }

        return Task.CompletedTask;
    }

    public Task<PermissionRuleset> GetSessionPermissionsAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        // Get session-scoped rules
        var sessionRules = _sessionRules.GetValueOrDefault(sessionId);
        List<PermissionRule> rules;

        if (sessionRules != null)
        {
            lock (sessionRules)
            {
                rules = sessionRules.ToList();
            }
        }
        else
        {
            rules = new List<PermissionRule>();
        }

        return Task.FromResult(new PermissionRuleset
        {
            Rules = rules,
            DefaultAction = PermissionAction.Ask,
            Name = $"Session-{sessionId}"
        });
    }

    public void ClearSessionPermissions(Guid sessionId)
    {
        _sessionRules.TryRemove(sessionId, out _);
        _logger.LogDebug("Cleared session permissions for {SessionId}", sessionId);
    }

    public PermissionRuleset GetAgentRuleset(string agentName)
    {
        return BuiltInRulesets.GetByName(agentName) ?? BuiltInRulesets.Primary;
    }

    private bool PatternMatches(string pattern, string target)
    {
        var regex = _patternCache.GetOrAdd(pattern, p =>
        {
            // Support wildcards: * matches any sequence
            var regexPattern = "^" + Regex.Escape(p)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });

        return regex.IsMatch(target);
    }

    private static string BuildPromptMessage(PermissionCheckResult check)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The agent wants to use '{check.Tool}'");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(check.Resource) && check.Resource != "*")
        {
            // Truncate long resources
            var resource = check.Resource.Length > 100
                ? check.Resource[..97] + "..."
                : check.Resource;
            sb.AppendLine($"Resource: {resource}");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(check.Reason))
        {
            sb.AppendLine($"Reason: {check.Reason}");
        }

        return sb.ToString();
    }
}
