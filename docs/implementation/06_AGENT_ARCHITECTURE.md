# Agent Architecture Implementation Guide

## Overview

This guide details implementing a sophisticated agent architecture with specialized agent types, execution modes, and capability isolation.

---

## Architecture Analysis

### Current State (OpenFork)

```csharp
// Current simple agent model
public class AgentProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string SystemPrompt { get; set; }
    public string ProviderKey { get; set; }
    public string Model { get; set; }
    public int MaxIterations { get; set; }
}
```

**Limitations**:
- All agents are equivalent (no type distinction)
- No permission profiles
- No tool restrictions
- No execution mode configuration
- No hidden/internal agents

### Target State (opencode-aligned)

```
┌─────────────────────────────────────────────────────────────────┐
│                    Agent Architecture                            │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    Agent Registry                           │ │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐           │ │
│  │  │  Primary   │  │  Subagent  │  │   Hidden   │           │ │
│  │  │  Agents    │  │   Types    │  │  Agents    │           │ │
│  │  │            │  │            │  │            │           │ │
│  │  │ • build    │  │ • general  │  │ • compact  │           │ │
│  │  │ • plan     │  │ • explore  │  │ • summary  │           │ │
│  │  │ • custom   │  │ • research │  │ • internal │           │ │
│  │  └────────────┘  └────────────┘  └────────────┘           │ │
│  └────────────────────────────────────────────────────────────┘ │
│                              │                                   │
│                              ▼                                   │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                   Agent Configuration                       │ │
│  │                                                             │ │
│  │  • Model & Provider                                         │ │
│  │  • System Prompt (with templating)                         │ │
│  │  • Permission Ruleset                                       │ │
│  │  • Tool Allow/Deny List                                     │ │
│  │  • Execution Limits                                         │ │
│  │  • Capabilities (subagents, etc.)                          │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

---

## Domain Model

### Agent Types

```csharp
namespace OpenFork.Core.Domain;

/// <summary>
/// Categorization of agent types by their role and visibility.
/// </summary>
public enum AgentCategory
{
    /// <summary>
    /// Primary agents visible to users for direct interaction.
    /// Can spawn subagents.
    /// </summary>
    Primary,

    /// <summary>
    /// Subagents spawned by primary agents via Task tool.
    /// Cannot spawn their own subagents.
    /// </summary>
    Subagent,

    /// <summary>
    /// Internal/hidden agents for system tasks.
    /// Not visible in UI, used for compaction, summarization, etc.
    /// </summary>
    Hidden
}

/// <summary>
/// Execution mode determining how the agent processes requests.
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>
    /// Standard agentic loop with tool calling.
    /// </summary>
    Agentic,

    /// <summary>
    /// Single response without tool use.
    /// </summary>
    SingleShot,

    /// <summary>
    /// Streaming response for chat-like interaction.
    /// </summary>
    Streaming,

    /// <summary>
    /// Planning mode - creates plan without execution.
    /// </summary>
    Planning
}
```

### Enhanced Agent Model

```csharp
namespace OpenFork.Core.Domain;

/// <summary>
/// Complete agent configuration with all capabilities.
/// </summary>
public class Agent
{
    // Identity
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;  // URL-friendly identifier
    public string Description { get; set; } = string.Empty;
    public AgentCategory Category { get; set; } = AgentCategory.Primary;

    // LLM Configuration
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int? MaxTokens { get; set; }

    // Prompting
    public string SystemPrompt { get; set; } = string.Empty;
    public Dictionary<string, string> PromptVariables { get; set; } = new();
    public bool UseDefaultSystemPrefix { get; set; } = true;

    // Execution
    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.Agentic;
    public int MaxIterations { get; set; } = 30;
    public TimeSpan? Timeout { get; set; }

    // Capabilities
    public bool CanSpawnSubagents { get; set; }
    public List<string> AllowedSubagentTypes { get; set; } = new();

    // Tools
    public ToolConfiguration Tools { get; set; } = new();

    // Permissions
    public PermissionRuleset Permissions { get; set; } = new();

    // UI
    public string? IconEmoji { get; set; }
    public string? Color { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsVisible { get; set; } = true;

    // Metadata
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsBuiltIn { get; set; }
}

/// <summary>
/// Tool configuration for an agent.
/// </summary>
public class ToolConfiguration
{
    /// <summary>
    /// Mode for tool filtering.
    /// </summary>
    public ToolFilterMode Mode { get; set; } = ToolFilterMode.AllExcept;

    /// <summary>
    /// Tools to include (if Mode is OnlyThese) or exclude (if Mode is AllExcept).
    /// </summary>
    public List<string> ToolList { get; set; } = new();

    /// <summary>
    /// Per-tool configuration overrides.
    /// </summary>
    public Dictionary<string, ToolOverride> Overrides { get; set; } = new();
}

public enum ToolFilterMode
{
    All,        // All tools available
    AllExcept,  // All except specified
    OnlyThese,  // Only specified tools
    None        // No tools
}

public record ToolOverride
{
    public int? MaxOutputLength { get; init; }
    public TimeSpan? Timeout { get; init; }
    public Dictionary<string, object>? DefaultArguments { get; init; }
}
```

---

## Built-in Agents

```csharp
namespace OpenFork.Core.Agents;

public static class BuiltInAgents
{
    // ═══════════════════════════════════════════════════════════════
    // PRIMARY AGENTS
    // ═══════════════════════════════════════════════════════════════

    public static readonly Agent Coder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Name = "Coder",
        Slug = "coder",
        Description = "Full-featured coding assistant with file editing and command execution",
        Category = AgentCategory.Primary,
        IconEmoji = "💻",
        Color = "#3B82F6",
        IsBuiltIn = true,
        CanSpawnSubagents = true,
        AllowedSubagentTypes = new() { "explore", "general", "planner", "researcher" },
        MaxIterations = 50,
        ExecutionMode = AgentExecutionMode.Agentic,
        SystemPrompt = """
            You are an expert software engineer. Your role is to help users with coding tasks including:
            - Writing and editing code
            - Debugging issues
            - Explaining code and concepts
            - Refactoring and optimization

            Guidelines:
            - Always read files before editing them
            - Make minimal, focused changes
            - Test changes when possible
            - Explain your reasoning

            You can use the 'task' tool to delegate specialized work to subagents.
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.AllExcept,
            ToolList = new() { }  // All tools allowed
        },
        Permissions = BuiltInRulesets.Primary
    };

    public static readonly Agent Planner = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        Name = "Planner",
        Slug = "planner",
        Description = "Creates implementation plans without executing them",
        Category = AgentCategory.Primary,
        IconEmoji = "📋",
        Color = "#10B981",
        IsBuiltIn = true,
        CanSpawnSubagents = true,
        AllowedSubagentTypes = new() { "explore", "researcher" },
        MaxIterations = 20,
        ExecutionMode = AgentExecutionMode.Planning,
        SystemPrompt = """
            You are a planning specialist. Your role is to analyze tasks and create detailed implementation plans.

            For each task:
            1. Understand the requirements fully
            2. Explore the codebase to understand the context
            3. Identify files that need changes
            4. Create a step-by-step plan

            DO NOT implement the plan. Only analyze and plan.

            Use the todo tool to track your planning steps.
            Use the explore subagent to understand unfamiliar code.
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.OnlyThese,
            ToolList = new() { "read", "glob", "grep", "list", "todo", "task", "question" }
        },
        Permissions = BuiltInRulesets.Planner
    };

    public static readonly Agent Reviewer = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
        Name = "Reviewer",
        Slug = "reviewer",
        Description = "Code review specialist for quality and security analysis",
        Category = AgentCategory.Primary,
        IconEmoji = "🔍",
        Color = "#F59E0B",
        IsBuiltIn = true,
        CanSpawnSubagents = false,
        MaxIterations = 15,
        ExecutionMode = AgentExecutionMode.Agentic,
        SystemPrompt = """
            You are a code review specialist. Your role is to analyze code for:
            - Bugs and potential issues
            - Security vulnerabilities
            - Performance problems
            - Code style and best practices
            - Architecture concerns

            Provide constructive feedback with specific suggestions.
            Reference line numbers when pointing out issues.
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.OnlyThese,
            ToolList = new() { "read", "glob", "grep", "list", "diagnostics" }
        },
        Permissions = BuiltInRulesets.Explorer
    };

    // ═══════════════════════════════════════════════════════════════
    // SUBAGENT TYPES
    // ═══════════════════════════════════════════════════════════════

    public static readonly Agent ExploreSubagent = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0001-000000000001"),
        Name = "Explorer",
        Slug = "explore",
        Description = "Codebase exploration specialist",
        Category = AgentCategory.Subagent,
        IconEmoji = "🔭",
        IsBuiltIn = true,
        CanSpawnSubagents = false,
        MaxIterations = 15,
        ExecutionMode = AgentExecutionMode.Agentic,
        SystemPrompt = """
            You are a codebase exploration specialist. Your task is to find and understand code.

            Guidelines:
            - Use glob to find files by pattern
            - Use grep to search for code patterns
            - Read relevant files to understand structure
            - Summarize your findings concisely

            DO NOT modify any files.
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.OnlyThese,
            ToolList = new() { "read", "glob", "grep", "list" }
        },
        Permissions = BuiltInRulesets.Explorer
    };

    public static readonly Agent GeneralSubagent = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0001-000000000002"),
        Name = "General",
        Slug = "general",
        Description = "General-purpose assistant for various tasks",
        Category = AgentCategory.Subagent,
        IconEmoji = "🤖",
        IsBuiltIn = true,
        CanSpawnSubagents = false,
        MaxIterations = 20,
        ExecutionMode = AgentExecutionMode.Agentic,
        SystemPrompt = """
            You are a general-purpose assistant. Complete the task you've been given.
            Be thorough and report your findings.
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.AllExcept,
            ToolList = new() { "task" }  // Can't spawn sub-subagents
        },
        Permissions = BuiltInRulesets.Primary
    };

    public static readonly Agent ResearchSubagent = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0001-000000000003"),
        Name = "Researcher",
        Slug = "researcher",
        Description = "Web research and documentation specialist",
        Category = AgentCategory.Subagent,
        IconEmoji = "📚",
        IsBuiltIn = true,
        CanSpawnSubagents = false,
        MaxIterations = 10,
        ExecutionMode = AgentExecutionMode.Agentic,
        SystemPrompt = """
            You are a research specialist. Your task is to gather information from the web.

            Guidelines:
            - Search for relevant documentation
            - Read and analyze web content
            - Synthesize findings into clear summaries
            - Cite sources
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.OnlyThese,
            ToolList = new() { "websearch", "webfetch", "read" }
        },
        Permissions = BuiltInRulesets.Researcher
    };

    public static readonly Agent PlannerSubagent = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0001-000000000004"),
        Name = "Task Planner",
        Slug = "planner-sub",
        Description = "Creates focused implementation plans",
        Category = AgentCategory.Subagent,
        IconEmoji = "📝",
        IsBuiltIn = true,
        CanSpawnSubagents = false,
        MaxIterations = 10,
        ExecutionMode = AgentExecutionMode.Planning,
        SystemPrompt = """
            Create a focused implementation plan for the given task.
            Break down into specific, actionable steps.
            Identify files that will need changes.
            """,
        Tools = new ToolConfiguration
        {
            Mode = ToolFilterMode.OnlyThese,
            ToolList = new() { "read", "glob", "grep", "list", "todo" }
        },
        Permissions = BuiltInRulesets.Planner
    };

    // ═══════════════════════════════════════════════════════════════
    // HIDDEN/INTERNAL AGENTS
    // ═══════════════════════════════════════════════════════════════

    public static readonly Agent CompactionAgent = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0002-000000000001"),
        Name = "Compaction",
        Slug = "compaction",
        Description = "Internal agent for conversation summarization",
        Category = AgentCategory.Hidden,
        IsBuiltIn = true,
        IsVisible = false,
        CanSpawnSubagents = false,
        MaxIterations = 1,
        ExecutionMode = AgentExecutionMode.SingleShot,
        SystemPrompt = """
            Summarize the conversation history, preserving:
            - Key decisions made
            - Important context
            - Files modified
            - Current state of the task
            - Pending items

            Be concise but preserve critical information.
            """,
        Tools = new ToolConfiguration { Mode = ToolFilterMode.None }
    };

    public static readonly Agent SummaryAgent = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0002-000000000002"),
        Name = "Summary",
        Slug = "summary",
        Description = "Internal agent for generating session summaries",
        Category = AgentCategory.Hidden,
        IsBuiltIn = true,
        IsVisible = false,
        CanSpawnSubagents = false,
        MaxIterations = 1,
        ExecutionMode = AgentExecutionMode.SingleShot,
        SystemPrompt = """
            Generate a brief summary of the session including:
            - Main topic/task
            - Key outcomes
            - Files changed
            """,
        Tools = new ToolConfiguration { Mode = ToolFilterMode.None }
    };

    // Collection of all built-in agents
    public static readonly IReadOnlyList<Agent> All = new[]
    {
        Coder, Planner, Reviewer,
        ExploreSubagent, GeneralSubagent, ResearchSubagent, PlannerSubagent,
        CompactionAgent, SummaryAgent
    };

    public static readonly IReadOnlyDictionary<string, Agent> BySlug =
        All.ToDictionary(a => a.Slug);
}
```

---

## Agent Registry Service

```csharp
namespace OpenFork.Core.Services;

public interface IAgentRegistry
{
    // Query
    Agent? GetById(Guid id);
    Agent? GetBySlug(string slug);
    IReadOnlyList<Agent> GetAll();
    IReadOnlyList<Agent> GetByCategory(AgentCategory category);
    IReadOnlyList<Agent> GetVisible();
    IReadOnlyList<Agent> GetSubagentTypes();

    // Management (for custom agents)
    Task<Agent> RegisterAsync(Agent agent, CancellationToken ct = default);
    Task UpdateAsync(Agent agent, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Validation
    bool ValidateAgent(Agent agent, out List<string> errors);
    bool CanSpawnSubagent(Agent parent, string subagentSlug);
}

public class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<Guid, Agent> _agents = new();
    private readonly ConcurrentDictionary<string, Agent> _agentsBySlug = new();
    private readonly IAgentRepository _repository;
    private readonly ILogger<AgentRegistry> _logger;

    public AgentRegistry(
        IAgentRepository repository,
        ILogger<AgentRegistry> logger)
    {
        _repository = repository;
        _logger = logger;

        // Register built-in agents
        foreach (var agent in BuiltInAgents.All)
        {
            _agents[agent.Id] = agent;
            _agentsBySlug[agent.Slug] = agent;
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Load custom agents from database
        var customAgents = await _repository.GetAllAsync(ct);
        foreach (var agent in customAgents)
        {
            _agents[agent.Id] = agent;
            _agentsBySlug[agent.Slug] = agent;
        }

        _logger.LogInformation(
            "Agent registry initialized: {BuiltIn} built-in, {Custom} custom",
            BuiltInAgents.All.Count,
            customAgents.Count);
    }

    public Agent? GetById(Guid id) =>
        _agents.TryGetValue(id, out var agent) ? agent : null;

    public Agent? GetBySlug(string slug) =>
        _agentsBySlug.TryGetValue(slug, out var agent) ? agent : null;

    public IReadOnlyList<Agent> GetAll() =>
        _agents.Values.ToList();

    public IReadOnlyList<Agent> GetByCategory(AgentCategory category) =>
        _agents.Values.Where(a => a.Category == category).ToList();

    public IReadOnlyList<Agent> GetVisible() =>
        _agents.Values.Where(a => a.IsVisible).OrderBy(a => a.DisplayOrder).ToList();

    public IReadOnlyList<Agent> GetSubagentTypes() =>
        _agents.Values.Where(a => a.Category == AgentCategory.Subagent).ToList();

    public async Task<Agent> RegisterAsync(Agent agent, CancellationToken ct = default)
    {
        if (!ValidateAgent(agent, out var errors))
        {
            throw new InvalidOperationException($"Invalid agent: {string.Join(", ", errors)}");
        }

        agent.Id = agent.Id == Guid.Empty ? Guid.NewGuid() : agent.Id;
        agent.CreatedAt = DateTimeOffset.UtcNow;
        agent.IsBuiltIn = false;

        await _repository.CreateAsync(agent, ct);

        _agents[agent.Id] = agent;
        _agentsBySlug[agent.Slug] = agent;

        _logger.LogInformation("Registered agent: {Name} ({Slug})", agent.Name, agent.Slug);
        return agent;
    }

    public bool ValidateAgent(Agent agent, out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(agent.Name))
            errors.Add("Name is required");

        if (string.IsNullOrWhiteSpace(agent.Slug))
            errors.Add("Slug is required");
        else if (!Regex.IsMatch(agent.Slug, @"^[a-z0-9-]+$"))
            errors.Add("Slug must be lowercase alphanumeric with hyphens only");

        if (string.IsNullOrWhiteSpace(agent.ProviderId))
            errors.Add("ProviderId is required");

        if (string.IsNullOrWhiteSpace(agent.ModelId))
            errors.Add("ModelId is required");

        if (agent.MaxIterations < 1)
            errors.Add("MaxIterations must be at least 1");

        // Check for slug collision (unless updating same agent)
        if (_agentsBySlug.TryGetValue(agent.Slug, out var existing) && existing.Id != agent.Id)
            errors.Add($"Slug '{agent.Slug}' is already in use");

        return errors.Count == 0;
    }

    public bool CanSpawnSubagent(Agent parent, string subagentSlug)
    {
        if (!parent.CanSpawnSubagents)
            return false;

        if (parent.AllowedSubagentTypes.Count == 0)
            return true;  // Empty list means all allowed

        return parent.AllowedSubagentTypes.Contains(subagentSlug);
    }
}
```

---

## Agent Execution Service

```csharp
namespace OpenFork.Core.Services;

public interface IAgentExecutionService
{
    Task<AgentExecutionResult> ExecuteAsync(
        Agent agent,
        Session session,
        string input,
        CancellationToken ct = default);
}

public record AgentExecutionResult
{
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public int IterationsUsed { get; init; }
    public TokenUsage? TokensUsed { get; init; }
    public TimeSpan Duration { get; init; }
    public List<ToolInvocation> ToolInvocations { get; init; } = new();
}

public class AgentExecutionService : IAgentExecutionService
{
    private readonly IChatService _chatService;
    private readonly IToolRegistry _toolRegistry;
    private readonly IPermissionService _permissionService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AgentExecutionService> _logger;

    public async Task<AgentExecutionResult> ExecuteAsync(
        Agent agent,
        Session session,
        string input,
        CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var toolInvocations = new List<ToolInvocation>();

        await _eventBus.PublishAsync(new AgentExecutionStartedEvent
        {
            AgentId = agent.Id,
            AgentName = agent.Name,
            SessionId = session.Id,
            ExecutionMode = agent.ExecutionMode
        }, ct);

        try
        {
            // Build effective configuration
            var effectiveTools = BuildEffectiveToolList(agent);
            var effectivePermissions = _permissionService.MergeRulesets(
                agent.Permissions,
                await _permissionService.GetSessionPermissionsAsync(session.Id, ct));

            // Build system prompt with variables
            var systemPrompt = RenderSystemPrompt(agent);

            // Execute based on mode
            string output;
            int iterations;
            TokenUsage? tokens;

            switch (agent.ExecutionMode)
            {
                case AgentExecutionMode.SingleShot:
                    (output, tokens) = await ExecuteSingleShotAsync(
                        agent, session, input, systemPrompt, ct);
                    iterations = 1;
                    break;

                case AgentExecutionMode.Planning:
                    (output, iterations, tokens, toolInvocations) = await ExecutePlanningAsync(
                        agent, session, input, systemPrompt, effectiveTools, effectivePermissions, ct);
                    break;

                case AgentExecutionMode.Agentic:
                default:
                    (output, iterations, tokens, toolInvocations) = await ExecuteAgenticAsync(
                        agent, session, input, systemPrompt, effectiveTools, effectivePermissions, ct);
                    break;
            }

            var duration = DateTimeOffset.UtcNow - startTime;

            await _eventBus.PublishAsync(new AgentExecutionCompletedEvent
            {
                AgentId = agent.Id,
                SessionId = session.Id,
                Success = true,
                Iterations = iterations,
                Duration = duration
            }, ct);

            return new AgentExecutionResult
            {
                Success = true,
                Output = output,
                IterationsUsed = iterations,
                TokensUsed = tokens,
                Duration = duration,
                ToolInvocations = toolInvocations
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent execution failed: {Agent}", agent.Name);

            await _eventBus.PublishAsync(new AgentExecutionFailedEvent
            {
                AgentId = agent.Id,
                SessionId = session.Id,
                Error = ex.Message
            }, ct);

            return new AgentExecutionResult
            {
                Success = false,
                Error = ex.Message,
                Duration = DateTimeOffset.UtcNow - startTime,
                ToolInvocations = toolInvocations
            };
        }
    }

    private IReadOnlyList<ITool> BuildEffectiveToolList(Agent agent)
    {
        var allTools = _toolRegistry.GetTools();

        return agent.Tools.Mode switch
        {
            ToolFilterMode.All => allTools.ToList(),
            ToolFilterMode.None => new List<ITool>(),
            ToolFilterMode.OnlyThese => allTools
                .Where(t => agent.Tools.ToolList.Contains(t.Name))
                .ToList(),
            ToolFilterMode.AllExcept => allTools
                .Where(t => !agent.Tools.ToolList.Contains(t.Name))
                .ToList(),
            _ => allTools.ToList()
        };
    }

    private string RenderSystemPrompt(Agent agent)
    {
        var prompt = agent.SystemPrompt;

        // Apply variable substitution
        foreach (var (key, value) in agent.PromptVariables)
        {
            prompt = prompt.Replace($"{{{{{key}}}}}", value);
        }

        // Add default prefix if enabled
        if (agent.UseDefaultSystemPrefix)
        {
            prompt = DefaultSystemPrefix + "\n\n" + prompt;
        }

        return prompt;
    }

    private const string DefaultSystemPrefix = """
        You are an AI assistant powered by a large language model.
        Current time: {{current_time}}
        Working directory: {{working_directory}}
        """;
}
```

---

## Configuration

### appsettings.json

```json
{
  "Agents": {
    "DefaultAgentSlug": "coder",
    "GlobalPromptVariables": {
      "project_name": "MyProject",
      "project_type": "dotnet"
    },
    "Custom": [
      {
        "Name": "Custom Coder",
        "Slug": "custom-coder",
        "Description": "Project-specific coding assistant",
        "Category": "Primary",
        "ProviderId": "openai",
        "ModelId": "gpt-4o",
        "SystemPrompt": "You are a .NET specialist...",
        "CanSpawnSubagents": true,
        "MaxIterations": 40,
        "Tools": {
          "Mode": "AllExcept",
          "ToolList": []
        },
        "IconEmoji": "🔧",
        "Color": "#8B5CF6"
      }
    ]
  }
}
```

---

## Database Schema

```sql
-- Agents table (for custom agents)
CREATE TABLE IF NOT EXISTS Agents (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Slug TEXT NOT NULL UNIQUE,
    Description TEXT,
    Category TEXT NOT NULL DEFAULT 'Primary',
    ProviderId TEXT NOT NULL,
    ModelId TEXT NOT NULL,
    Temperature REAL DEFAULT 0.7,
    MaxTokens INTEGER,
    SystemPrompt TEXT NOT NULL,
    PromptVariablesJson TEXT,
    UseDefaultSystemPrefix INTEGER DEFAULT 1,
    ExecutionMode TEXT DEFAULT 'Agentic',
    MaxIterations INTEGER DEFAULT 30,
    TimeoutSeconds INTEGER,
    CanSpawnSubagents INTEGER DEFAULT 0,
    AllowedSubagentTypesJson TEXT,
    ToolsConfigJson TEXT,
    PermissionsJson TEXT,
    IconEmoji TEXT,
    Color TEXT,
    DisplayOrder INTEGER DEFAULT 0,
    IsVisible INTEGER DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT
);

CREATE INDEX IF NOT EXISTS idx_agents_slug ON Agents(Slug);
CREATE INDEX IF NOT EXISTS idx_agents_category ON Agents(Category);
```

---

## Migration Path

1. Add new Agent model alongside existing AgentProfile
2. Create AgentRegistry service
3. Migrate existing agent configs to new model
4. Update ChatService to use AgentExecutionService
5. Update UI to show agent categories and capabilities
6. Deprecate AgentProfile

---

## Testing

```csharp
[Fact]
public void AgentRegistry_LoadsBuiltInAgents()
{
    var registry = new AgentRegistry(Mock.Of<IAgentRepository>(), NullLogger<AgentRegistry>.Instance);

    var coder = registry.GetBySlug("coder");

    Assert.NotNull(coder);
    Assert.Equal(AgentCategory.Primary, coder.Category);
    Assert.True(coder.CanSpawnSubagents);
}

[Fact]
public void CanSpawnSubagent_RespectsAllowedTypes()
{
    var registry = new AgentRegistry(Mock.Of<IAgentRepository>(), NullLogger<AgentRegistry>.Instance);
    var coder = registry.GetBySlug("coder")!;

    Assert.True(registry.CanSpawnSubagent(coder, "explore"));
    Assert.True(registry.CanSpawnSubagent(coder, "general"));

    var reviewer = registry.GetBySlug("reviewer")!;
    Assert.False(registry.CanSpawnSubagent(reviewer, "general"));
}

[Fact]
public void BuildEffectiveToolList_RespectsFilterMode()
{
    // Test OnlyThese, AllExcept, None modes
}
```
