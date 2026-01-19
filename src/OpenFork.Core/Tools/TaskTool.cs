using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Domain;
using OpenFork.Core.Events;
using OpenFork.Core.Services;

namespace OpenFork.Core.Tools;

/// <summary>
/// Tool for spawning subagents to handle delegated tasks.
/// </summary>
public class TaskTool : ITool
{
    public string Name => "task";

    public string Description => """
        Launch a specialized subagent to handle a specific task.
        The subagent runs in its own session and returns results when complete.

        Use this when:
        - Task requires specialized capabilities (exploration, planning, research)
        - You want to delegate work while continuing other tasks
        - The task is well-defined and can be completed independently

        Available subagent types:
        - general: Open-ended tasks with full tool access
        - explore: Codebase exploration (read-only)
        - planner-sub: Task planning (no execution)
        - researcher: Web research and documentation

        IMPORTANT: Subagents cannot spawn their own subagents.
        """;

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            subagent_type = new
            {
                type = "string",
                description = "Type of subagent to spawn",
                @enum = new[] { "general", "explore", "planner-sub", "researcher" }
            },
            prompt = new
            {
                type = "string",
                description = "Detailed task description for the subagent"
            },
            description = new
            {
                type = "string",
                description = "Short description (3-5 words) for display"
            },
            run_in_background = new
            {
                type = "boolean",
                description = "Run asynchronously without blocking",
                @default = false
            },
            max_turns = new
            {
                type = "integer",
                description = "Maximum agent turns before stopping",
                @default = 10
            }
        },
        required = new[] { "subagent_type", "prompt", "description" }
    };

    private readonly ISubagentService _subagentService;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TaskTool> _logger;

    public TaskTool(
        ISubagentService subagentService,
        IAgentRegistry agentRegistry,
        IEventBus eventBus,
        ILogger<TaskTool> logger)
    {
        _subagentService = subagentService;
        _agentRegistry = agentRegistry;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<TaskToolArgs>(arguments,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (args == null)
            {
                return new ToolResult(false, "Invalid arguments: could not parse JSON");
            }

            if (string.IsNullOrWhiteSpace(args.Prompt))
            {
                return new ToolResult(false, "prompt is required");
            }

            if (string.IsNullOrWhiteSpace(args.SubagentType))
            {
                return new ToolResult(false, "subagent_type is required");
            }

            // Check if current agent can spawn subagents
            if (context.AgentConfig?.CanSpawnSubagents != true)
            {
                return new ToolResult(false,
                    "This agent cannot spawn subagents. Only primary agents can use the task tool.");
            }

            // Validate subagent type exists and is allowed
            var subagent = _agentRegistry.GetBySlug(args.SubagentType);
            if (subagent == null)
            {
                return new ToolResult(false, $"Unknown subagent type: {args.SubagentType}");
            }

            if (subagent.Category != AgentCategory.Subagent)
            {
                return new ToolResult(false, $"'{args.SubagentType}' is not a valid subagent type");
            }

            // Check if parent agent allows this specific subagent type
            var parentAgent = _agentRegistry.GetById(context.AgentConfig.Id);
            if (parentAgent != null && !_subagentService.CanSpawnSubagent(parentAgent, args.SubagentType))
            {
                return new ToolResult(false,
                    $"This agent is not allowed to spawn subagent type '{args.SubagentType}'");
            }

            // Get session ID from context
            if (context.SessionId == null)
            {
                return new ToolResult(false, "No session context available");
            }

            var description = args.Description ?? "Subagent task";
            var maxTurns = Math.Max(1, Math.Min(args.MaxTurns ?? 10, subagent.MaxIterations));

            // Create subsession
            var subSession = await _subagentService.CreateSubSessionAsync(
                parentSessionId: context.SessionId.Value,
                parentMessageId: context.MessageId,
                agentSlug: args.SubagentType,
                prompt: args.Prompt,
                description: description,
                maxIterations: maxTurns);

            _logger.LogInformation(
                "Spawned subagent {Type} with ID {Id} for: {Description}",
                args.SubagentType, subSession.Id, description);

            if (args.RunInBackground == true)
            {
                // Start async and return immediately
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _subagentService.ExecuteSubSessionAsync(subSession.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background subagent {Id} failed", subSession.Id);
                    }
                });

                return new ToolResult(true, $"""
                    Subagent launched in background.
                    - ID: {subSession.Id}
                    - Type: {args.SubagentType}
                    - Description: {description}

                    The subagent is working on:
                    {args.Prompt}

                    You will be notified when it completes. Continue with other work in the meantime.
                    """);
            }
            else
            {
                // Execute synchronously and return result
                var result = await _subagentService.ExecuteSubSessionAsync(subSession.Id);

                if (result.Status == SubSessionStatus.Completed)
                {
                    return new ToolResult(true, $"""
                        ## Subagent Result ({args.SubagentType})

                        **Task:** {description}

                        **Result:**
                        {result.Result ?? "(No output)"}
                        """);
                }
                else if (result.Status == SubSessionStatus.Cancelled)
                {
                    return new ToolResult(false, $"Subagent was cancelled: {result.Error ?? "No reason provided"}");
                }
                else
                {
                    return new ToolResult(false, $"Subagent failed: {result.Error ?? "Unknown error"}");
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse task tool arguments");
            return new ToolResult(false, $"Invalid JSON arguments: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task tool execution failed");
            return new ToolResult(false, $"Error: {ex.Message}");
        }
    }

    private class TaskToolArgs
    {
        public string? SubagentType { get; set; }
        public string? Prompt { get; set; }
        public string? Description { get; set; }
        public bool? RunInBackground { get; set; }
        public int? MaxTurns { get; set; }
    }
}
