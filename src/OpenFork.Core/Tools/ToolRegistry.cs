using OpenFork.Core.Chat;
using OpenFork.Core.Domain;
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

    /// <summary>
    /// Gets tool definitions filtered by the agent's tool configuration.
    /// </summary>
    public List<ToolDefinition> GetFilteredToolDefinitions(ToolConfiguration config)
    {
        var filteredTools = config.Mode switch
        {
            ToolFilterMode.All => _tools.Values,
            ToolFilterMode.None => Enumerable.Empty<ITool>(),
            ToolFilterMode.OnlyThese => _tools.Values.Where(t =>
                config.ToolList.Contains(t.Name, StringComparer.OrdinalIgnoreCase)),
            ToolFilterMode.AllExcept => _tools.Values.Where(t =>
                !config.ToolList.Contains(t.Name, StringComparer.OrdinalIgnoreCase)),
            _ => _tools.Values
        };

        return filteredTools.Select(t => new ToolDefinition
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

    /// <summary>
    /// Gets all registered pipeline tools (tools loaded from *.tool.json files).
    /// </summary>
    public IEnumerable<PipelineTool> GetPipelineTools()
    {
        return _tools.Values.OfType<PipelineTool>();
    }

    /// <summary>
    /// Gets pipeline tool names that match a prefix (for autocomplete).
    /// </summary>
    public IEnumerable<string> GetPipelineToolNamesMatching(string prefix)
    {
        var normalizedPrefix = prefix.TrimStart('/').ToLowerInvariant();

        return GetPipelineTools()
            .Where(t => t.Name.ToLowerInvariant().StartsWith(normalizedPrefix))
            .Select(t => t.Name)
            .OrderBy(n => n);
    }
}
