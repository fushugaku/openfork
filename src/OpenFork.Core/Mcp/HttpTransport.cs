using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Mcp;

/// <summary>
/// HTTP transport for remote MCP servers.
/// </summary>
public class HttpTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpTransport> _logger;

    public bool IsConnected => true;  // HTTP is stateless
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
        if (string.IsNullOrEmpty(_config.Url))
        {
            throw new McpException("No URL specified for HTTP transport");
        }

        // Configure authentication
        if (_config.Auth != null)
        {
            switch (_config.Auth.Type)
            {
                case McpAuthType.ApiKey:
                    var apiKey = _config.Auth.ApiKey
                        ?? Environment.GetEnvironmentVariable(_config.Auth.ApiKeyEnv ?? "");
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    }
                    break;

                case McpAuthType.Bearer:
                    var token = _config.Auth.BearerToken
                        ?? Environment.GetEnvironmentVariable(_config.Auth.TokenEnv ?? "");
                    if (!string.IsNullOrEmpty(token))
                    {
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", token);
                    }
                    break;
            }
        }

        _logger.LogInformation("Configured HTTP transport for MCP server: {Name}", _config.Name);
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
            request["params"] = parameters.DeepClone();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_config.RequestTimeout);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_config.Url, request, cts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(cts.Token);

            if (json?["error"] != null)
            {
                var errorMsg = json["error"]!["message"]?.GetValue<string>() ?? "Unknown error";
                var errorCode = json["error"]!["code"]?.GetValue<int>() ?? -1;
                throw new McpException(errorMsg, errorCode);
            }

            return json?["result"] ?? new JsonObject();
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"HTTP request failed: {ex.Message}", -32002, ex);
        }
        catch (OperationCanceledException)
        {
            throw new McpException("Request timed out", -32000);
        }
    }

    public async Task SendNotificationAsync(
        string method,
        JsonNode? parameters,
        CancellationToken ct = default)
    {
        // HTTP notifications are fire-and-forget
        try
        {
            _ = await SendRequestAsync(method, parameters, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send HTTP notification: {Method}", method);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
