namespace OpenFork.Core.Events;

/// <summary>
/// Fired when an MCP server is connected.
/// </summary>
public record McpServerConnectedEvent : EventBase
{
    /// <summary>Server name.</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>Number of tools available from this server.</summary>
    public int ToolCount { get; init; }
}

/// <summary>
/// Fired when an MCP server connection fails.
/// </summary>
public record McpServerConnectionFailedEvent : EventBase
{
    /// <summary>Server name.</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>Error message.</summary>
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Fired when an MCP server is disconnected.
/// </summary>
public record McpServerDisconnectedEvent : EventBase
{
    /// <summary>Server name.</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>Reason for disconnection.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Fired when an MCP tool is called.
/// </summary>
public record McpToolCalledEvent : EventBase
{
    /// <summary>Server name.</summary>
    public string ServerName { get; init; } = string.Empty;

    /// <summary>Tool name.</summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>Whether the call succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Call duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }
}
