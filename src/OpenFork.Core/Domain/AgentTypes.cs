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

/// <summary>
/// Mode for tool filtering.
/// </summary>
public enum ToolFilterMode
{
    /// <summary>All tools available.</summary>
    All,

    /// <summary>All except specified tools.</summary>
    AllExcept,

    /// <summary>Only specified tools.</summary>
    OnlyThese,

    /// <summary>No tools.</summary>
    None
}
