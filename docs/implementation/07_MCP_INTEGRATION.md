# MCP Integration Implementation Guide

## Overview

Model Context Protocol (MCP) enables integration with external tool servers, extending OpenFork's capabilities through a standardized protocol.

---

## Architecture Analysis

### Current State (OpenFork)

```
┌─────────────────────────────────────────┐
│          Hardcoded Tools Only           │
│                                         │
│   ToolRegistry ──► Built-in Tools       │
│                    • read               │
│                    • edit               │
│                    • bash               │
│                    • ...                │
│                                         │
│   (No external tool integration)        │
└─────────────────────────────────────────┘
```

### Target State (MCP-enabled)

```
┌─────────────────────────────────────────────────────────────────┐
│                    MCP-Enabled OpenFork                          │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    Tool Registry                            │ │
│  │                                                             │ │
│  │  Built-in Tools          MCP Tools                         │ │
│  │  • read                  • mcp__github__create_pr          │ │
│  │  • edit                  • mcp__jira__create_issue         │ │
│  │  • bash                  • mcp__postgres__query            │ │
│  │  • ...                   • mcp__custom__anything           │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              │                                   │
│                              ▼                                   │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                   MCP Server Manager                        │ │
│  │                                                             │ │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐                 │ │
│  │  │  stdio   │  │   HTTP   │  │   SSE    │                 │ │
│  │  │ servers  │  │ servers  │  │ servers  │                 │ │
│  │  └──────────┘  └──────────┘  └──────────┘                 │ │
│  │                                                             │ │
│  │  • Connection pooling                                       │ │
│  │  • Health monitoring                                        │ │
│  │  • Auto-reconnect                                           │ │
│  │  • OAuth integration                                        │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

---

## MCP Protocol Overview

The Model Context Protocol defines:

1. **Transports**: stdio, HTTP, SSE
2. **Messages**: JSON-RPC 2.0 format
3. **Capabilities**: tools, prompts, resources, sampling
4. **Lifecycle**: initialize, tools/list, tools/call, shutdown

---

## Domain Model

### MCP Server Configuration

```csharp
namespace OpenFork.Core.Mcp;

/// <summary>
/// Configuration for an MCP server connection.
/// </summary>
public class McpServerConfig
{
    /// <summary>
    /// Unique identifier for this server.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Transport type.
    /// </summary>
    public McpTransport Transport { get; set; } = McpTransport.Stdio;

    /// <summary>
    /// For stdio: command to execute.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// For stdio: command arguments.
    /// </summary>
    public List<string>? Args { get; set; }

    /// <summary>
    /// For HTTP/SSE: server URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Environment variables to pass to stdio server.
    /// </summary>
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Working directory for stdio server.
    /// </summary>
    public string? Cwd { get; set; }

    /// <summary>
    /// Authentication configuration.
    /// </summary>
    public McpAuthConfig? Auth { get; set; }

    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Request timeout.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Auto-reconnect on disconnect.
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Whether this server is enabled.
    /// </summary>
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
```

### MCP Tool Definition

```csharp
namespace OpenFork.Core.Mcp;

/// <summary>
/// Tool definition from MCP server.
/// </summary>
public class McpTool
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonNode? InputSchema { get; set; }
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// Full tool name for LLM: mcp__{server}__{tool}
    /// </summary>
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
```

---

## MCP Client Implementation

### Transport Layer

```csharp
namespace OpenFork.Core.Mcp.Transport;

public interface IMcpTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    Task<JsonNode> SendRequestAsync(string method, JsonNode? parameters, CancellationToken ct = default);
    Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken ct = default);
    event Action<JsonNode>? OnNotification;
    bool IsConnected { get; }
}

/// <summary>
/// Stdio transport for local MCP servers.
/// </summary>
public class StdioTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly ILogger<StdioTransport> _logger;
    private Process? _process;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private Task? _readTask;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode>> _pending = new();
    private int _requestId;

    public bool IsConnected => _process?.HasExited == false;
    public event Action<JsonNode>? OnNotification;

    public StdioTransport(McpServerConfig config, ILogger<StdioTransport> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _config.Command,
            Arguments = string.Join(" ", _config.Args ?? new List<string>()),
            WorkingDirectory = _config.Cwd ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Add environment variables
        if (_config.Env != null)
        {
            foreach (var (key, value) in _config.Env)
            {
                startInfo.EnvironmentVariables[key] = value;
            }
        }

        _process = new Process { StartInfo = startInfo };
        _process.Start();

        _writer = _process.StandardInput;
        _reader = _process.StandardOutput;

        // Start reading responses
        _readTask = ReadResponsesAsync(ct);

        _logger.LogInformation("Connected to MCP server: {Name}", _config.Name);
    }

    public async Task<JsonNode> SendRequestAsync(
        string method,
        JsonNode? parameters,
        CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JsonNode>();
        _pending[id] = tcs;

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };

        if (parameters != null)
        {
            request["params"] = parameters;
        }

        var message = request.ToJsonString() + "\n";
        await _writer!.WriteAsync(message);
        await _writer.FlushAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_config.RequestTimeout);

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async Task SendNotificationAsync(
        string method,
        JsonNode? parameters,
        CancellationToken ct = default)
    {
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters != null)
        {
            notification["params"] = parameters;
        }

        var message = notification.ToJsonString() + "\n";
        await _writer!.WriteAsync(message);
        await _writer.FlushAsync();
    }

    private async Task ReadResponsesAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                var line = await _reader!.ReadLineAsync();
                if (line == null) break;

                try
                {
                    var json = JsonNode.Parse(line);
                    if (json == null) continue;

                    var id = json["id"]?.GetValue<string>();

                    if (id != null && _pending.TryRemove(id, out var tcs))
                    {
                        // Response to request
                        if (json["error"] != null)
                        {
                            tcs.SetException(new McpException(
                                json["error"]!["message"]?.GetValue<string>() ?? "Unknown error",
                                json["error"]!["code"]?.GetValue<int>() ?? -1));
                        }
                        else
                        {
                            tcs.SetResult(json["result"]!);
                        }
                    }
                    else
                    {
                        // Notification
                        OnNotification?.Invoke(json);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse MCP response: {Line}", line);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "MCP read loop failed for {Name}", _config.Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process != null)
        {
            try
            {
                _process.Kill(true);
            }
            catch { }

            _process.Dispose();
        }

        if (_readTask != null)
        {
            await _readTask;
        }
    }
}

/// <summary>
/// HTTP transport for remote MCP servers.
/// </summary>
public class HttpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpTransport> _logger;

    public bool IsConnected => true;  // Stateless
    public event Action<JsonNode>? OnNotification;

    public HttpTransport(
        McpServerConfig config,
        HttpClient httpClient,
        ILogger<HttpTransport> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        // Configure auth
        if (_config.Auth != null)
        {
            switch (_config.Auth.Type)
            {
                case McpAuthType.ApiKey:
                    var apiKey = _config.Auth.ApiKey
                        ?? Environment.GetEnvironmentVariable(_config.Auth.ApiKeyEnv ?? "");
                    _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    break;

                case McpAuthType.Bearer:
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _config.Auth.BearerToken);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    public async Task<JsonNode> SendRequestAsync(
        string method,
        JsonNode? parameters,
        CancellationToken ct = default)
    {
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["method"] = method
        };

        if (parameters != null)
        {
            request["params"] = parameters;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_config.RequestTimeout);

        var response = await _httpClient.PostAsJsonAsync(_config.Url, request, cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(cts.Token);

        if (json?["error"] != null)
        {
            throw new McpException(
                json["error"]!["message"]?.GetValue<string>() ?? "Unknown error",
                json["error"]!["code"]?.GetValue<int>() ?? -1);
        }

        return json!["result"]!;
    }

    public Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken ct = default)
    {
        // HTTP notifications are fire-and-forget
        _ = SendRequestAsync(method, parameters, ct);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## MCP Server Manager

```csharp
namespace OpenFork.Core.Mcp;

public interface IMcpServerManager
{
    Task<IReadOnlyList<McpServerConfig>> GetServersAsync(CancellationToken ct = default);
    Task<McpServerConnection?> GetConnectionAsync(string serverName, CancellationToken ct = default);
    Task<IReadOnlyList<McpTool>> GetAllToolsAsync(CancellationToken ct = default);
    Task<McpToolResult> CallToolAsync(string serverName, string toolName, JsonNode arguments, CancellationToken ct = default);
    Task StartAllAsync(CancellationToken ct = default);
    Task StopAllAsync();
}

public class McpServerManager : IMcpServerManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpServerConnection> _connections = new();
    private readonly IOptions<McpSettings> _settings;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<McpServerManager> _logger;

    public McpServerManager(
        IOptions<McpSettings> settings,
        IServiceProvider serviceProvider,
        IEventBus eventBus,
        ILogger<McpServerManager> logger)
    {
        _settings = settings;
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public Task<IReadOnlyList<McpServerConfig>> GetServersAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<McpServerConfig>>(
            _settings.Value.Servers.Where(s => s.Enabled).ToList());
    }

    public async Task StartAllAsync(CancellationToken ct = default)
    {
        var servers = _settings.Value.Servers.Where(s => s.Enabled);

        await Parallel.ForEachAsync(servers, ct, async (server, ct) =>
        {
            try
            {
                var connection = await ConnectServerAsync(server, ct);
                _connections[server.Name] = connection;

                await _eventBus.PublishAsync(new McpServerConnectedEvent
                {
                    ServerName = server.Name,
                    ToolCount = connection.Tools.Count
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server: {Name}", server.Name);

                await _eventBus.PublishAsync(new McpServerConnectionFailedEvent
                {
                    ServerName = server.Name,
                    Error = ex.Message
                }, ct);
            }
        });
    }

    private async Task<McpServerConnection> ConnectServerAsync(
        McpServerConfig config,
        CancellationToken ct)
    {
        var transport = CreateTransport(config);
        await transport.ConnectAsync(ct);

        // Initialize MCP protocol
        var initResult = await transport.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject()
            },
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "OpenFork",
                ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
            }
        }, ct);

        // Notify initialized
        await transport.SendNotificationAsync("notifications/initialized", null, ct);

        // Get available tools
        var toolsResult = await transport.SendRequestAsync("tools/list", null, ct);
        var tools = toolsResult["tools"]!.AsArray()
            .Select(t => new McpTool
            {
                Name = t!["name"]!.GetValue<string>(),
                Description = t["description"]?.GetValue<string>(),
                InputSchema = t["inputSchema"],
                ServerName = config.Name
            })
            .ToList();

        _logger.LogInformation(
            "Connected to MCP server {Name}: {Count} tools available",
            config.Name, tools.Count);

        return new McpServerConnection
        {
            Config = config,
            Transport = transport,
            Tools = tools,
            ServerInfo = initResult["serverInfo"]
        };
    }

    private IMcpTransport CreateTransport(McpServerConfig config)
    {
        return config.Transport switch
        {
            McpTransport.Stdio => new StdioTransport(
                config,
                _serviceProvider.GetRequiredService<ILogger<StdioTransport>>()),

            McpTransport.Http => new HttpTransport(
                config,
                _serviceProvider.GetRequiredService<HttpClient>(),
                _serviceProvider.GetRequiredService<ILogger<HttpTransport>>()),

            McpTransport.Sse => throw new NotImplementedException("SSE transport not yet implemented"),

            _ => throw new ArgumentException($"Unknown transport: {config.Transport}")
        };
    }

    public async Task<IReadOnlyList<McpTool>> GetAllToolsAsync(CancellationToken ct = default)
    {
        var tools = new List<McpTool>();

        foreach (var connection in _connections.Values)
        {
            tools.AddRange(connection.Tools);
        }

        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(
        string serverName,
        string toolName,
        JsonNode arguments,
        CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(serverName, out var connection))
        {
            throw new InvalidOperationException($"MCP server not connected: {serverName}");
        }

        var result = await connection.Transport.SendRequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments
        }, ct);

        return new McpToolResult
        {
            IsError = result["isError"]?.GetValue<bool>() ?? false,
            Content = result["content"]!.AsArray()
                .Select(c => new McpContent
                {
                    Type = c!["type"]?.GetValue<string>() ?? "text",
                    Text = c["text"]?.GetValue<string>(),
                    MimeType = c["mimeType"]?.GetValue<string>(),
                    Data = c["data"]?.GetValue<string>()
                })
                .ToList()
        };
    }

    public async Task<McpServerConnection?> GetConnectionAsync(
        string serverName,
        CancellationToken ct = default)
    {
        return _connections.TryGetValue(serverName, out var connection) ? connection : null;
    }

    public async Task StopAllAsync()
    {
        foreach (var connection in _connections.Values)
        {
            await connection.Transport.DisposeAsync();
        }
        _connections.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
    }
}

public class McpServerConnection
{
    public McpServerConfig Config { get; init; } = null!;
    public IMcpTransport Transport { get; init; } = null!;
    public List<McpTool> Tools { get; init; } = new();
    public JsonNode? ServerInfo { get; init; }
}
```

---

## MCP Tool Adapter

```csharp
namespace OpenFork.Core.Mcp;

/// <summary>
/// Adapts MCP tools to OpenFork's ITool interface.
/// </summary>
public class McpToolAdapter : ITool
{
    private readonly McpTool _mcpTool;
    private readonly IMcpServerManager _serverManager;
    private readonly ILogger<McpToolAdapter> _logger;

    public string Name => _mcpTool.FullName;

    public string Description => _mcpTool.Description ?? $"MCP tool from {_mcpTool.ServerName}";

    public JsonNode ParametersSchema => _mcpTool.InputSchema ?? new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject()
    };

    public McpToolAdapter(
        McpTool mcpTool,
        IMcpServerManager serverManager,
        ILogger<McpToolAdapter> logger)
    {
        _mcpTool = mcpTool;
        _serverManager = serverManager;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonNode parameters,
        ToolContext context,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Executing MCP tool: {Tool} on {Server}",
            _mcpTool.Name, _mcpTool.ServerName);

        try
        {
            var result = await _serverManager.CallToolAsync(
                _mcpTool.ServerName,
                _mcpTool.Name,
                parameters,
                ct);

            if (result.IsError)
            {
                return ToolResult.Failure(
                    string.Join("\n", result.Content.Select(c => c.Text ?? "[binary content]")));
            }

            // Format content
            var output = new StringBuilder();
            foreach (var content in result.Content)
            {
                switch (content.Type)
                {
                    case "text":
                        output.AppendLine(content.Text);
                        break;

                    case "image":
                        output.AppendLine($"[Image: {content.MimeType}]");
                        // Could save to temp file and include path
                        break;

                    case "resource":
                        if (content.Resource != null)
                        {
                            output.AppendLine($"[Resource: {content.Resource.Uri}]");
                            if (content.Resource.Text != null)
                            {
                                output.AppendLine(content.Resource.Text);
                            }
                        }
                        break;
                }
            }

            return ToolResult.Success(output.ToString().TrimEnd());
        }
        catch (McpException ex)
        {
            _logger.LogWarning(ex, "MCP tool failed: {Tool}", _mcpTool.Name);
            return ToolResult.Failure($"MCP error ({ex.Code}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool execution failed: {Tool}", _mcpTool.Name);
            return ToolResult.Failure($"Tool execution failed: {ex.Message}");
        }
    }
}
```

---

## Tool Registry Integration

```csharp
// Extension to ToolRegistry
public partial class ToolRegistry
{
    private readonly IMcpServerManager _mcpServerManager;

    public async Task RegisterMcpToolsAsync(CancellationToken ct = default)
    {
        var mcpTools = await _mcpServerManager.GetAllToolsAsync(ct);

        foreach (var mcpTool in mcpTools)
        {
            var adapter = new McpToolAdapter(
                mcpTool,
                _mcpServerManager,
                _serviceProvider.GetRequiredService<ILogger<McpToolAdapter>>());

            Register(adapter);
            _logger.LogDebug("Registered MCP tool: {Name}", adapter.Name);
        }

        _logger.LogInformation("Registered {Count} MCP tools", mcpTools.Count);
    }
}
```

---

## Configuration

### mcp.json (Claude Code compatible)

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_TOKEN": "${GITHUB_TOKEN}"
      }
    },
    "postgres": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-postgres"],
      "env": {
        "DATABASE_URL": "${DATABASE_URL}"
      }
    },
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/allowed/dir"]
    },
    "remote-api": {
      "transport": "http",
      "url": "https://api.example.com/mcp",
      "auth": {
        "type": "bearer",
        "tokenEnv": "API_TOKEN"
      }
    }
  }
}
```

### appsettings.json

```json
{
  "Mcp": {
    "Enabled": true,
    "ConfigPath": ".mcp.json",
    "DefaultTimeout": 60,
    "AutoStart": true,
    "Servers": []
  }
}
```

---

## Startup Integration

```csharp
// In Program.cs
public static async Task Main(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // ... other registrations

    // Register MCP
    builder.Services.AddSingleton<IMcpServerManager, McpServerManager>();
    builder.Services.Configure<McpSettings>(
        builder.Configuration.GetSection("Mcp"));

    var app = builder.Build();

    // Start MCP servers
    var mcpManager = app.Services.GetRequiredService<IMcpServerManager>();
    await mcpManager.StartAllAsync();

    // Register MCP tools
    var toolRegistry = app.Services.GetRequiredService<IToolRegistry>();
    await toolRegistry.RegisterMcpToolsAsync();

    // ... run application

    // Cleanup
    await mcpManager.StopAllAsync();
}
```

---

## Testing

```csharp
[Fact]
public async Task McpToolAdapter_ExecutesSuccessfully()
{
    var mockManager = new Mock<IMcpServerManager>();
    mockManager.Setup(m => m.CallToolAsync("github", "create_issue", It.IsAny<JsonNode>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new McpToolResult
        {
            IsError = false,
            Content = new() { new McpContent { Type = "text", Text = "Issue #123 created" } }
        });

    var tool = new McpToolAdapter(
        new McpTool { Name = "create_issue", ServerName = "github" },
        mockManager.Object,
        NullLogger<McpToolAdapter>.Instance);

    var result = await tool.ExecuteAsync(
        JsonNode.Parse("""{"title": "Test", "body": "Test body"}""")!,
        new ToolContext());

    Assert.True(result.Success);
    Assert.Contains("Issue #123 created", result.Output);
}
```

---

## Security Considerations

1. **Environment Variables**: Secrets via env vars, never in config files
2. **Server Validation**: Validate server commands before execution
3. **Network Isolation**: Consider firewall rules for HTTP servers
4. **Permission Integration**: MCP tools respect agent permissions
5. **Timeout Protection**: Configurable timeouts prevent hanging

---

## Migration Path

1. Add MCP configuration schema
2. Implement transport layer (stdio first)
3. Create McpServerManager
4. Create McpToolAdapter
5. Integrate with ToolRegistry
6. Add configuration UI
7. Implement HTTP/SSE transports
