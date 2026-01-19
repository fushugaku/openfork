using System.Text.Json;
using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;
using OpenFork.Core.Permissions;

namespace OpenFork.Storage.Repositories;

/// <summary>
/// Repository for subsession storage.
/// </summary>
public class SubSessionRepository : ISubSessionRepository
{
    private readonly SqliteConnectionFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SubSessionRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<SubSession> CreateAsync(SubSession subSession, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        if (subSession.Id == Guid.Empty)
            subSession.Id = Guid.NewGuid();

        if (subSession.CreatedAt == default)
            subSession.CreatedAt = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(
            """
            INSERT INTO subsessions (
                id, parent_session_id, parent_message_id, agent_slug, status,
                prompt, description, result, error, max_iterations, iterations_used,
                effective_permissions_json, created_at, completed_at
            ) VALUES (
                @Id, @ParentSessionId, @ParentMessageId, @AgentSlug, @Status,
                @Prompt, @Description, @Result, @Error, @MaxIterations, @IterationsUsed,
                @EffectivePermissionsJson, @CreatedAt, @CompletedAt
            )
            """,
            MapToRow(subSession));

        return subSession;
    }

    public async Task<SubSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync<SubSessionRow>(
            "SELECT * FROM subsessions WHERE id = @Id",
            new { Id = id.ToString() });

        return row != null ? DeserializeSubSession(row) : null;
    }

    public async Task UpdateAsync(SubSession subSession, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(
            """
            UPDATE subsessions SET
                status = @Status, result = @Result, error = @Error,
                iterations_used = @IterationsUsed, completed_at = @CompletedAt
            WHERE id = @Id
            """,
            new
            {
                Id = subSession.Id.ToString(),
                Status = subSession.Status.ToString(),
                subSession.Result,
                subSession.Error,
                subSession.IterationsUsed,
                CompletedAt = subSession.CompletedAt?.ToString("O")
            });
    }

    public async Task<IReadOnlyList<SubSession>> GetByParentSessionIdAsync(
        long parentSessionId,
        CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<SubSessionRow>(
            "SELECT * FROM subsessions WHERE parent_session_id = @ParentSessionId ORDER BY created_at ASC",
            new { ParentSessionId = parentSessionId });

        return rows.Select(DeserializeSubSession).ToList();
    }

    public async Task<IReadOnlyList<SubSession>> GetByStatusAsync(
        SubSessionStatus status,
        CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<SubSessionRow>(
            "SELECT * FROM subsessions WHERE status = @Status ORDER BY created_at ASC",
            new { Status = status.ToString() });

        return rows.Select(DeserializeSubSession).ToList();
    }

    public async Task<IReadOnlyList<SubSession>> GetActiveByParentSessionIdAsync(
        long parentSessionId,
        CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<SubSessionRow>(
            """
            SELECT * FROM subsessions
            WHERE parent_session_id = @ParentSessionId
              AND status IN ('Pending', 'Running')
            ORDER BY created_at ASC
            """,
            new { ParentSessionId = parentSessionId });

        return rows.Select(DeserializeSubSession).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(
            "DELETE FROM subsessions WHERE id = @Id",
            new { Id = id.ToString() });
    }

    private static SubSession DeserializeSubSession(SubSessionRow row)
    {
        return new SubSession
        {
            Id = Guid.Parse(row.Id),
            ParentSessionId = row.ParentSessionId,
            ParentMessageId = row.ParentMessageId,
            AgentSlug = row.AgentSlug,
            Status = Enum.Parse<SubSessionStatus>(row.Status),
            Prompt = row.Prompt,
            Description = row.Description,
            Result = row.Result,
            Error = row.Error,
            MaxIterations = row.MaxIterations,
            IterationsUsed = row.IterationsUsed,
            EffectivePermissions = string.IsNullOrEmpty(row.EffectivePermissionsJson)
                ? new PermissionRuleset()
                : JsonSerializer.Deserialize<PermissionRuleset>(row.EffectivePermissionsJson, JsonOptions)
                  ?? new PermissionRuleset(),
            CreatedAt = DateTimeOffset.Parse(row.CreatedAt),
            CompletedAt = string.IsNullOrEmpty(row.CompletedAt) ? null : DateTimeOffset.Parse(row.CompletedAt)
        };
    }

    private static object MapToRow(SubSession subSession)
    {
        return new
        {
            Id = subSession.Id.ToString(),
            subSession.ParentSessionId,
            subSession.ParentMessageId,
            subSession.AgentSlug,
            Status = subSession.Status.ToString(),
            subSession.Prompt,
            subSession.Description,
            subSession.Result,
            subSession.Error,
            subSession.MaxIterations,
            subSession.IterationsUsed,
            EffectivePermissionsJson = JsonSerializer.Serialize(subSession.EffectivePermissions, JsonOptions),
            CreatedAt = subSession.CreatedAt.ToString("O"),
            CompletedAt = subSession.CompletedAt?.ToString("O")
        };
    }

    private class SubSessionRow
    {
        public string Id { get; set; } = string.Empty;
        public long ParentSessionId { get; set; }
        public long? ParentMessageId { get; set; }
        public string AgentSlug { get; set; } = "general";
        public string Status { get; set; } = "Pending";
        public string? Prompt { get; set; }
        public string? Description { get; set; }
        public string? Result { get; set; }
        public string? Error { get; set; }
        public int MaxIterations { get; set; }
        public int IterationsUsed { get; set; }
        public string? EffectivePermissionsJson { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? CompletedAt { get; set; }
    }
}
