using System.Text.Json.Nodes;

namespace OpenFork.Core.Mcp;

/// <summary>
/// Configuration for an MCP server connection.
/// </summary>
public class McpServerConfig
{
    /// <summary>Unique identifier for this server.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>Transport type.</summary>
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>For stdio: command to execute.</summary>
    public string? Command { get; set; }

    /// <summary>For stdio: command arguments.</summary>
    public List<string>? Args { get; set; }

    /// <summary>For HTTP/SSE: server URL.</summary>
    public string? Url { get; set; }

    /// <summary>Environment variables to pass to stdio server.</summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>Working directory for stdio server.</summary>
    public string? Cwd { get; set; }

    /// <summary>Authentication configuration.</summary>
    public McpAuthConfig? Auth { get; set; }

    /// <summary>Connection timeout.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Request timeout.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Auto-reconnect on disconnect.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>Whether this server is enabled.</summary>
    public bool Enabled { get; set; } = true;
}

public enum McpTransport
{
    Stdio,
    Http,
    Sse
}

public class McpAuthConfig
{
    public McpAuthType Type { get; set; } = McpAuthType.None;
    public string? ApiKey { get; set; }
    public string? ApiKeyEnv { get; set; }  // Environment variable name
    public string? BearerToken { get; set; }
    public string? TokenEnv { get; set; }  // Environment variable name for token
    public McpOAuthConfig? OAuth { get; set; }
}

public enum McpAuthType
{
    None,
    ApiKey,
    Bearer,
    OAuth
}

public class McpOAuthConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string? AuthorizationUrl { get; set; }
    public List<string>? Scopes { get; set; }
}

/// <summary>
/// Tool definition from MCP server.
/// </summary>
public class McpTool
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonNode? InputSchema { get; set; }
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Full tool name for LLM: mcp__{server}__{tool}</summary>
    public string FullName => $"mcp__{ServerName}__{Name}";
}

/// <summary>
/// Result of MCP tool call.
/// </summary>
public class McpToolResult
{
    public bool IsError { get; set; }
    public List<McpContent> Content { get; set; } = new();
}

public class McpContent
{
    public string Type { get; set; } = "text";  // text, image, resource
    public string? Text { get; set; }
    public string? MimeType { get; set; }
    public string? Data { get; set; }  // Base64 for binary
    public McpResource? Resource { get; set; }
}

public class McpResource
{
    public string Uri { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public string? Text { get; set; }
    public string? Blob { get; set; }
}

/// <summary>
/// MCP server connection state.
/// </summary>
public class McpServerConnection
{
    public McpServerConfig Config { get; init; } = null!;
    public IMcpTransport Transport { get; init; } = null!;
    public List<McpTool> Tools { get; init; } = new();
    public JsonNode? ServerInfo { get; init; }
}
