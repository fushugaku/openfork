using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Tools;

namespace OpenFork.Core.Mcp;

/// <summary>
/// Adapts MCP tools to OpenFork's ITool interface.
/// </summary>
public class McpToolAdapter : ITool
{
    private readonly McpTool _mcpTool;
    private readonly IMcpServerManager _serverManager;
    private readonly ILogger<McpToolAdapter> _logger;

    public string Name => _mcpTool.FullName;

    public string Description => _mcpTool.Description ?? $"MCP tool from {_mcpTool.ServerName}";

    public object ParametersSchema => _mcpTool.InputSchema ?? new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject()
    };

    public McpToolAdapter(
        McpTool mcpTool,
        IMcpServerManager serverManager,
        ILogger<McpToolAdapter> logger)
    {
        _mcpTool = mcpTool;
        _serverManager = serverManager;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(
        string arguments,
        ToolContext context)
    {
        _logger.LogDebug("Executing MCP tool: {Tool} on {Server}",
            _mcpTool.Name, _mcpTool.ServerName);

        JsonNode? argsJson;
        try
        {
            argsJson = JsonNode.Parse(arguments);
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Invalid JSON arguments: {ex.Message}");
        }

        try
        {
            var result = await _serverManager.CallToolAsync(
                _mcpTool.ServerName,
                _mcpTool.Name,
                argsJson ?? new JsonObject());

            if (result.IsError)
            {
                return new ToolResult(false,
                    string.Join("\n", result.Content.Select(c => c.Text ?? "[binary content]")));
            }

            // Format content
            var output = new StringBuilder();
            foreach (var content in result.Content)
            {
                switch (content.Type)
                {
                    case "text":
                        output.AppendLine(content.Text);
                        break;

                    case "image":
                        output.AppendLine($"[Image: {content.MimeType ?? "unknown"}]");
                        break;

                    case "resource":
                        if (content.Resource != null)
                        {
                            output.AppendLine($"[Resource: {content.Resource.Uri}]");
                            if (content.Resource.Text != null)
                            {
                                output.AppendLine(content.Resource.Text);
                            }
                        }
                        break;

                    default:
                        if (content.Text != null)
                        {
                            output.AppendLine(content.Text);
                        }
                        break;
                }
            }

            return new ToolResult(true, output.ToString().TrimEnd());
        }
        catch (McpException ex)
        {
            _logger.LogWarning(ex, "MCP tool failed: {Tool}", _mcpTool.Name);
            return new ToolResult(false, $"MCP error ({ex.Code}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool execution failed: {Tool}", _mcpTool.Name);
            return new ToolResult(false, $"Tool execution failed: {ex.Message}");
        }
    }
}
