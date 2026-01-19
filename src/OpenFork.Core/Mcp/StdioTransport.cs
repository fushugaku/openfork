using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Mcp;

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
    private CancellationTokenSource? _readCts;
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
        if (string.IsNullOrEmpty(_config.Command))
        {
            throw new McpException("No command specified for stdio transport");
        }

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
                // Support environment variable expansion
                var expandedValue = ExpandEnvVars(value);
                startInfo.EnvironmentVariables[key] = expandedValue;
            }
        }

        _process = new Process { StartInfo = startInfo };

        try
        {
            _process.Start();
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to start MCP server: {ex.Message}", -1, ex);
        }

        _writer = _process.StandardInput;
        _reader = _process.StandardOutput;

        // Start reading responses
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = ReadResponsesAsync(_readCts.Token);

        _logger.LogInformation("Connected to MCP server: {Name} (PID: {Pid})",
            _config.Name, _process.Id);
    }

    public async Task<JsonNode> SendRequestAsync(
        string method,
        JsonNode? parameters,
        CancellationToken ct = default)
    {
        if (!IsConnected || _writer == null)
        {
            throw new McpException("Transport not connected");
        }

        var id = Interlocked.Increment(ref _requestId).ToString();
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };

        if (parameters != null)
        {
            request["params"] = parameters.DeepClone();
        }

        var message = request.ToJsonString() + "\n";

        try
        {
            await _writer.WriteAsync(message);
            await _writer.FlushAsync();
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            throw new McpException($"Failed to send request: {ex.Message}", -1, ex);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_config.RequestTimeout);

        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            throw new McpException("Request timed out", -32000);
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
        if (!IsConnected || _writer == null)
        {
            throw new McpException("Transport not connected");
        }

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters != null)
        {
            notification["params"] = parameters.DeepClone();
        }

        var message = notification.ToJsonString() + "\n";
        await _writer.WriteAsync(message);
        await _writer.FlushAsync();
    }

    private async Task ReadResponsesAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && IsConnected && _reader != null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;

                if (string.IsNullOrWhiteSpace(line)) continue;

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
                            var errorMsg = json["error"]!["message"]?.GetValue<string>() ?? "Unknown error";
                            var errorCode = json["error"]!["code"]?.GetValue<int>() ?? -1;
                            tcs.SetException(new McpException(errorMsg, errorCode));
                        }
                        else if (json["result"] != null)
                        {
                            tcs.SetResult(json["result"]!);
                        }
                        else
                        {
                            tcs.SetResult(new JsonObject());
                        }
                    }
                    else
                    {
                        // Notification from server
                        OnNotification?.Invoke(json);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse MCP response: {Line}", line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP read loop failed for {Name}", _config.Name);

            // Fail all pending requests
            foreach (var (id, tcs) in _pending)
            {
                tcs.TrySetException(new McpException("Connection lost", -32001));
            }
            _pending.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readCts?.Cancel();

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error killing MCP process for {Name}", _config.Name);
            }

            _process.Dispose();
            _process = null;
        }

        if (_readTask != null)
        {
            try
            {
                await _readTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Read task did not complete in time for {Name}", _config.Name);
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        _readCts?.Dispose();
        _logger.LogInformation("Disconnected from MCP server: {Name}", _config.Name);
    }

    private static string ExpandEnvVars(string value)
    {
        // Support ${VAR} and $VAR syntax
        if (value.Contains('$'))
        {
            // Replace ${VAR} first
            var result = System.Text.RegularExpressions.Regex.Replace(
                value,
                @"\$\{([^}]+)\}",
                m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "");

            // Then $VAR (but not in middle of other text)
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"\$([A-Z_][A-Z0-9_]*)",
                m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return result;
        }

        return value;
    }
}
