using System.Text.Json.Nodes;

namespace OpenFork.Core.Permissions;

/// <summary>
/// Maps tool names to permission categories and extracts resource identifiers.
/// </summary>
public static class ToolPermissionMapping
{
    /// <summary>
    /// Maps tool names to their permission category.
    /// Edit-based tools share the "edit" permission.
    /// </summary>
    public static string GetPermissionCategory(string toolName) => toolName.ToLowerInvariant() switch
    {
        "edit" => "edit",
        "multiedit" => "edit",
        "write" => "edit",
        "editfile" => "edit",
        "writefile" => "edit",
        "multi_edit" => "edit",
        _ => toolName.ToLowerInvariant()
    };

    /// <summary>
    /// Extracts the resource identifier from tool arguments.
    /// </summary>
    public static string ExtractResource(string toolName, JsonNode? arguments)
    {
        if (arguments == null) return "*";

        return toolName.ToLowerInvariant() switch
        {
            "bash" => arguments["command"]?.GetValue<string>() ?? "*",
            "read" or "readfile" => arguments["file_path"]?.GetValue<string>()
                ?? arguments["path"]?.GetValue<string>() ?? "*",
            "edit" or "write" or "multiedit" or "editfile" or "writefile" or "multi_edit"
                => arguments["file_path"]?.GetValue<string>()
                ?? arguments["path"]?.GetValue<string>() ?? "*",
            "glob" => arguments["pattern"]?.GetValue<string>() ?? "*",
            "grep" => arguments["path"]?.GetValue<string>()
                ?? arguments["directory"]?.GetValue<string>() ?? "*",
            "list" => arguments["path"]?.GetValue<string>() ?? "*",
            "webfetch" => arguments["url"]?.GetValue<string>() ?? "*",
            "websearch" => arguments["query"]?.GetValue<string>() ?? "*",
            "task" => arguments["subagent_type"]?.GetValue<string>()
                ?? arguments["agent_type"]?.GetValue<string>() ?? "*",
            "codesearch" => arguments["query"]?.GetValue<string>() ?? "*",
            "lsp" => arguments["action"]?.GetValue<string>() ?? "*",
            "diagnostics" => arguments["path"]?.GetValue<string>() ?? "*",
            "todo" => arguments["action"]?.GetValue<string>() ?? "*",
            "question" => "*",
            _ => "*"
        };
    }

    /// <summary>
    /// Builds the full pattern string for a tool invocation.
    /// </summary>
    public static string BuildPattern(string toolName, JsonNode? arguments)
    {
        var category = GetPermissionCategory(toolName);
        var resource = ExtractResource(toolName, arguments);
        return $"{category}:{resource}";
    }
}
