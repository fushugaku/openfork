namespace OpenFork.Core.Permissions;

/// <summary>
/// Built-in permission rulesets for different agent types.
/// </summary>
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
            new PermissionRule { Pattern = "codesearch:*", Action = PermissionAction.Allow, Priority = 10 },

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
            new PermissionRule { Pattern = "bash:python *", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "bash:pip *", Action = PermissionAction.Allow, Priority = 10 },

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
            new PermissionRule { Pattern = "bash:rm -rf /", Action = PermissionAction.Deny, Priority = 100,
                Reason = "Destructive system operation" },

            // Subagents - allow primary to spawn
            new PermissionRule { Pattern = "task:*", Action = PermissionAction.Allow, Priority = 10 },

            // Web operations - ask (external network)
            new PermissionRule { Pattern = "webfetch:*", Action = PermissionAction.Ask, Priority = 15,
                Reason = "External web request" },
            new PermissionRule { Pattern = "websearch:*", Action = PermissionAction.Allow, Priority = 10 },

            // User interaction - always allow
            new PermissionRule { Pattern = "question:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "todo:*", Action = PermissionAction.Allow, Priority = 10 },

            // LSP operations - allow
            new PermissionRule { Pattern = "lsp:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "diagnostics:*", Action = PermissionAction.Allow, Priority = 10 }
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
            new PermissionRule { Pattern = "codesearch:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "lsp:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "diagnostics:*", Action = PermissionAction.Allow, Priority = 10 },

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
            new PermissionRule { Pattern = "codesearch:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "todo:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "question:*", Action = PermissionAction.Allow, Priority = 10 },

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
            new PermissionRule { Pattern = "read:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "glob:*", Action = PermissionAction.Allow, Priority = 10 },
            new PermissionRule { Pattern = "grep:*", Action = PermissionAction.Allow, Priority = 10 },

            // Sensitive files - deny
            new PermissionRule { Pattern = "read:*.env*", Action = PermissionAction.Deny, Priority = 20 },
            new PermissionRule { Pattern = "read:*secret*", Action = PermissionAction.Deny, Priority = 20 }
        }
    };

    /// <summary>
    /// Gets the built-in ruleset by name.
    /// </summary>
    public static PermissionRuleset? GetByName(string name) => name.ToLowerInvariant() switch
    {
        "primary" or "coder" or "build" => Primary,
        "explorer" or "explore" => Explorer,
        "planner" or "plan" => Planner,
        "researcher" or "research" => Researcher,
        _ => null
    };
}
