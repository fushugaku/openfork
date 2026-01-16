using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Storage.Repositories;

public class AgentRepository : IAgentRepository
{
    private readonly SqliteConnectionFactory _factory;

    public AgentRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<AgentProfile>> ListAsync()
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var result = await connection.QueryAsync<AgentProfile>("select id, name, system_prompt, provider_key, model, max_iterations, created_at, updated_at from agents order by name asc");
        return result.ToList();
    }

    public async Task<AgentProfile?> GetAsync(long id)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<AgentProfile>("select id, name, system_prompt, provider_key, model, max_iterations, created_at, updated_at from agents where id=@Id", new { Id = id });
    }

    public async Task<AgentProfile?> GetByNameAsync(string name)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<AgentProfile>("select id, name, system_prompt, provider_key, model, max_iterations, created_at, updated_at from agents where name=@Name", new { Name = name });
    }

    public async Task<AgentProfile> UpsertAsync(AgentProfile profile)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();

        if (profile.Id == 0)
        {
            profile.CreatedAt = profile.CreatedAt == default ? DateTimeOffset.UtcNow : profile.CreatedAt;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            var id = await connection.ExecuteScalarAsync<long>(
                "insert into agents (name, system_prompt, provider_key, model, max_iterations, created_at, updated_at) values (@Name, @SystemPrompt, @ProviderKey, @Model, @MaxIterations, @CreatedAt, @UpdatedAt); select last_insert_rowid();",
                profile);
            profile.Id = id;
            return profile;
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            "update agents set name=@Name, system_prompt=@SystemPrompt, provider_key=@ProviderKey, model=@Model, max_iterations=@MaxIterations, updated_at=@UpdatedAt where id=@Id",
            profile);
        return profile;
    }

    public async Task DeleteAsync(long id)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        await connection.ExecuteAsync("delete from agents where id=@Id", new { Id = id });
        await connection.ExecuteAsync("delete from pipeline_steps where agent_id=@Id", new { Id = id });
    }
}
