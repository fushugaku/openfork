using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Tools;

namespace OpenFork.Core.Lsp;

public class LspClient : IDisposable
{
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly string _serverId;
    private readonly string _rootPath;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly Dictionary<string, List<LspDiagnostic>> _diagnostics = new();
    private readonly CancellationTokenSource _cts = new();
    private int _requestId;
    private bool _initialized;
    private bool _disposed;

    public string ServerId => _serverId;
    public string RootPath => _rootPath;
    public IReadOnlyDictionary<string, List<LspDiagnostic>> Diagnostics => _diagnostics;

    private LspClient(Process process, string serverId, string rootPath, ILogger logger)
    {
        _process = process;
        _serverId = serverId;
        _rootPath = rootPath;
        _logger = logger;
        _writer = new StreamWriter(process.StandardInput.BaseStream, Encoding.UTF8) { AutoFlush = true };
        _reader = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);
    }

    public static async Task<LspClient?> CreateAsync(LspServerConfig config, string rootPath, ILogger logger, CancellationToken ct = default)
    {
        var command = config.Command;
        var which = FindExecutable(command);
        if (which == null)
        {
            logger.LogWarning("LSP server {ServerId} not found: {Command}", config.Id, command);
            return null;
        }

        var psi = new ProcessStartInfo
        {
            FileName = which,
            WorkingDirectory = rootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in config.Args)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi);
        if (process == null)
        {
            logger.LogError("Failed to start LSP server {ServerId}", config.Id);
            return null;
        }

        var client = new LspClient(process, config.Id, rootPath, logger);
        
        _ = Task.Run(() => client.ReadMessagesAsync(client._cts.Token), ct);

        try
        {
            await client.InitializeAsync(config, ct);
            return client;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize LSP server {ServerId}", config.Id);
            client.Dispose();
            return null;
        }
    }

    private static string? FindExecutable(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, name + ".exe");
                if (File.Exists(fullPath)) return fullPath;
                fullPath = Path.Combine(path, name + ".cmd");
                if (File.Exists(fullPath)) return fullPath;
                fullPath = Path.Combine(path, name + ".bat");
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        else
        {
            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(':') ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, name);
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        return null;
    }

    private async Task InitializeAsync(LspServerConfig config, CancellationToken ct)
    {
        var initParams = new
        {
            processId = Environment.ProcessId,
            rootUri = new Uri(_rootPath).ToString(),
            capabilities = new
            {
                textDocument = new
                {
                    synchronization = new { didOpen = true, didChange = true },
                    publishDiagnostics = new { versionSupport = true }
                },
                workspace = new { configuration = true }
            },
            initializationOptions = config.InitializationOptions
        };

        var result = await SendRequestAsync<JsonElement>("initialize", initParams, ct);
        _logger.LogInformation("LSP {ServerId} initialized: {Capabilities}", _serverId, result.GetRawText().Truncate(200));

        await SendNotificationAsync("initialized", new { }, ct);
        _initialized = true;
    }

    private async Task ReadMessagesAsync(CancellationToken ct)
    {
        var headerBuffer = new StringBuilder();
        
        while (!ct.IsCancellationRequested && !_process.HasExited)
        {
            try
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;

                if (line.StartsWith("Content-Length:"))
                {
                    headerBuffer.Clear();
                    headerBuffer.AppendLine(line);
                    continue;
                }

                if (string.IsNullOrEmpty(line) && headerBuffer.Length > 0)
                {
                    var headers = headerBuffer.ToString();
                    var lengthMatch = System.Text.RegularExpressions.Regex.Match(headers, @"Content-Length:\s*(\d+)");
                    if (lengthMatch.Success && int.TryParse(lengthMatch.Groups[1].Value, out var contentLength))
                    {
                        var buffer = new char[contentLength];
                        var read = 0;
                        while (read < contentLength)
                        {
                            var chunk = await _reader.ReadAsync(buffer.AsMemory(read, contentLength - read), ct);
                            if (chunk == 0) break;
                            read += chunk;
                        }

                        var json = new string(buffer, 0, read);
                        ProcessMessage(json);
                    }
                    headerBuffer.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading LSP message from {ServerId}", _serverId);
            }
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idProp))
            {
                var id = idProp.GetInt32();
                if (_pendingRequests.TryGetValue(id, out var tcs))
                {
                    _pendingRequests.Remove(id);
                    if (root.TryGetProperty("result", out var result))
                        tcs.TrySetResult(result.Clone());
                    else if (root.TryGetProperty("error", out var error))
                        tcs.TrySetException(new Exception($"LSP error: {error.GetRawText()}"));
                    else
                        tcs.TrySetResult(default);
                }
            }
            else if (root.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString();
                if (method == "textDocument/publishDiagnostics" && root.TryGetProperty("params", out var paramsProp))
                {
                    HandleDiagnostics(paramsProp);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LSP message: {Json}", json.Truncate(500));
        }
    }

    private void HandleDiagnostics(JsonElement paramsElement)
    {
        var uri = paramsElement.GetProperty("uri").GetString() ?? "";
        var filePath = new Uri(uri).LocalPath;

        var diags = new List<LspDiagnostic>();
        if (paramsElement.TryGetProperty("diagnostics", out var diagsArray))
        {
            foreach (var diag in diagsArray.EnumerateArray())
            {
                var range = diag.GetProperty("range");
                var start = range.GetProperty("start");
                var severity = diag.TryGetProperty("severity", out var sev) ? sev.GetInt32() : 1;
                var message = diag.GetProperty("message").GetString() ?? "";
                var code = diag.TryGetProperty("code", out var codeProp) ? codeProp.ToString() : null;

                diags.Add(new LspDiagnostic
                {
                    FilePath = filePath,
                    Line = start.GetProperty("line").GetInt32() + 1,
                    Column = start.GetProperty("character").GetInt32() + 1,
                    Severity = (LspDiagnosticSeverity)severity,
                    Message = message,
                    Code = code
                });
            }
        }

        _diagnostics[filePath] = diags;
        _logger.LogDebug("Received {Count} diagnostics for {File}", diags.Count, filePath);
    }

    public async Task<T> SendRequestAsync<T>(string method, object? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _requestId);
        var tcs = new TaskCompletionSource<JsonElement>();
        _pendingRequests[id] = tcs;

        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        await SendMessageAsync(request, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var result = await tcs.Task.WaitAsync(cts.Token);
            return JsonSerializer.Deserialize<T>(result.GetRawText(), JsonHelper.Options)!;
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.Remove(id);
            throw new TimeoutException($"LSP request '{method}' timed out");
        }
    }

    public async Task SendNotificationAsync(string method, object? parameters, CancellationToken ct)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        await SendMessageAsync(notification, ct);
    }

    private async Task SendMessageAsync(object message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var content = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n{json}";
        
        await _writer.WriteAsync(content.AsMemory(), ct);
    }

    public async Task OpenFileAsync(string filePath, CancellationToken ct)
    {
        if (!_initialized) return;

        var content = await File.ReadAllTextAsync(filePath, ct);
        var languageId = LspLanguages.GetLanguageId(filePath);

        await SendNotificationAsync("textDocument/didOpen", new
        {
            textDocument = new
            {
                uri = new Uri(filePath).ToString(),
                languageId,
                version = 1,
                text = content
            }
        }, ct);
    }

    public async Task<JsonElement?> HoverAsync(string filePath, int line, int character, CancellationToken ct)
    {
        return await SendRequestAsync<JsonElement?>("textDocument/hover", new
        {
            textDocument = new { uri = new Uri(filePath).ToString() },
            position = new { line = line - 1, character = character - 1 }
        }, ct);
    }

    public async Task<JsonElement?> DefinitionAsync(string filePath, int line, int character, CancellationToken ct)
    {
        return await SendRequestAsync<JsonElement?>("textDocument/definition", new
        {
            textDocument = new { uri = new Uri(filePath).ToString() },
            position = new { line = line - 1, character = character - 1 }
        }, ct);
    }

    public async Task<JsonElement?> ReferencesAsync(string filePath, int line, int character, CancellationToken ct)
    {
        return await SendRequestAsync<JsonElement?>("textDocument/references", new
        {
            textDocument = new { uri = new Uri(filePath).ToString() },
            position = new { line = line - 1, character = character - 1 },
            context = new { includeDeclaration = true }
        }, ct);
    }

    public async Task<JsonElement?> DocumentSymbolAsync(string filePath, CancellationToken ct)
    {
        return await SendRequestAsync<JsonElement?>("textDocument/documentSymbol", new
        {
            textDocument = new { uri = new Uri(filePath).ToString() }
        }, ct);
    }

    public async Task<JsonElement?> WorkspaceSymbolAsync(string query, CancellationToken ct)
    {
        return await SendRequestAsync<JsonElement?>("workspace/symbol", new { query }, ct);
    }

    public async Task<JsonElement?> ImplementationAsync(string filePath, int line, int character, CancellationToken ct)
    {
        return await SendRequestAsync<JsonElement?>("textDocument/implementation", new
        {
            textDocument = new { uri = new Uri(filePath).ToString() },
            position = new { line = line - 1, character = character - 1 }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(1000);
            }
        }
        catch { }

        _process.Dispose();
        _writer.Dispose();
        _reader.Dispose();
    }
}

public class LspDiagnostic
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public LspDiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = "";
    public string? Code { get; set; }
}

public enum LspDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

internal static class StringExtensionsLsp
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
