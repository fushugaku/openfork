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
/// Scope for remembering permission decisions.
/// </summary>
public enum PermissionScope
{
    /// <summary>Just this one invocation.</summary>
    ThisCall,

    /// <summary>For the rest of this session.</summary>
    ThisSession,

    /// <summary>For this pattern permanently.</summary>
    ThisPattern,

    /// <summary>Always for this tool.</summary>
    Always
}
