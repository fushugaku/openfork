namespace OpenFork.Core.Chat;

public class ChatCompletionRequest
{
    public string Model { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public List<ToolDefinition>? Tools { get; set; }
    public bool Stream { get; set; }
    public string? User { get; set; }
}

public class ToolDefinition
{
    public string Type { get; set; } = "function";
    public ToolDefinitionFunction Function { get; set; } = new();
}

public class ToolDefinitionFunction
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? Parameters { get; set; }
}

public class ChatStreamEvent
{
    public string? DeltaContent { get; set; }
    public List<ToolCall>? DeltaToolCalls { get; set; }
    public bool IsDone { get; set; }
}

public class ChatCompletionResponse
{
    public string? Id { get; set; }
    public List<ChatChoice> Choices { get; set; } = new();
    public UsageInfo? Usage { get; set; }
}

public class ChatChoice
{
    public int Index { get; set; }
    public ChatMessage Message { get; set; } = new();
    public string? FinishReason { get; set; }
}

public class UsageInfo
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
