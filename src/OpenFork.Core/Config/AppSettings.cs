namespace OpenFork.Core.Config;

public class AppSettings
{
    public string DatabasePath { get; set; } = "data/openfork.db";
    public string? DefaultProviderKey { get; set; }
    public string? DefaultModel { get; set; }
    public Dictionary<string, OpenAiCompatibleProvider> OpenAiCompatible { get; set; } = new();
    public List<AgentProfileConfig> Agents { get; set; } = new();
    public List<PipelineConfig> Pipelines { get; set; } = new();
    public SearchSettings Search { get; set; } = new();
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
