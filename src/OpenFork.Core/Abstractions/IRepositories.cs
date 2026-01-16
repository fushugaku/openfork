using OpenFork.Core.Domain;

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
