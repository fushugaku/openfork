using System.Text.Json.Nodes;

namespace OpenFork.Core.Mcp;

/// <summary>
/// Transport layer for MCP communication.
/// </summary>
public interface IMcpTransport : IAsyncDisposable
{
    /// <summary>Connect to the MCP server.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Send a request and wait for response.</summary>
    Task<JsonNode> SendRequestAsync(string method, JsonNode? parameters, CancellationToken ct = default);

    /// <summary>Send a notification (no response expected).</summary>
    Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken ct = default);

    /// <summary>Event fired when notification is received from server.</summary>
    event Action<JsonNode>? OnNotification;

    /// <summary>Whether the transport is currently connected.</summary>
    bool IsConnected { get; }
}
