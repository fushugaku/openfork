# Subagent System Implementation Guide

## Overview

The subagent system enables parent agents to delegate work to specialized child agents via the `task` tool. This creates a hierarchical execution model where complex tasks can be broken down and handled by purpose-built agents.

---

## Architecture Analysis

### Current State (OpenFork)

```
┌─────────────────────────────────────┐
│           ChatService               │
│  ┌─────────────────────────────┐   │
│  │      Single Agent Loop      │   │
│  │  (no delegation capability) │   │
│  └─────────────────────────────┘   │
└─────────────────────────────────────┘
```

OpenFork currently has:
- Single-agent execution per session
- No parent-child session relationships
- No Task tool for spawning subagents
- No event communication between agents

### Target State (opencode-aligned)

```
┌─────────────────────────────────────────────────────────┐
│                    Parent Session                        │
│  ┌─────────────────────────────────────────────────┐   │
│  │               Parent Agent (e.g., build)         │   │
│  │                      │                           │   │
│  │                 task tool                        │   │
│  │                      │                           │   │
│  │   ┌──────────────────┼──────────────────┐       │   │
│  │   ▼                  ▼                  ▼       │   │
│  │ ┌─────────┐    ┌─────────┐    ┌─────────┐      │   │
│  │ │ Child 1 │    │ Child 2 │    │ Child 3 │      │   │
│  │ │(explore)│    │(general)│    │(custom) │      │   │
│  │ └─────────┘    └─────────┘    └─────────┘      │   │
│  │       │              │              │          │   │
│  │       └──────────────┴──────────────┘          │   │
│  │                      │                          │   │
│  │              EventBus (PartUpdated)            │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## Domain Model

### New Entities

```csharp
namespace OpenFork.Core.Domain;

/// <summary>
/// Represents a child session spawned by a parent agent.
/// </summary>
public class SubSession
{
    public Guid Id { get; set; }
    public Guid ParentSessionId { get; set; }
    public Guid? ParentMessageId { get; set; }  // The message containing the task tool call
    public string AgentType { get; set; } = "general";
    public SubSessionStatus Status { get; set; } = SubSessionStatus.Pending;
    public string? Prompt { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Inherited permissions from parent + agent defaults
    public PermissionRuleset EffectivePermissions { get; set; } = new();
}

public enum SubSessionStatus
{
    Pending,     // Created but not started
    Running,     // Actively processing
    Completed,   // Successfully finished
    Failed,      // Encountered error
    Cancelled    // Manually cancelled
}

/// <summary>
/// Configuration for subagent types.
/// </summary>
public class SubAgentConfig
{
    public string Type { get; set; } = "general";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public List<string> AllowedTools { get; set; } = new();
    public bool CanSpawnSubagents { get; set; } = false;  // Recursion prevention
    public int MaxIterations { get; set; } = 10;
    public PermissionRuleset DefaultPermissions { get; set; } = new();
}
```

### Built-in Subagent Types

```csharp
public static class BuiltInSubAgents
{
    public static readonly SubAgentConfig General = new()
    {
        Type = "general",
        Name = "General Agent",
        Description = "General-purpose agent for open-ended tasks",
        SystemPrompt = """
            You are a general-purpose assistant helping with various tasks.
            Complete the given task thoroughly and report your findings.
            """,
        AllowedTools = new() { "read", "write", "edit", "glob", "grep", "bash" },
        CanSpawnSubagents = false,
        MaxIterations = 20
    };

    public static readonly SubAgentConfig Explore = new()
    {
        Type = "explore",
        Name = "Codebase Explorer",
        Description = "Specialized agent for exploring and understanding codebases",
        SystemPrompt = """
            You are a codebase exploration specialist. Your job is to:
            1. Find relevant files using glob and grep
            2. Read and analyze code structure
            3. Identify patterns and relationships
            4. Report findings concisely

            DO NOT modify any files. Only read and analyze.
            """,
        AllowedTools = new() { "read", "glob", "grep", "list" },
        CanSpawnSubagents = false,
        MaxIterations = 15
    };

    public static readonly SubAgentConfig Planner = new()
    {
        Type = "planner",
        Name = "Task Planner",
        Description = "Creates implementation plans without executing them",
        SystemPrompt = """
            You are a planning specialist. Your job is to:
            1. Analyze the task requirements
            2. Break down into concrete steps
            3. Identify files that need changes
            4. Create a detailed implementation plan

            DO NOT implement the plan. Only plan and document.
            """,
        AllowedTools = new() { "read", "glob", "grep", "list", "todo" },
        CanSpawnSubagents = false,
        MaxIterations = 10
    };

    public static readonly SubAgentConfig Researcher = new()
    {
        Type = "researcher",
        Name = "Research Agent",
        Description = "Gathers information from web and documentation",
        SystemPrompt = """
            You are a research specialist. Your job is to:
            1. Search for relevant information
            2. Read documentation and articles
            3. Synthesize findings
            4. Report key insights
            """,
        AllowedTools = new() { "webfetch", "websearch", "read" },
        CanSpawnSubagents = false,
        MaxIterations = 10
    };
}
```

---

## Task Tool Implementation

### Tool Definition

```csharp
namespace OpenFork.Core.Tools;

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
        - general: Open-ended tasks
        - explore: Codebase exploration (read-only)
        - planner: Task planning (no execution)
        - researcher: Web research and documentation

        IMPORTANT: Subagents cannot spawn their own subagents.
        """;

    public JsonNode ParametersSchema => JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "subagent_type": {
                    "type": "string",
                    "description": "Type of subagent to spawn",
                    "enum": ["general", "explore", "planner", "researcher"]
                },
                "prompt": {
                    "type": "string",
                    "description": "Detailed task description for the subagent"
                },
                "description": {
                    "type": "string",
                    "description": "Short description (3-5 words) for display"
                },
                "run_in_background": {
                    "type": "boolean",
                    "description": "Run asynchronously without blocking",
                    "default": false
                },
                "max_turns": {
                    "type": "integer",
                    "description": "Maximum agent turns before stopping",
                    "default": 10
                }
            },
            "required": ["subagent_type", "prompt", "description"]
        }
        """)!;

    private readonly ISubagentService _subagentService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TaskTool> _logger;

    public TaskTool(
        ISubagentService subagentService,
        IEventBus eventBus,
        ILogger<TaskTool> logger)
    {
        _subagentService = subagentService;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<ToolResult> ExecuteAsync(
        JsonNode parameters,
        ToolContext context,
        CancellationToken ct = default)
    {
        var subagentType = parameters["subagent_type"]?.GetValue<string>() ?? "general";
        var prompt = parameters["prompt"]?.GetValue<string>()
            ?? throw new ArgumentException("prompt is required");
        var description = parameters["description"]?.GetValue<string>() ?? "Subagent task";
        var runInBackground = parameters["run_in_background"]?.GetValue<bool>() ?? false;
        var maxTurns = parameters["max_turns"]?.GetValue<int>() ?? 10;

        // Check if current agent can spawn subagents
        if (!context.AgentConfig.CanSpawnSubagents)
        {
            return ToolResult.Failure(
                "This agent cannot spawn subagents. Only primary agents can use the task tool.");
        }

        // Create subsession
        var subSession = await _subagentService.CreateSubSessionAsync(
            parentSessionId: context.SessionId,
            parentMessageId: context.MessageId,
            agentType: subagentType,
            prompt: prompt,
            maxTurns: maxTurns,
            ct: ct);

        _logger.LogInformation(
            "Spawned subagent {Type} with ID {Id} for: {Description}",
            subagentType, subSession.Id, description);

        if (runInBackground)
        {
            // Start async and return immediately
            _ = _subagentService.ExecuteSubSessionAsync(subSession.Id, ct);

            return ToolResult.Success($$"""
                Subagent launched in background.
                - ID: {{subSession.Id}}
                - Type: {{subagentType}}
                - Description: {{description}}

                Use 'task_status' to check progress or wait for completion event.
                """);
        }
        else
        {
            // Execute synchronously and return result
            var result = await _subagentService.ExecuteSubSessionAsync(subSession.Id, ct);

            if (result.Status == SubSessionStatus.Completed)
            {
                return ToolResult.Success($$"""
                    ## Subagent Result ({{subagentType}})

                    {{result.Result}}
                    """);
            }
            else
            {
                return ToolResult.Failure($$"""
                    Subagent failed: {{result.Error ?? "Unknown error"}}
                    """);
            }
        }
    }
}
```

---

## Subagent Service

```csharp
namespace OpenFork.Core.Services;

public interface ISubagentService
{
    Task<SubSession> CreateSubSessionAsync(
        Guid parentSessionId,
        Guid? parentMessageId,
        string agentType,
        string prompt,
        int maxTurns = 10,
        CancellationToken ct = default);

    Task<SubSession> ExecuteSubSessionAsync(
        Guid subSessionId,
        CancellationToken ct = default);

    Task<SubSession?> GetSubSessionAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SubSession>> GetSubSessionsForParentAsync(
        Guid parentSessionId,
        CancellationToken ct = default);

    Task CancelSubSessionAsync(Guid subSessionId, CancellationToken ct = default);
}

public class SubagentService : ISubagentService
{
    private readonly ISubSessionRepository _repository;
    private readonly IChatService _chatService;
    private readonly IAgentService _agentService;
    private readonly ISessionService _sessionService;
    private readonly IEventBus _eventBus;
    private readonly IPermissionService _permissionService;
    private readonly ILogger<SubagentService> _logger;

    public SubagentService(
        ISubSessionRepository repository,
        IChatService chatService,
        IAgentService agentService,
        ISessionService sessionService,
        IEventBus eventBus,
        IPermissionService permissionService,
        ILogger<SubagentService> logger)
    {
        _repository = repository;
        _chatService = chatService;
        _agentService = agentService;
        _sessionService = sessionService;
        _eventBus = eventBus;
        _permissionService = permissionService;
        _logger = logger;
    }

    public async Task<SubSession> CreateSubSessionAsync(
        Guid parentSessionId,
        Guid? parentMessageId,
        string agentType,
        string prompt,
        int maxTurns = 10,
        CancellationToken ct = default)
    {
        // Get parent session for context
        var parentSession = await _sessionService.GetByIdAsync(parentSessionId, ct)
            ?? throw new InvalidOperationException($"Parent session {parentSessionId} not found");

        // Get subagent config
        var agentConfig = GetSubAgentConfig(agentType);

        // Merge permissions: parent session + agent defaults
        var parentPermissions = await _permissionService.GetSessionPermissionsAsync(parentSessionId, ct);
        var effectivePermissions = _permissionService.MergePermissions(
            parentPermissions,
            agentConfig.DefaultPermissions);

        var subSession = new SubSession
        {
            Id = Guid.NewGuid(),
            ParentSessionId = parentSessionId,
            ParentMessageId = parentMessageId,
            AgentType = agentType,
            Prompt = prompt,
            Status = SubSessionStatus.Pending,
            EffectivePermissions = effectivePermissions,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repository.CreateAsync(subSession, ct);

        // Emit creation event
        await _eventBus.PublishAsync(new SubSessionCreatedEvent
        {
            SubSessionId = subSession.Id,
            ParentSessionId = parentSessionId,
            AgentType = agentType
        }, ct);

        return subSession;
    }

    public async Task<SubSession> ExecuteSubSessionAsync(
        Guid subSessionId,
        CancellationToken ct = default)
    {
        var subSession = await _repository.GetByIdAsync(subSessionId, ct)
            ?? throw new InvalidOperationException($"SubSession {subSessionId} not found");

        if (subSession.Status != SubSessionStatus.Pending)
        {
            _logger.LogWarning("SubSession {Id} is already {Status}", subSessionId, subSession.Status);
            return subSession;
        }

        // Update status to running
        subSession.Status = SubSessionStatus.Running;
        await _repository.UpdateAsync(subSession, ct);

        await _eventBus.PublishAsync(new SubSessionStatusChangedEvent
        {
            SubSessionId = subSessionId,
            OldStatus = SubSessionStatus.Pending,
            NewStatus = SubSessionStatus.Running
        }, ct);

        try
        {
            // Get agent config and create execution context
            var agentConfig = GetSubAgentConfig(subSession.AgentType);

            // Create a temporary internal session for the subagent
            var internalSession = await _sessionService.CreateAsync(new Session
            {
                ProjectId = (await _sessionService.GetByIdAsync(subSession.ParentSessionId, ct))!.ProjectId,
                Name = $"SubSession-{subSession.Id:N}",
                ActiveAgentId = null  // Will use subagent config directly
            }, ct);

            // Subscribe to progress events
            using var subscription = _eventBus.Subscribe<MessagePartUpdatedEvent>(async evt =>
            {
                if (evt.SessionId == internalSession.Id)
                {
                    // Forward to parent as SubtaskPart update
                    await _eventBus.PublishAsync(new SubSessionProgressEvent
                    {
                        SubSessionId = subSessionId,
                        ParentSessionId = subSession.ParentSessionId,
                        PartType = evt.PartType,
                        Content = evt.Content
                    }, ct);
                }
            });

            // Execute the subagent
            var result = await _chatService.RunSubagentAsync(
                session: internalSession,
                agentConfig: agentConfig,
                prompt: subSession.Prompt!,
                permissions: subSession.EffectivePermissions,
                cancellationToken: ct);

            // Update subsession with result
            subSession.Status = SubSessionStatus.Completed;
            subSession.Result = result;
            subSession.CompletedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(subSession, ct);

            await _eventBus.PublishAsync(new SubSessionCompletedEvent
            {
                SubSessionId = subSessionId,
                ParentSessionId = subSession.ParentSessionId,
                Result = result
            }, ct);

            return subSession;
        }
        catch (OperationCanceledException)
        {
            subSession.Status = SubSessionStatus.Cancelled;
            subSession.CompletedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(subSession, ct);

            await _eventBus.PublishAsync(new SubSessionCancelledEvent
            {
                SubSessionId = subSessionId,
                ParentSessionId = subSession.ParentSessionId
            }, ct);

            return subSession;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubSession {Id} failed", subSessionId);

            subSession.Status = SubSessionStatus.Failed;
            subSession.Error = ex.Message;
            subSession.CompletedAt = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(subSession, ct);

            await _eventBus.PublishAsync(new SubSessionFailedEvent
            {
                SubSessionId = subSessionId,
                ParentSessionId = subSession.ParentSessionId,
                Error = ex.Message
            }, ct);

            return subSession;
        }
    }

    private static SubAgentConfig GetSubAgentConfig(string type) => type switch
    {
        "explore" => BuiltInSubAgents.Explore,
        "planner" => BuiltInSubAgents.Planner,
        "researcher" => BuiltInSubAgents.Researcher,
        _ => BuiltInSubAgents.General
    };

    // ... other methods
}
```

---

## ChatService Extensions

```csharp
// Add to ChatService.cs

public async Task<string> RunSubagentAsync(
    Session session,
    SubAgentConfig agentConfig,
    string prompt,
    PermissionRuleset permissions,
    CancellationToken cancellationToken = default)
{
    // Build tool list restricted to agent's allowed tools
    var allowedTools = _toolRegistry.GetTools()
        .Where(t => agentConfig.AllowedTools.Contains(t.Name))
        .ToList();

    // Build messages with subagent system prompt
    var messages = new List<ChatMessage>
    {
        new()
        {
            Role = "system",
            Content = agentConfig.SystemPrompt
        },
        new()
        {
            Role = "user",
            Content = prompt
        }
    };

    var iteration = 0;
    var maxIterations = agentConfig.MaxIterations;
    var result = new StringBuilder();

    while (iteration < maxIterations)
    {
        iteration++;

        var response = await ExecuteLlmWithToolsAsync(
            session,
            messages,
            allowedTools,
            agentConfig.Provider,
            agentConfig.Model,
            cancellationToken);

        // Accumulate text responses
        if (!string.IsNullOrEmpty(response.Content))
        {
            result.AppendLine(response.Content);
        }

        // Check for tool calls
        if (response.ToolCalls is not { Count: > 0 })
        {
            break;  // No more tool calls, agent is done
        }

        // Execute tool calls with permission checks
        messages.Add(new ChatMessage
        {
            Role = "assistant",
            Content = response.Content,
            ToolCalls = response.ToolCalls
        });

        foreach (var toolCall in response.ToolCalls)
        {
            // Check permission before execution
            var permissionResult = await _permissionService.CheckPermissionAsync(
                permissions,
                toolCall.Function.Name,
                toolCall.Function.Arguments,
                cancellationToken);

            string toolResult;
            if (permissionResult.Action == PermissionAction.Deny)
            {
                toolResult = $"Permission denied: {permissionResult.Reason}";
            }
            else if (permissionResult.Action == PermissionAction.Ask)
            {
                // For subagents, treat "ask" as "deny" (no user interaction)
                toolResult = $"Permission required: {permissionResult.Reason}. Subagents cannot request user permission.";
            }
            else
            {
                toolResult = await ExecuteToolAsync(toolCall, session, cancellationToken);
            }

            messages.Add(new ChatMessage
            {
                Role = "tool",
                ToolCallId = toolCall.Id,
                Content = toolResult
            });
        }
    }

    return result.ToString().Trim();
}
```

---

## Database Schema

```sql
-- Add SubSessions table
CREATE TABLE IF NOT EXISTS SubSessions (
    Id TEXT PRIMARY KEY,
    ParentSessionId TEXT NOT NULL,
    ParentMessageId TEXT,
    AgentType TEXT NOT NULL DEFAULT 'general',
    Status TEXT NOT NULL DEFAULT 'Pending',
    Prompt TEXT,
    Result TEXT,
    Error TEXT,
    EffectivePermissionsJson TEXT,
    CreatedAt TEXT NOT NULL,
    CompletedAt TEXT,
    FOREIGN KEY (ParentSessionId) REFERENCES Sessions(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_subsessions_parent ON SubSessions(ParentSessionId);
CREATE INDEX IF NOT EXISTS idx_subsessions_status ON SubSessions(Status);
```

---

## Event Definitions

```csharp
namespace OpenFork.Core.Events;

public record SubSessionCreatedEvent
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string AgentType { get; init; } = string.Empty;
}

public record SubSessionStatusChangedEvent
{
    public Guid SubSessionId { get; init; }
    public SubSessionStatus OldStatus { get; init; }
    public SubSessionStatus NewStatus { get; init; }
}

public record SubSessionProgressEvent
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string PartType { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

public record SubSessionCompletedEvent
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string Result { get; init; } = string.Empty;
}

public record SubSessionFailedEvent
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string Error { get; init; } = string.Empty;
}

public record SubSessionCancelledEvent
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
}
```

---

## UI Integration

### TUI Display

```csharp
// In ConsoleApp.Chat.cs

private void RenderSubSessionProgress(SubSessionProgressEvent evt)
{
    var color = evt.PartType switch
    {
        "text" => Color.White,
        "tool" => Color.Cyan,
        "error" => Color.Red,
        _ => Color.Grey
    };

    AnsiConsole.MarkupLine($"  [dim]↳ Subagent:[/] [{color}]{Markup.Escape(evt.Content.Truncate(100))}[/]");
}

private void RenderSubSessionCompletion(SubSessionCompletedEvent evt)
{
    AnsiConsole.MarkupLine("[green]✓[/] Subagent completed");

    var panel = new Panel(evt.Result)
    {
        Header = new PanelHeader("Subagent Result"),
        Border = BoxBorder.Rounded,
        BorderStyle = new Style(Color.Green)
    };
    AnsiConsole.Write(panel);
}
```

---

## Testing Strategy

### Unit Tests

```csharp
[Fact]
public async Task CreateSubSession_SetsCorrectParentRelationship()
{
    // Arrange
    var parentSessionId = Guid.NewGuid();
    var service = CreateService();

    // Act
    var subSession = await service.CreateSubSessionAsync(
        parentSessionId: parentSessionId,
        parentMessageId: null,
        agentType: "explore",
        prompt: "Find all API endpoints");

    // Assert
    Assert.Equal(parentSessionId, subSession.ParentSessionId);
    Assert.Equal("explore", subSession.AgentType);
    Assert.Equal(SubSessionStatus.Pending, subSession.Status);
}

[Fact]
public async Task ExecuteSubSession_InheritsParentPermissions()
{
    // Arrange
    var parentPermissions = new PermissionRuleset
    {
        Rules = new[]
        {
            new PermissionRule { Pattern = "bash:*", Action = PermissionAction.Deny }
        }
    };

    // Act & Assert
    // Verify bash tool is denied in subagent context
}

[Fact]
public async Task SubAgent_CannotSpawnSubagents()
{
    // Verify recursion prevention
}
```

### Integration Tests

```csharp
[Fact]
public async Task TaskTool_ExecutesSubagentAndReturnsResult()
{
    // Full integration test with real subagent execution
}

[Fact]
public async Task BackgroundSubagent_EmitsProgressEvents()
{
    // Verify event communication
}
```

---

## Migration Path

### Step 1: Add Database Schema
```bash
# Run schema migration
dotnet run -- migrate add-subsessions
```

### Step 2: Register New Services
```csharp
// In Program.cs
services.AddSingleton<ISubagentService, SubagentService>();
services.AddSingleton<ISubSessionRepository, SubSessionRepository>();
services.AddSingleton<TaskTool>();
```

### Step 3: Update Tool Registry
```csharp
// Add TaskTool to registry
registry.Register(serviceProvider.GetRequiredService<TaskTool>());
```

### Step 4: Update Primary Agents
```json
// In appsettings.json, mark primary agents
{
  "Agents": [
    {
      "Name": "coder",
      "CanSpawnSubagents": true,  // Enable task tool
      ...
    }
  ]
}
```

---

## Performance Considerations

1. **Connection Pooling**: SubSessions use same SQLite connection pool
2. **Event Batching**: Progress events batched at 16ms intervals
3. **Memory**: Each subagent maintains minimal message history
4. **Cancellation**: Proper CancellationToken propagation prevents orphaned executions
5. **Cleanup**: Failed/cancelled subsessions cleaned up on parent session close

---

## Security Considerations

1. **Recursion Prevention**: `CanSpawnSubagents = false` for all subagent types
2. **Permission Inheritance**: Subagents can't exceed parent permissions
3. **Tool Restrictions**: Subagents have limited tool access by type
4. **No User Interaction**: Subagents cannot prompt user (ask → deny)
5. **Timeout**: Max iterations prevent infinite loops

---

## Next Steps

1. Implement EventBus (prerequisite)
2. Implement PermissionService (prerequisite)
3. Add SubSession repository
4. Implement TaskTool
5. Add UI integration
6. Write comprehensive tests
