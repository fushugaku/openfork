using System.Text.Json.Nodes;

namespace OpenFork.Core.Mcp;

/// <summary>
/// Manages MCP server connections and tools.
/// </summary>
public interface IMcpServerManager
{
    /// <summary>Get all configured servers.</summary>
    Task<IReadOnlyList<McpServerConfig>> GetServersAsync(CancellationToken ct = default);

    /// <summary>Get a specific server connection.</summary>
    Task<McpServerConnection?> GetConnectionAsync(string serverName, CancellationToken ct = default);

    /// <summary>Get all tools from all connected servers.</summary>
    Task<IReadOnlyList<McpTool>> GetAllToolsAsync(CancellationToken ct = default);

    /// <summary>Call a tool on a specific server.</summary>
    Task<McpToolResult> CallToolAsync(
        string serverName,
        string toolName,
        JsonNode arguments,
        CancellationToken ct = default);

    /// <summary>Start all configured servers.</summary>
    Task StartAllAsync(CancellationToken ct = default);

    /// <summary>Stop all servers.</summary>
    Task StopAllAsync();

    /// <summary>Check if MCP is enabled and has any servers.</summary>
    bool IsEnabled { get; }

    /// <summary>Get connected server names.</summary>
    IReadOnlyList<string> ConnectedServers { get; }
}
