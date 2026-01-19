namespace OpenFork.Core.Mcp;

/// <summary>
/// Settings for MCP integration.
/// </summary>
public class McpSettings
{
    /// <summary>Whether MCP is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Path to mcp.json configuration file.</summary>
    public string? ConfigPath { get; set; }

    /// <summary>Default request timeout in seconds.</summary>
    public int DefaultTimeout { get; set; } = 60;

    /// <summary>Auto-start servers on application startup.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>Configured MCP servers.</summary>
    public List<McpServerConfig> Servers { get; set; } = new();
}
