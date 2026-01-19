using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Tools;

namespace OpenFork.Core.Services;

/// <summary>
/// Loads pipeline tools from *.tool.json files.
/// </summary>
public class ToolFileLoader : IToolFileLoader
{
    private readonly ISubagentService _subagentService;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<ToolFileLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public ToolFileLoader(
        ISubagentService subagentService,
        ToolRegistry toolRegistry,
        ILogger<ToolFileLoader> logger,
        ILoggerFactory loggerFactory)
    {
        _subagentService = subagentService;
        _toolRegistry = toolRegistry;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<List<ITool>> LoadToolsAsync(string directory, CancellationToken ct = default)
    {
        var tools = new List<ITool>();

        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Tools directory does not exist: {Directory}", directory);
            return tools;
        }

        var toolFiles = Directory.GetFiles(directory, "*.tool.json", SearchOption.TopDirectoryOnly);

        _logger.LogInformation("Found {Count} tool definition files in {Directory}", toolFiles.Length, directory);

        foreach (var filePath in toolFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tool = await LoadToolAsync(filePath, ct);
            if (tool != null)
            {
                tools.Add(tool);
            }
        }

        return tools;
    }

    public async Task<ITool?> LoadToolAsync(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        try
        {
            _logger.LogDebug("Loading tool definition from {FilePath}", filePath);

            var json = await File.ReadAllTextAsync(filePath, ct);
            var definition = JsonSerializer.Deserialize<PipelineToolDefinition>(json, JsonOptions);

            if (definition == null)
            {
                _logger.LogWarning("Failed to deserialize tool definition from {FilePath}", filePath);
                return null;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                _logger.LogWarning("Tool definition in {FilePath} has no name", filePath);
                return null;
            }

            if (definition.Pipeline.Count == 0)
            {
                _logger.LogWarning("Tool definition '{Name}' in {FilePath} has no pipeline steps", definition.Name, filePath);
                return null;
            }

            // Validate pipeline steps
            for (var i = 0; i < definition.Pipeline.Count; i++)
            {
                var step = definition.Pipeline[i];
                var stepNum = i + 1;

                // Each step must have either an agent or a tool specified
                var hasAgent = !string.IsNullOrWhiteSpace(step.Agent);
                var hasTool = !string.IsNullOrWhiteSpace(step.Tool);

                if (!hasAgent && !hasTool)
                {
                    _logger.LogWarning(
                        "Tool definition '{Name}' step {StepNum} has neither agent nor tool specified",
                        definition.Name, stepNum);
                    return null;
                }

                if (hasAgent && hasTool)
                {
                    _logger.LogWarning(
                        "Tool definition '{Name}' step {StepNum} has both agent and tool specified (use one)",
                        definition.Name, stepNum);
                    return null;
                }

                // Validate that step has a prompt/arguments
                if (string.IsNullOrWhiteSpace(step.GetEffectivePrompt()))
                {
                    _logger.LogWarning(
                        "Tool definition '{Name}' step {StepNum} has no prompt/arguments specified",
                        definition.Name, stepNum);
                    return null;
                }
            }

            var pipelineToolLogger = _loggerFactory.CreateLogger<PipelineTool>();
            var tool = new PipelineTool(definition, _subagentService, _toolRegistry, pipelineToolLogger);

            _logger.LogInformation(
                "Loaded pipeline tool '{Name}' with {StepCount} steps from {FilePath}",
                definition.Name, definition.Pipeline.Count, filePath);

            return tool;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in tool definition file {FilePath}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tool definition from {FilePath}", filePath);
            return null;
        }
    }
}
