using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Config;
using OpenFork.Core.Events;

namespace OpenFork.Core.Mcp;

/// <summary>
/// Manages MCP server connections and tools.
/// </summary>
public class McpServerManager : IMcpServerManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpServerConnection> _connections = new();
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly IEventBus _eventBus;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpServerManager> _logger;

    public McpServerManager(
        AppSettings settings,
        HttpClient httpClient,
        IEventBus eventBus,
        ILoggerFactory loggerFactory,
        ILogger<McpServerManager> logger)
    {
        _settings = settings;
        _httpClient = httpClient;
        _eventBus = eventBus;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public bool IsEnabled => _settings.Mcp.Enabled && _settings.Mcp.Servers.Any(s => s.Enabled);

    public IReadOnlyList<string> ConnectedServers => _connections.Keys.ToList();

    public Task<IReadOnlyList<McpServerConfig>> GetServersAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<McpServerConfig>>(
            _settings.Mcp.Servers.Where(s => s.Enabled).ToList());
    }

    public async Task StartAllAsync(CancellationToken ct = default)
    {
        if (!_settings.Mcp.Enabled)
        {
            _logger.LogInformation("MCP is disabled");
            return;
        }

        var servers = _settings.Mcp.Servers.Where(s => s.Enabled).ToList();
        if (servers.Count == 0)
        {
            _logger.LogInformation("No MCP servers configured");
            return;
        }

        _logger.LogInformation("Starting {Count} MCP servers...", servers.Count);

        foreach (var server in servers)
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
        }

        _logger.LogInformation("Connected to {Count}/{Total} MCP servers",
            _connections.Count, servers.Count);
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
        var toolsArray = toolsResult["tools"]?.AsArray() ?? new JsonArray();
        var tools = toolsArray
            .Where(t => t != null)
            .Select(t => new McpTool
            {
                Name = t!["name"]!.GetValue<string>(),
                Description = t["description"]?.GetValue<string>(),
                InputSchema = t["inputSchema"]?.DeepClone(),
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
            ServerInfo = initResult["serverInfo"]?.DeepClone()
        };
    }

    private IMcpTransport CreateTransport(McpServerConfig config)
    {
        return config.Transport switch
        {
            McpTransport.Stdio => new StdioTransport(
                config,
                _loggerFactory.CreateLogger<StdioTransport>()),

            McpTransport.Http => new HttpTransport(
                config,
                _httpClient,
                _loggerFactory.CreateLogger<HttpTransport>()),

            McpTransport.Sse => throw new NotImplementedException("SSE transport not yet implemented"),

            _ => throw new ArgumentException($"Unknown transport: {config.Transport}")
        };
    }

    public Task<IReadOnlyList<McpTool>> GetAllToolsAsync(CancellationToken ct = default)
    {
        var tools = new List<McpTool>();

        foreach (var connection in _connections.Values)
        {
            tools.AddRange(connection.Tools);
        }

        return Task.FromResult<IReadOnlyList<McpTool>>(tools);
    }

    public async Task<McpToolResult> CallToolAsync(
        string serverName,
        string toolName,
        JsonNode arguments,
        CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(serverName, out var connection))
        {
            throw new McpException($"MCP server not connected: {serverName}");
        }

        _logger.LogDebug("Calling MCP tool {Tool} on {Server}", toolName, serverName);

        try
        {
            var result = await connection.Transport.SendRequestAsync("tools/call", new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments.DeepClone()
            }, ct);

            var contentArray = result["content"]?.AsArray() ?? new JsonArray();
            return new McpToolResult
            {
                IsError = result["isError"]?.GetValue<bool>() ?? false,
                Content = contentArray
                    .Where(c => c != null)
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
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call MCP tool {Tool} on {Server}", toolName, serverName);
            throw new McpException($"Tool call failed: {ex.Message}", -32001, ex);
        }
    }

    public Task<McpServerConnection?> GetConnectionAsync(
        string serverName,
        CancellationToken ct = default)
    {
        return Task.FromResult(
            _connections.TryGetValue(serverName, out var connection) ? connection : null);
    }

    public async Task StopAllAsync()
    {
        foreach (var (name, connection) in _connections)
        {
            try
            {
                await connection.Transport.DisposeAsync();
                _logger.LogInformation("Disconnected from MCP server: {Name}", name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting from MCP server: {Name}", name);
            }
        }

        _connections.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync();
    }
}
