using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Storage.Repositories;

public class PipelineRepository : IPipelineRepository
{
    private readonly SqliteConnectionFactory _factory;

    public PipelineRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<Pipeline>> ListAsync()
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var result = await connection.QueryAsync<Pipeline>("select * from pipelines order by name asc");
        return result.ToList();
    }

    public async Task<Pipeline?> GetAsync(long id)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Pipeline>("select * from pipelines where id=@Id", new { Id = id });
    }

    public async Task<Pipeline?> GetByNameAsync(string name)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Pipeline>("select * from pipelines where name=@Name", new { Name = name });
    }

    public async Task<Pipeline> UpsertAsync(Pipeline pipeline)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();

        if (pipeline.Id == 0)
        {
            pipeline.CreatedAt = pipeline.CreatedAt == default ? DateTimeOffset.UtcNow : pipeline.CreatedAt;
            pipeline.UpdatedAt = DateTimeOffset.UtcNow;
            var id = await connection.ExecuteScalarAsync<long>(
                "insert into pipelines (name, description, created_at, updated_at) values (@Name, @Description, @CreatedAt, @UpdatedAt); select last_insert_rowid();",
                pipeline);
            pipeline.Id = id;
            return pipeline;
        }

        pipeline.UpdatedAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            "update pipelines set name=@Name, description=@Description, updated_at=@UpdatedAt where id=@Id",
            pipeline);
        return pipeline;
    }

    public async Task<List<PipelineStep>> ListStepsAsync(long pipelineId)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var result = await connection.QueryAsync<PipelineStep>(
            "select * from pipeline_steps where pipeline_id=@PipelineId order by order_index asc",
            new { PipelineId = pipelineId });
        return result.ToList();
    }

    public async Task UpsertStepsAsync(long pipelineId, List<PipelineStep> steps)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("delete from pipeline_steps where pipeline_id=@PipelineId", new { PipelineId = pipelineId }, transaction);
        foreach (var step in steps)
        {
            step.PipelineId = pipelineId;
            await connection.ExecuteAsync(
                "insert into pipeline_steps (pipeline_id, order_index, agent_id, handoff_mode) values (@PipelineId, @OrderIndex, @AgentId, @HandoffMode)",
                step, transaction);
        }
        transaction.Commit();
    }

    public async Task DeleteAsync(long id)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("delete from pipeline_steps where pipeline_id=@Id", new { Id = id }, transaction);
        await connection.ExecuteAsync("delete from pipelines where id=@Id", new { Id = id }, transaction);
        transaction.Commit();
    }
}
