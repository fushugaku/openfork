namespace OpenFork.Core.Chat;

public class ChatMessage
{
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Name { get; set; }
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "function";
    public ToolFunction Function { get; set; } = new();
}

public class ToolFunction
{
    public string Name { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
}
