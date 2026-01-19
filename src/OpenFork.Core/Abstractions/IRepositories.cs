using OpenFork.Core.Domain;
using OpenFork.Core.Domain.Parts;

namespace OpenFork.Core.Abstractions;

public interface IProjectRepository
{
    Task<List<Project>> ListAsync();
    Task<Project?> GetAsync(long id);
    Task<Project> UpsertAsync(Project project);
}

public interface ISessionRepository
{
    Task<List<Session>> ListByProjectAsync(long projectId);
    Task<Session?> GetAsync(long id);
    Task<Session> UpsertAsync(Session session);
}

public interface IMessageRepository
{
    Task<List<Message>> ListBySessionAsync(long sessionId);
    Task<Message> AddAsync(Message message);

    /// <summary>Get messages that are not compacted.</summary>
    Task<List<Message>> ListActiveBySessionAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Get messages after a specific message ID.</summary>
    Task<List<Message>> ListAfterAsync(long sessionId, long afterMessageId, CancellationToken ct = default);

    /// <summary>Update a message.</summary>
    Task UpdateAsync(Message message, CancellationToken ct = default);
}

public interface IAppStateRepository
{
    Task<long?> GetLongAsync(string key);
    Task SetLongAsync(string key, long value);
}

public interface IAgentRepository
{
    Task<List<AgentProfile>> ListAsync();
    Task<AgentProfile?> GetAsync(long id);
    Task<AgentProfile?> GetByNameAsync(string name);
    Task<AgentProfile> UpsertAsync(AgentProfile profile);
    Task DeleteAsync(long id);
}

/// <summary>
/// Repository for custom Agent entities (new architecture with Guid IDs).
/// Used by AgentRegistry for persisting custom agent configurations.
/// </summary>
public interface ICustomAgentRepository
{
    /// <summary>Get all custom agents.</summary>
    Task<IReadOnlyList<Agent>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get a custom agent by ID.</summary>
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get a custom agent by slug.</summary>
    Task<Agent?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Create a new custom agent.</summary>
    Task<Agent> CreateAsync(Agent agent, CancellationToken ct = default);

    /// <summary>Update an existing custom agent.</summary>
    Task UpdateAsync(Agent agent, CancellationToken ct = default);

    /// <summary>Delete a custom agent.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IPipelineRepository
{
    Task<List<Pipeline>> ListAsync();
    Task<Pipeline?> GetAsync(long id);
    Task<Pipeline?> GetByNameAsync(string name);
    Task<Pipeline> UpsertAsync(Pipeline pipeline);
    Task<List<PipelineStep>> ListStepsAsync(long pipelineId);
    Task UpsertStepsAsync(long pipelineId, List<PipelineStep> steps);
    Task DeleteAsync(long id);
}

/// <summary>
/// Repository for message parts with polymorphic storage.
/// Parts use Guid IDs internally but reference Messages/Sessions via long IDs.
/// </summary>
public interface IMessagePartRepository
{
    /// <summary>Create a new message part.</summary>
    Task<MessagePart> CreateAsync(MessagePart part, CancellationToken ct = default);

    /// <summary>Get a message part by ID.</summary>
    Task<MessagePart?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Update an existing message part.</summary>
    Task UpdateAsync(MessagePart part, CancellationToken ct = default);

    /// <summary>Delete a message part.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Get all parts for a message, ordered by index.</summary>
    Task<IReadOnlyList<MessagePart>> GetByMessageIdAsync(long messageId, CancellationToken ct = default);

    /// <summary>Get all parts for a session.</summary>
    Task<IReadOnlyList<MessagePart>> GetBySessionIdAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Get tool parts by status.</summary>
    Task<IReadOnlyList<ToolPart>> GetToolPartsByStatusAsync(long sessionId, ToolPartStatus status, CancellationToken ct = default);

    /// <summary>Get the most recent compaction part for a session.</summary>
    Task<CompactionPart?> GetMostRecentCompactionAsync(long sessionId, CancellationToken ct = default);

    /// <summary>Get a typed part by ID.</summary>
    Task<T?> GetTypedPartAsync<T>(Guid id, CancellationToken ct = default) where T : MessagePart;
}

/// <summary>
/// Repository for SubSession entities (subagent executions).
/// </summary>
public interface ISubSessionRepository
{
    /// <summary>Create a new subsession.</summary>
    Task<SubSession> CreateAsync(SubSession subSession, CancellationToken ct = default);

    /// <summary>Get a subsession by ID.</summary>
    Task<SubSession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Update an existing subsession.</summary>
    Task UpdateAsync(SubSession subSession, CancellationToken ct = default);

    /// <summary>Get all subsessions for a parent session.</summary>
    Task<IReadOnlyList<SubSession>> GetByParentSessionIdAsync(long parentSessionId, CancellationToken ct = default);

    /// <summary>Get subsessions by status.</summary>
    Task<IReadOnlyList<SubSession>> GetByStatusAsync(SubSessionStatus status, CancellationToken ct = default);

    /// <summary>Get pending or running subsessions for a parent session.</summary>
    Task<IReadOnlyList<SubSession>> GetActiveByParentSessionIdAsync(long parentSessionId, CancellationToken ct = default);

    /// <summary>Delete a subsession.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
