using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

public class AgentService
{
    private readonly IAgentRepository _agents;

    public AgentService(IAgentRepository agents)
    {
        _agents = agents;
    }

    public Task<List<AgentProfile>> ListAsync() => _agents.ListAsync();

    public Task<AgentProfile?> GetAsync(long id) => _agents.GetAsync(id);

    public Task<AgentProfile?> GetByNameAsync(string name) => _agents.GetByNameAsync(name);

    public Task<AgentProfile> UpsertAsync(AgentProfile agent) => _agents.UpsertAsync(agent);

    public Task DeleteAsync(long id) => _agents.DeleteAsync(id);
}
