using OpenFork.Core.Domain;
using OpenFork.Core.Permissions;

namespace OpenFork.Core.Agents;

/// <summary>
/// Built-in agents with predefined configurations.
/// </summary>
public static class BuiltInAgents
{
    // ═══════════════════════════════════════════════════════════════
    // PRIMARY AGENTS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Full-featured coding assistant with file editing and command execution.
    /// </summary>
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
        AllowedSubagentTypes = new() { "explore", "general", "planner-sub", "researcher" },
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
            ToolList = new()
        },
        Permissions = BuiltInRulesets.Primary,
        DisplayOrder = 1
    };

    /// <summary>
    /// Creates implementation plans without executing them.
    /// </summary>
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
        Permissions = BuiltInRulesets.Planner,
        DisplayOrder = 2
    };

    /// <summary>
    /// Code review specialist for quality and security analysis.
    /// </summary>
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
        Permissions = BuiltInRulesets.Explorer,
        DisplayOrder = 3
    };

    // ═══════════════════════════════════════════════════════════════
    // SUBAGENT TYPES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Codebase exploration specialist.
    /// </summary>
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
        Permissions = BuiltInRulesets.Explorer,
        DisplayOrder = 10
    };

    /// <summary>
    /// General-purpose assistant for various tasks.
    /// </summary>
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
        Permissions = BuiltInRulesets.Primary,
        DisplayOrder = 11
    };

    /// <summary>
    /// Web research and documentation specialist.
    /// </summary>
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
        Permissions = BuiltInRulesets.Researcher,
        DisplayOrder = 12
    };

    /// <summary>
    /// Creates focused implementation plans.
    /// </summary>
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
        Permissions = BuiltInRulesets.Planner,
        DisplayOrder = 13
    };

    // ═══════════════════════════════════════════════════════════════
    // HIDDEN/INTERNAL AGENTS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Internal agent for conversation summarization.
    /// </summary>
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
        Tools = new ToolConfiguration { Mode = ToolFilterMode.None },
        DisplayOrder = 100
    };

    /// <summary>
    /// Internal agent for generating session summaries.
    /// </summary>
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
        Tools = new ToolConfiguration { Mode = ToolFilterMode.None },
        DisplayOrder = 101
    };

    // ═══════════════════════════════════════════════════════════════
    // COLLECTIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Collection of all built-in agents.
    /// </summary>
    public static readonly IReadOnlyList<Agent> All = new[]
    {
        // Primary
        Coder, Planner, Reviewer,
        // Subagents
        ExploreSubagent, GeneralSubagent, ResearchSubagent, PlannerSubagent,
        // Hidden
        CompactionAgent, SummaryAgent
    };

    /// <summary>
    /// Dictionary of all built-in agents by slug.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Agent> BySlug =
        All.ToDictionary(a => a.Slug);

    /// <summary>
    /// Dictionary of all built-in agents by ID.
    /// </summary>
    public static readonly IReadOnlyDictionary<Guid, Agent> ById =
        All.ToDictionary(a => a.Id);
}
