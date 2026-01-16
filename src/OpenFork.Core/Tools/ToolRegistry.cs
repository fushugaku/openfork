using OpenFork.Core.Chat;
using OpenFork.Core.Lsp;

namespace OpenFork.Core.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public ToolRegistry(LspService? lspService = null)
    {
        Register(new ReadFileTool());
        Register(new EditFileTool());
        Register(new MultiEditTool());
        Register(new WriteFileTool());
        Register(new BashTool());
        Register(new GlobTool());
        Register(new GrepTool());
        Register(new ListTool());
        Register(new WebFetchTool());
        Register(new WebSearchTool());
        Register(new CodeSearchTool());
        Register(new QuestionTool());
        Register(new DiagnosticsTool(lspService));
        if (lspService != null)
            Register(new LspTool(lspService));
        Register(new TodoWriteTool());
        Register(new TodoReadTool());
    }

    public void Register(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public ITool? Get(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IEnumerable<ITool> All => _tools.Values;

    public List<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition
        {
            Type = "function",
            Function = new ToolDefinitionFunction
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.ParametersSchema
            }
        }).ToList();
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, string arguments, ToolContext context)
    {
        var tool = Get(toolName);
        if (tool == null)
            return new ToolResult(false, $"Unknown tool: {toolName}");

        return await tool.ExecuteAsync(arguments, context);
    }
}
