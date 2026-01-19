namespace OpenFork.Core.Permissions;

/// <summary>
/// A collection of permission rules forming a complete ruleset.
/// </summary>
public record PermissionRuleset
{
    /// <summary>
    /// Rules evaluated in order by priority. Last matching rule wins.
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
    /// <summary>
    /// The action to take (Allow, Deny, Ask).
    /// </summary>
    public PermissionAction Action { get; init; }

    /// <summary>
    /// Reason for the action (from rule or default).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The rule that matched, if any.
    /// </summary>
    public PermissionRule? MatchedRule { get; init; }

    /// <summary>
    /// The tool that was checked.
    /// </summary>
    public string Tool { get; init; } = string.Empty;

    /// <summary>
    /// The resource that was checked.
    /// </summary>
    public string Resource { get; init; } = string.Empty;
}

/// <summary>
/// Result of user permission prompt.
/// </summary>
public record PermissionPromptResult
{
    /// <summary>
    /// Whether permission was granted.
    /// </summary>
    public bool Granted { get; init; }

    /// <summary>
    /// Whether to remember the choice.
    /// </summary>
    public bool RememberChoice { get; init; }

    /// <summary>
    /// Scope for remembering the choice.
    /// </summary>
    public PermissionScope RememberScope { get; init; }

    /// <summary>
    /// Optional reason from user.
    /// </summary>
    public string? UserReason { get; init; }
}
