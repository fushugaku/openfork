using OpenFork.Core.Hooks;
using OpenFork.Core.Mcp;

namespace OpenFork.Core.Config;

public class AppSettings
{
    public string DatabasePath { get; set; } = "data/openfork.db";
    public string? DefaultProviderKey { get; set; }
    public string? DefaultModel { get; set; }
    public string? DefaultAgentSlug { get; set; } = "coder";
    public Dictionary<string, OpenAiCompatibleProvider> OpenAiCompatible { get; set; } = new();
    public List<AgentProfileConfig> Agents { get; set; } = new();
    public List<PipelineConfig> Pipelines { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
    public HookSettings Hooks { get; set; } = new();
    public McpSettings Mcp { get; set; } = new();

    /// <summary>
    /// Path to directory containing *.tool.json pipeline tool definitions.
    /// Relative paths are resolved from the config directory.
    /// </summary>
    public string? ToolsDirectory { get; set; }
}

public class SearchSettings
{
    public string QdrantHost { get; set; } = "localhost";
    public int QdrantPort { get; set; } = 6334;
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public int EmbeddingDimension { get; set; } = 768;
    public bool EnableSemanticSearch { get; set; } = true;
}
