using System.Text.Json;
using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain.Parts;

namespace OpenFork.Storage.Repositories;

/// <summary>
/// Repository for message parts with polymorphic JSON storage.
/// </summary>
public class MessagePartRepository : IMessagePartRepository
{
    private readonly SqliteConnectionFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public MessagePartRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<MessagePart> CreateAsync(MessagePart part, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        if (part.Id == Guid.Empty)
            part.Id = Guid.NewGuid();

        if (part.CreatedAt == default)
            part.CreatedAt = DateTimeOffset.UtcNow;

        var dataJson = SerializePart(part);

        await connection.ExecuteAsync(
            """
            insert into message_parts (id, session_id, message_id, order_index, type, data_json, created_at, updated_at)
            values (@Id, @SessionId, @MessageId, @OrderIndex, @Type, @DataJson, @CreatedAt, @UpdatedAt)
            """,
            new
            {
                Id = part.Id.ToString(),
                part.SessionId,
                part.MessageId,
                part.OrderIndex,
                part.Type,
                DataJson = dataJson,
                CreatedAt = part.CreatedAt.ToString("O"),
                UpdatedAt = part.UpdatedAt?.ToString("O")
            });

        return part;
    }

    /// <inheritdoc />
    public async Task<MessagePart?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<PartRow>(
            "select * from message_parts where id = @Id",
            new { Id = id.ToString() });

        return row != null ? DeserializePart(row) : null;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(MessagePart part, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        part.UpdatedAt = DateTimeOffset.UtcNow;
        var dataJson = SerializePart(part);

        await connection.ExecuteAsync(
            """
            update message_parts
            set order_index = @OrderIndex, data_json = @DataJson, updated_at = @UpdatedAt
            where id = @Id
            """,
            new
            {
                Id = part.Id.ToString(),
                part.OrderIndex,
                DataJson = dataJson,
                UpdatedAt = part.UpdatedAt?.ToString("O")
            });
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(
            "delete from message_parts where id = @Id",
            new { Id = id.ToString() });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessagePart>> GetByMessageIdAsync(long messageId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<PartRow>(
            "select * from message_parts where message_id = @MessageId order by order_index asc",
            new { MessageId = messageId });

        return rows.Select(DeserializePart).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessagePart>> GetBySessionIdAsync(long sessionId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<PartRow>(
            "select * from message_parts where session_id = @SessionId order by message_id asc, order_index asc",
            new { SessionId = sessionId });

        return rows.Select(DeserializePart).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolPart>> GetToolPartsByStatusAsync(
        long sessionId,
        ToolPartStatus status,
        CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<PartRow>(
            "select * from message_parts where session_id = @SessionId and type = 'tool' order by order_index asc",
            new { SessionId = sessionId });

        return rows
            .Select(DeserializePart)
            .OfType<ToolPart>()
            .Where(t => t.Status == status)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<CompactionPart?> GetMostRecentCompactionAsync(long sessionId, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<PartRow>(
            "select * from message_parts where session_id = @SessionId and type = 'compaction' order by created_at desc limit 1",
            new { SessionId = sessionId });

        return row != null ? DeserializePart(row) as CompactionPart : null;
    }

    /// <inheritdoc />
    public async Task<T?> GetTypedPartAsync<T>(Guid id, CancellationToken ct = default) where T : MessagePart
    {
        var part = await GetByIdAsync(id, ct);
        return part as T;
    }

    private static string SerializePart(MessagePart part)
    {
        return JsonSerializer.Serialize(part, part.GetType(), JsonOptions);
    }

    private static MessagePart DeserializePart(PartRow row)
    {
        var type = row.Type switch
        {
            "text" => typeof(TextPart),
            "tool" => typeof(ToolPart),
            "reasoning" => typeof(ReasoningPart),
            "file" => typeof(FilePart),
            "patch" => typeof(PatchPart),
            "step" => typeof(StepPart),
            "compaction" => typeof(CompactionPart),
            "subtask" => typeof(SubtaskPart),
            "agent" => typeof(AgentPart),
            "retry" => typeof(RetryPart),
            "snapshot" => typeof(SnapshotPart),
            _ => throw new InvalidOperationException($"Unknown part type: {row.Type}")
        };

        var part = (MessagePart)JsonSerializer.Deserialize(row.DataJson, type, JsonOptions)!;

        // Restore common properties from row (in case JSON doesn't have them)
        part.Id = Guid.Parse(row.Id);
        part.SessionId = row.SessionId;
        part.MessageId = row.MessageId;
        part.OrderIndex = row.OrderIndex;
        part.CreatedAt = DateTimeOffset.Parse(row.CreatedAt);
        part.UpdatedAt = string.IsNullOrEmpty(row.UpdatedAt) ? null : DateTimeOffset.Parse(row.UpdatedAt);

        return part;
    }

    private class PartRow
    {
        public string Id { get; set; } = string.Empty;
        public long SessionId { get; set; }
        public long MessageId { get; set; }
        public int OrderIndex { get; set; }
        public string Type { get; set; } = string.Empty;
        public string DataJson { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
        public string? UpdatedAt { get; set; }
    }
}
