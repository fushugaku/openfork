using System.Text.Json.Nodes;
using OpenFork.Core.Permissions;

namespace OpenFork.Core.Services;

/// <summary>
/// Service for checking and managing tool execution permissions.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Check if an operation is permitted.
    /// </summary>
    /// <param name="ruleset">The ruleset to evaluate against.</param>
    /// <param name="tool">The tool name.</param>
    /// <param name="arguments">Tool arguments as JSON.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating the permission action.</returns>
    Task<PermissionCheckResult> CheckAsync(
        PermissionRuleset ruleset,
        string tool,
        JsonNode? arguments,
        CancellationToken ct = default);

    /// <summary>
    /// Prompt the user for permission.
    /// </summary>
    /// <param name="check">The permission check result that requires prompting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the user prompt.</returns>
    Task<PermissionPromptResult> PromptAsync(
        PermissionCheckResult check,
        CancellationToken ct = default);

    /// <summary>
    /// Merge multiple rulesets (e.g., session + agent).
    /// Rules from later rulesets override earlier ones.
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

    /// <summary>
    /// Clear session-scoped permissions.
    /// </summary>
    void ClearSessionPermissions(Guid sessionId);

    /// <summary>
    /// Get the ruleset for an agent by name.
    /// </summary>
    PermissionRuleset GetAgentRuleset(string agentName);
}
