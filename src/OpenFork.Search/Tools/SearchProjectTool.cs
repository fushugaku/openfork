using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFork.Core.Tools;
using OpenFork.Search.Services;

namespace OpenFork.Search.Tools;

public class SearchProjectTool : ITool
{
    private readonly ProjectIndexService _indexService;
    private readonly Func<long?> _getActiveProjectId;
    private readonly Func<string?> _getActiveProjectRoot;

    public SearchProjectTool(
        ProjectIndexService indexService,
        Func<long?> getActiveProjectId,
        Func<string?> getActiveProjectRoot)
    {
        _indexService = indexService;
        _getActiveProjectId = getActiveProjectId;
        _getActiveProjectRoot = getActiveProjectRoot;
    }

    public string Name => "search_project";
    
    public string Description => PromptLoader.Load("search_project",
        "Semantic search across the project codebase. Returns relevant code snippets based on natural language queries.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "Natural language search query describing what you're looking for"
            },
            limit = new
            {
                type = "integer",
                description = "Maximum number of results to return (default: 5)"
            }
        },
        required = new[] { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<SearchArgs>(arguments, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (string.IsNullOrWhiteSpace(args?.Query))
                return new ToolResult(false, "Missing required parameter: query");

            var projectId = _getActiveProjectId();
            var projectRoot = _getActiveProjectRoot();

            if (!projectId.HasValue || string.IsNullOrEmpty(projectRoot))
                return new ToolResult(false, "No project selected. Please select a project first.");

            var limit = args.Limit ?? 5;
            var results = await _indexService.SearchAsync(projectId.Value, args.Query, limit);

            if (results.Count == 0)
                return new ToolResult(true, "No matching results found.");

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} results for: \"{args.Query}\"");
            sb.AppendLine();

            foreach (var result in results)
            {
                sb.AppendLine($"📄 {result.RelativePath} (lines {result.StartLine}-{result.EndLine}) [score: {result.Score:F3}]");
                sb.AppendLine("```");
                sb.AppendLine(TruncateContent(result.Content, 500));
                sb.AppendLine("```");
                sb.AppendLine();
            }

            return new ToolResult(true, sb.ToString());
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Search failed: {ex.Message}");
        }
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength)
            return content;
        
        return content[..maxLength] + "\n... (truncated)";
    }

    private record SearchArgs(
        [property: JsonPropertyName("query")] string? Query,
        [property: JsonPropertyName("limit")] int? Limit
    );
}
