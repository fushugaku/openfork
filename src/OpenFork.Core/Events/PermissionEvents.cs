using OpenFork.Core.Permissions;

namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// PERMISSION EVENTS
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Raised when permission is requested for an operation.
/// </summary>
public record PermissionRequestedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
    public PermissionAction RequestedAction { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Raised when permission is granted for an operation.
/// </summary>
public record PermissionGrantedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
    public PermissionScope Scope { get; init; }
    public bool UserApproved { get; init; }
}

/// <summary>
/// Raised when permission is denied for an operation.
/// </summary>
public record PermissionDeniedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public bool UserDenied { get; init; }
}

/// <summary>
/// Raised when a permission prompt is shown to the user.
/// </summary>
public record PermissionPromptShownEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string PromptId { get; init; } = string.Empty;
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
}

/// <summary>
/// Raised when a user responds to a permission prompt.
/// </summary>
public record PermissionPromptAnsweredEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string PromptId { get; init; } = string.Empty;
    public bool Granted { get; init; }
    public PermissionScope Scope { get; init; }
}
