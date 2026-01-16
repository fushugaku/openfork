using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Storage.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SessionRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<Session>> ListByProjectAsync(long projectId)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var result = await connection.QueryAsync<Session>(
            "select * from sessions where project_id = @ProjectId order by updated_at desc",
            new { ProjectId = projectId });
        return result.ToList();
    }

    public async Task<Session?> GetAsync(long id)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Session>("select * from sessions where id = @Id", new { Id = id });
    }

    public async Task<Session> UpsertAsync(Session session)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();

        if (session.Id == 0)
        {
            session.CreatedAt = session.CreatedAt == default ? DateTimeOffset.UtcNow : session.CreatedAt;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            var id = await connection.ExecuteScalarAsync<long>(
                "insert into sessions (project_id, name, active_agent_id, active_pipeline_id, created_at, updated_at) values (@ProjectId, @Name, @ActiveAgentId, @ActivePipelineId, @CreatedAt, @UpdatedAt); select last_insert_rowid();",
                session);
            session.Id = id;
            return session;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            "update sessions set name=@Name, active_agent_id=@ActiveAgentId, active_pipeline_id=@ActivePipelineId, updated_at=@UpdatedAt where id=@Id",
            session);
        return session;
    }
}
