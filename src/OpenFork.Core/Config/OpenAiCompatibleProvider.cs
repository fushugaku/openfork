namespace OpenFork.Core.Config;

public class OpenAiCompatibleProvider
{
    public string ApiUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? ApiKeyEnv { get; set; }
    public List<ModelInfo> AvailableModels { get; set; } = new();
}

public class ModelInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int MaxTokens { get; set; }
    public int MaxOutputTokens { get; set; }
    public int MaxCompletionTokens { get; set; }
    public ModelCapabilities Capabilities { get; set; } = new();
}

public class ModelCapabilities
{
    public bool Tools { get; set; }
    public bool Images { get; set; }
    public bool ParallelToolCalls { get; set; }
    public bool PromptCacheKey { get; set; }
}
