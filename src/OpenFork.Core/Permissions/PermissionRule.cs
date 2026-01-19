namespace OpenFork.Core.Permissions;

/// <summary>
/// A single permission rule that matches tool invocations.
/// </summary>
public record PermissionRule
{
    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

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

    /// <summary>
    /// When this rule was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
