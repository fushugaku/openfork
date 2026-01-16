using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Lsp;

public class LspService : IDisposable
{
  private readonly ILogger<LspService> _logger;
  private readonly ConcurrentDictionary<string, LspClient> _clients = new();
  private readonly ConcurrentDictionary<string, Task<LspClient?>> _spawning = new();
  private readonly HashSet<string> _broken = new();
  private readonly object _lock = new();
  private bool _disposed;

  public LspService(ILogger<LspService> logger)
  {
    _logger = logger;
  }

  public async Task<LspClient?> GetClientForFileAsync(string filePath, string workspaceRoot, CancellationToken ct = default)
  {
    var config = LspServerConfigs.GetForFile(filePath);
    if (config == null)
    {
      _logger.LogDebug("No LSP server configured for file: {File}", filePath);
      return null;
    }

    var root = FindProjectRoot(filePath, workspaceRoot, config.RootPatterns);
    var key = $"{config.Id}:{root}";

    lock (_lock)
    {
      if (_broken.Contains(key))
        return null;
    }

    if (_clients.TryGetValue(key, out var existingClient))
      return existingClient;

    if (_spawning.TryGetValue(key, out var spawningTask))
      return await spawningTask;

    var task = SpawnClientAsync(config, root, key, ct);
    _spawning[key] = task;

    try
    {
      var client = await task;
      _spawning.TryRemove(key, out _);
      return client;
    }
    catch
    {
      _spawning.TryRemove(key, out _);
      throw;
    }
  }

  private async Task<LspClient?> SpawnClientAsync(LspServerConfig config, string root, string key, CancellationToken ct)
  {
    _logger.LogInformation("Starting LSP server {ServerId} for root {Root}", config.Id, root);

    var client = await LspClient.CreateAsync(config, root, _logger, ct);
    if (client == null)
    {
      lock (_lock)
      {
        _broken.Add(key);
      }
      return null;
    }

    _clients[key] = client;
    _logger.LogInformation("LSP server {ServerId} started successfully", config.Id);
    return client;
  }

  private static string FindProjectRoot(string filePath, string workspaceRoot, string[] rootPatterns)
  {
    var dir = Path.GetDirectoryName(filePath) ?? workspaceRoot;

    while (!string.IsNullOrEmpty(dir) && dir.StartsWith(workspaceRoot))
    {
      foreach (var pattern in rootPatterns)
      {
        if (pattern.Contains('*'))
        {
          var files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
          if (files.Length > 0)
            return dir;
        }
        else
        {
          if (File.Exists(Path.Combine(dir, pattern)) || Directory.Exists(Path.Combine(dir, pattern)))
            return dir;
        }
      }

      var parent = Path.GetDirectoryName(dir);
      if (parent == dir) break;
      dir = parent;
    }

    return workspaceRoot;
  }

  public bool HasServerForFile(string filePath)
  {
    return LspServerConfigs.GetForFile(filePath) != null;
  }

  public async Task OpenFileAsync(string filePath, string workspaceRoot, CancellationToken ct = default)
  {
    var client = await GetClientForFileAsync(filePath, workspaceRoot, ct);
    if (client != null)
    {
      await client.OpenFileAsync(filePath, ct);
    }
  }

  public async Task<List<LspDiagnostic>> GetDiagnosticsAsync(string[] files, string workspaceRoot, CancellationToken ct = default)
  {
    var diagnostics = new List<LspDiagnostic>();

    foreach (var file in files)
    {
      var client = await GetClientForFileAsync(file, workspaceRoot, ct);
      if (client == null) continue;

      await client.OpenFileAsync(file, ct);

      await Task.Delay(500, ct);

      if (client.Diagnostics.TryGetValue(file, out var fileDiags))
      {
        diagnostics.AddRange(fileDiags);
      }
    }

    if (files.Length == 0)
    {
      foreach (var client in _clients.Values)
      {
        foreach (var (path, diags) in client.Diagnostics)
        {
          diagnostics.AddRange(diags);
        }
      }
    }

    return diagnostics;
  }

  public async Task<string?> HoverAsync(string filePath, int line, int character, string workspaceRoot, CancellationToken ct = default)
  {
    var client = await GetClientForFileAsync(filePath, workspaceRoot, ct);
    if (client == null) return null;

    await client.OpenFileAsync(filePath, ct);
    var result = await client.HoverAsync(filePath, line, character, ct);

    if (result == null) return null;

    try
    {
      if (result.Value.TryGetProperty("contents", out var contents))
      {
        if (contents.ValueKind == JsonValueKind.String)
          return contents.GetString();
        if (contents.TryGetProperty("value", out var value))
          return value.GetString();
        if (contents.ValueKind == JsonValueKind.Array)
        {
          var parts = new List<string>();
          foreach (var item in contents.EnumerateArray())
          {
            if (item.ValueKind == JsonValueKind.String)
              parts.Add(item.GetString() ?? "");
            else if (item.TryGetProperty("value", out var v))
              parts.Add(v.GetString() ?? "");
          }
          return string.Join("\n", parts);
        }
      }
      return result.Value.GetRawText();
    }
    catch
    {
      return result.Value.GetRawText();
    }
  }

  public async Task<string?> DefinitionAsync(string filePath, int line, int character, string workspaceRoot, CancellationToken ct = default)
  {
    var client = await GetClientForFileAsync(filePath, workspaceRoot, ct);
    if (client == null) return null;

    await client.OpenFileAsync(filePath, ct);
    var result = await client.DefinitionAsync(filePath, line, character, ct);

    return result?.GetRawText();
  }

  public async Task<string?> ReferencesAsync(string filePath, int line, int character, string workspaceRoot, CancellationToken ct = default)
  {
    var client = await GetClientForFileAsync(filePath, workspaceRoot, ct);
    if (client == null) return null;

    await client.OpenFileAsync(filePath, ct);
    var result = await client.ReferencesAsync(filePath, line, character, ct);

    return result?.GetRawText();
  }

  public async Task<string?> DocumentSymbolAsync(string filePath, string workspaceRoot, CancellationToken ct = default)
  {
    var client = await GetClientForFileAsync(filePath, workspaceRoot, ct);
    if (client == null) return null;

    await client.OpenFileAsync(filePath, ct);
    var result = await client.DocumentSymbolAsync(filePath, ct);

    return result?.GetRawText();
  }

  public async Task<string?> WorkspaceSymbolAsync(string query, string workspaceRoot, CancellationToken ct = default)
  {
    var results = new List<JsonElement>();

    foreach (var client in _clients.Values)
    {
      try
      {
        var result = await client.WorkspaceSymbolAsync(query, ct);
        if (result != null && result.Value.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in result.Value.EnumerateArray())
            results.Add(item.Clone());
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error getting workspace symbols from {ServerId}", client.ServerId);
      }
    }

    return results.Count > 0 ? JsonSerializer.Serialize(results) : null;
  }

  public async Task<string?> ImplementationAsync(string filePath, int line, int character, string workspaceRoot, CancellationToken ct = default)
  {
    var client = await GetClientForFileAsync(filePath, workspaceRoot, ct);
    if (client == null) return null;

    await client.OpenFileAsync(filePath, ct);
    var result = await client.ImplementationAsync(filePath, line, character, ct);

    return result?.GetRawText();
  }

  public IEnumerable<(string ServerId, string Root)> GetActiveClients()
  {
    return _clients.Values.Select(c => (c.ServerId, c.RootPath));
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    foreach (var client in _clients.Values)
    {
      try
      {
        client.Dispose();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error disposing LSP client {ServerId}", client.ServerId);
      }
    }

    _clients.Clear();
  }
}
