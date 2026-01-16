using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Storage.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly SqliteConnectionFactory _factory;

    public MessageRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<Message>> ListBySessionAsync(long sessionId)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var result = await connection.QueryAsync<Message>(
            "select * from messages where session_id = @SessionId order by id asc",
            new { SessionId = sessionId });
        return result.ToList();
    }

    public async Task<Message> AddAsync(Message message)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        message.CreatedAt = message.CreatedAt == default ? DateTimeOffset.UtcNow : message.CreatedAt;
        var id = await connection.ExecuteScalarAsync<long>(
            "insert into messages (session_id, agent_id, pipeline_step_id, role, content, tool_calls_json, created_at) values (@SessionId, @AgentId, @PipelineStepId, @Role, @Content, @ToolCallsJson, @CreatedAt); select last_insert_rowid();",
            message);
        message.Id = id;
        return message;
    }
}
