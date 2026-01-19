using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Domain;
using OpenFork.Core.Services;

namespace OpenFork.Core.Tools;

/// <summary>
/// A tool that executes a pipeline of subagents and/or tools when invoked.
/// Loaded from *.tool.json files and registered dynamically.
/// </summary>
public class PipelineTool : ITool
{
    private readonly PipelineToolDefinition _definition;
    private readonly ISubagentService _subagentService;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<PipelineTool> _logger;

    private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
    private static readonly Regex LastOutputPlaceholder = new(@"\{\{_lastOutput\}\}", RegexOptions.Compiled);
    private static readonly Regex FullHistoryPlaceholder = new(@"\{\{_fullHistory\}\}", RegexOptions.Compiled);

    public PipelineTool(
        PipelineToolDefinition definition,
        ISubagentService subagentService,
        ToolRegistry toolRegistry,
        ILogger<PipelineTool> logger)
    {
        _definition = definition;
        _subagentService = subagentService;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public string Name => _definition.Name;

    public string Description => _definition.Description;

    public object ParametersSchema => _definition.Parameters.HasValue
        ? JsonSerializer.Deserialize<object>(_definition.Parameters.Value.GetRawText())!
        : new { type = "object", properties = new { } };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        // Parse arguments to extract parameter values
        Dictionary<string, JsonElement> parameters;
        try
        {
            parameters = string.IsNullOrWhiteSpace(arguments)
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)
                  ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException ex)
        {
            return new ToolResult(false, $"Invalid JSON arguments: {ex.Message}");
        }

        // Validate required parameters
        var validationResult = ValidateRequiredParameters(parameters);
        if (!validationResult.IsValid)
        {
            return new ToolResult(false, validationResult.ErrorMessage);
        }

        if (_definition.Pipeline.Count == 0)
        {
            return new ToolResult(false, "Pipeline has no steps defined.");
        }

        _logger.LogInformation(
            "Executing pipeline tool '{Name}' with {StepCount} steps",
            Name, _definition.Pipeline.Count);

        var results = new List<StepResult>();
        string? lastOutput = null;
        var fullHistory = new StringBuilder();

        for (var i = 0; i < _definition.Pipeline.Count; i++)
        {
            var step = _definition.Pipeline[i];
            var stepNumber = i + 1;
            var stepName = step.Name ?? step.GetTarget();
            var stepType = step.IsTool ? "tool" : "agent";

            _logger.LogDebug(
                "Pipeline '{Name}' step {StepNumber}/{TotalSteps}: {Type}={Target}, handoff={Handoff}",
                Name, stepNumber, _definition.Pipeline.Count, stepType, step.GetTarget(), step.Handoff);

            StepResult stepResult;

            try
            {
                if (step.IsTool)
                {
                    stepResult = await ExecuteToolStepAsync(
                        step, stepNumber, parameters, lastOutput, fullHistory.ToString(), context);
                }
                else
                {
                    stepResult = await ExecuteAgentStepAsync(
                        step, stepNumber, parameters, lastOutput, fullHistory.ToString(), context);
                }

                results.Add(stepResult);

                // Update tracking
                lastOutput = stepResult.Output;
                fullHistory.AppendLine($"--- Step {stepNumber} ({stepName}) ---");
                fullHistory.AppendLine(stepResult.Output);
                fullHistory.AppendLine();

                // Check for failure
                if (!stepResult.Success)
                {
                    _logger.LogWarning(
                        "Pipeline '{Name}' failed at step {StepNumber}: {Error}",
                        Name, stepNumber, stepResult.Output);

                    return new ToolResult(false, FormatResults(results, failed: true));
                }

                _logger.LogDebug(
                    "Pipeline '{Name}' step {StepNumber} completed successfully",
                    Name, stepNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Pipeline '{Name}' step {StepNumber} threw exception",
                    Name, stepNumber);

                results.Add(new StepResult
                {
                    StepNumber = stepNumber,
                    StepName = stepName,
                    StepType = stepType,
                    Success = false,
                    Output = $"Error: {ex.Message}"
                });

                return new ToolResult(false, FormatResults(results, failed: true));
            }
        }

        _logger.LogInformation(
            "Pipeline '{Name}' completed successfully with {StepCount} steps",
            Name, _definition.Pipeline.Count);

        return new ToolResult(true, FormatResults(results, failed: false));
    }

    private async Task<StepResult> ExecuteAgentStepAsync(
        PipelineStepDefinition step,
        int stepNumber,
        Dictionary<string, JsonElement> parameters,
        string? lastOutput,
        string fullHistory,
        ToolContext context)
    {
        var agentSlug = step.Agent ?? throw new InvalidOperationException("Agent step missing agent slug");
        var stepName = step.Name ?? agentSlug;

        // Build prompt with placeholders replaced
        var prompt = ReplacePlaceholders(step.GetEffectivePrompt(), parameters, lastOutput, fullHistory);

        // Add context based on handoff mode (for agent steps, prepend to prompt)
        if (!string.IsNullOrEmpty(lastOutput))
        {
            var contextPrefix = step.Handoff.ToLowerInvariant() switch
            {
                "full" => $"Previous context:\n{fullHistory}\n\n",
                "last" => $"Previous step result:\n{lastOutput}\n\n",
                "none" => "",
                _ => $"Previous step result:\n{lastOutput}\n\n"
            };

            // Only add if not already included via placeholder
            if (!step.GetEffectivePrompt().Contains("{{_lastOutput}}") &&
                !step.GetEffectivePrompt().Contains("{{_fullHistory}}"))
            {
                prompt = contextPrefix + prompt;
            }
        }

        // Check if we have session context for subagent execution
        if (!context.SessionId.HasValue)
        {
            return new StepResult
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StepType = "agent",
                Success = false,
                Output = "Agent steps require an active session context"
            };
        }

        try
        {
            var subSession = await _subagentService.CreateSubSessionAsync(
                parentSessionId: context.SessionId.Value,
                parentMessageId: context.MessageId,
                agentSlug: agentSlug,
                prompt: prompt,
                description: $"Pipeline '{Name}' step {stepNumber}: {stepName}");

            var executedSession = await _subagentService.ExecuteSubSessionAsync(subSession.Id);

            var success = executedSession.Status == SubSessionStatus.Completed;
            var output = executedSession.Result ?? executedSession.Error ?? "No output";

            return new StepResult
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StepType = "agent",
                Success = success,
                Output = output
            };
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Unknown agent type"))
        {
            return new StepResult
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StepType = "agent",
                Success = false,
                Output = $"Unknown agent type: {agentSlug}"
            };
        }
    }

    private async Task<StepResult> ExecuteToolStepAsync(
        PipelineStepDefinition step,
        int stepNumber,
        Dictionary<string, JsonElement> parameters,
        string? lastOutput,
        string fullHistory,
        ToolContext context)
    {
        var toolName = step.Tool ?? throw new InvalidOperationException("Tool step missing tool name");
        var stepName = step.Name ?? toolName;

        // Get the tool
        var tool = _toolRegistry.Get(toolName);
        if (tool == null)
        {
            return new StepResult
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StepType = "tool",
                Success = false,
                Output = $"Unknown tool: {toolName}"
            };
        }

        // Build arguments with placeholders replaced
        var toolArguments = ReplacePlaceholders(step.GetEffectivePrompt(), parameters, lastOutput, fullHistory);

        // For tools, we can inject previous output into arguments if needed
        // The handoff mode determines how we structure the arguments
        if (step.Handoff.ToLowerInvariant() != "none" && !string.IsNullOrEmpty(lastOutput))
        {
            // If the arguments template doesn't use placeholders, we might need to inject context
            // This depends on the tool - some tools accept a "context" or "query" parameter
            // For now, we assume the template handles this via {{_lastOutput}}
        }

        _logger.LogDebug(
            "Executing tool '{ToolName}' with arguments: {Arguments}",
            toolName, toolArguments.Length > 200 ? toolArguments[..200] + "..." : toolArguments);

        try
        {
            var result = await tool.ExecuteAsync(toolArguments, context);

            return new StepResult
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StepType = "tool",
                Success = result.Success,
                Output = result.Output
            };
        }
        catch (Exception ex)
        {
            return new StepResult
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StepType = "tool",
                Success = false,
                Output = $"Tool execution failed: {ex.Message}"
            };
        }
    }

    private static string ReplacePlaceholders(
        string template,
        Dictionary<string, JsonElement> parameters,
        string? lastOutput,
        string fullHistory)
    {
        // Replace special placeholders first
        var result = LastOutputPlaceholder.Replace(template, lastOutput ?? "");
        result = FullHistoryPlaceholder.Replace(result, fullHistory);

        // Replace parameter placeholders
        return PlaceholderRegex.Replace(result, match =>
        {
            var paramName = match.Groups[1].Value;

            // Skip special placeholders (already handled)
            if (paramName.StartsWith("_"))
                return match.Value;

            if (parameters.TryGetValue(paramName, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? "",
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => value.GetRawText()
                };
            }

            // Keep placeholder if parameter not provided
            return match.Value;
        });
    }

    private string FormatResults(List<StepResult> results, bool failed)
    {
        var sb = new StringBuilder();

        if (failed)
        {
            sb.AppendLine($"Pipeline '{Name}' failed.");
        }
        else
        {
            sb.AppendLine($"Pipeline '{Name}' completed successfully.");
        }

        sb.AppendLine();

        foreach (var result in results)
        {
            var statusIcon = result.Success ? "✓" : "✗";
            var typeIcon = result.StepType == "tool" ? "🔧" : "🤖";

            sb.AppendLine($"[Step {result.StepNumber}] {typeIcon} {result.StepName} {statusIcon}");
            sb.AppendLine(result.Output);
            sb.AppendLine();
        }

        // Return the last successful output as the primary result
        var lastSuccessful = results.LastOrDefault(r => r.Success);
        if (lastSuccessful != null && !failed)
        {
            return lastSuccessful.Output;
        }

        return sb.ToString();
    }

    private class StepResult
    {
        public int StepNumber { get; init; }
        public string StepName { get; init; } = string.Empty;
        public string StepType { get; init; } = "agent";
        public bool Success { get; init; }
        public string Output { get; init; } = string.Empty;
    }

    /// <summary>
    /// Validates that all required parameters are provided.
    /// </summary>
    private ValidationResult ValidateRequiredParameters(Dictionary<string, JsonElement> parameters)
    {
        var (requiredParams, parameterDescriptions) = GetParameterInfo();

        if (requiredParams.Count == 0)
        {
            return ValidationResult.Valid();
        }

        var missingParams = requiredParams
            .Where(p => !parameters.ContainsKey(p) ||
                        parameters[p].ValueKind == JsonValueKind.Null ||
                        (parameters[p].ValueKind == JsonValueKind.String &&
                         string.IsNullOrWhiteSpace(parameters[p].GetString())))
            .ToList();

        if (missingParams.Count == 0)
        {
            return ValidationResult.Valid();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Missing required parameter(s) for tool '{Name}':");
        sb.AppendLine();

        foreach (var param in missingParams)
        {
            var description = parameterDescriptions.GetValueOrDefault(param, "No description available");
            sb.AppendLine($"  • {param} (required)");
            sb.AppendLine($"    Description: {description}");
        }

        sb.AppendLine();
        sb.AppendLine("Please provide the missing parameters to execute this tool.");

        return ValidationResult.Invalid(sb.ToString());
    }

    /// <summary>
    /// Gets information about all parameters: required list and descriptions.
    /// </summary>
    public (List<string> Required, Dictionary<string, string> Descriptions) GetParameterInfo()
    {
        var required = new List<string>();
        var descriptions = new Dictionary<string, string>();

        if (!_definition.Parameters.HasValue)
        {
            return (required, descriptions);
        }

        var schema = _definition.Parameters.Value;

        // Get required array
        if (schema.TryGetProperty("required", out var requiredElement) &&
            requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    required.Add(item.GetString()!);
                }
            }
        }

        // Get property descriptions
        if (schema.TryGetProperty("properties", out var propsElement) &&
            propsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in propsElement.EnumerateObject())
            {
                var desc = "No description";
                if (prop.Value.TryGetProperty("description", out var descElement) &&
                    descElement.ValueKind == JsonValueKind.String)
                {
                    desc = descElement.GetString() ?? desc;
                }

                // Also get type for better messages
                var type = "any";
                if (prop.Value.TryGetProperty("type", out var typeElement) &&
                    typeElement.ValueKind == JsonValueKind.String)
                {
                    type = typeElement.GetString() ?? type;
                }

                descriptions[prop.Name] = $"{desc} (type: {type})";
            }
        }

        return (required, descriptions);
    }

    private record ValidationResult(bool IsValid, string ErrorMessage)
    {
        public static ValidationResult Valid() => new(true, string.Empty);
        public static ValidationResult Invalid(string message) => new(false, message);
    }
}
