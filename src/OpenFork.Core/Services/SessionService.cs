using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

public class SessionService
{
    private readonly ISessionRepository _sessions;

    public SessionService(ISessionRepository sessions)
    {
        _sessions = sessions;
    }

    public Task<List<Session>> ListByProjectAsync(long projectId) => _sessions.ListByProjectAsync(projectId);

    public Task<Session?> GetAsync(long id) => _sessions.GetAsync(id);

    public Task<Session> UpsertAsync(Session session) => _sessions.UpsertAsync(session);
}
