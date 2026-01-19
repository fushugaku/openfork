using Microsoft.Extensions.Logging;
using OpenFork.Search.Config;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace OpenFork.Search.Services;

public class HistoryService
{
    private readonly SearchConfig _config;
    private readonly EmbeddingService _embedding;
    private readonly ILogger<HistoryService> _logger;
    private QdrantClient? _client;

    private const string CollectionPrefix = "openfork_history_";

    public HistoryService(SearchConfig config, EmbeddingService embedding, ILogger<HistoryService> logger)
    {
        _config = config;
        _embedding = embedding;
        _logger = logger;
    }

    private QdrantClient GetClient()
    {
        return _client ??= new QdrantClient(_config.QdrantHost, _config.QdrantPort);
    }

    public async Task<bool> EnsureCollectionAsync(long sessionId, CancellationToken ct = default)
    {
        try
        {
            var client = GetClient();
            var name = GetCollectionName(sessionId);

            var collections = await client.ListCollectionsAsync(ct);
            if (collections.Any(c => c == name))
                return true;

            await client.CreateCollectionAsync(
                name,
                new VectorParams
                {
                    Size = (ulong)_config.EmbeddingDimension,
                    Distance = Distance.Cosine
                },
                cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Qdrant collection for session {SessionId}. Semantic search disabled.", sessionId);
            return false;
        }
    }

    public async Task StoreMessageAsync(long sessionId, long messageId, string role, string content, DateTimeOffset createdAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        try
        {
            var embedding = await _embedding.GetEmbeddingAsync(content, ct);
            if (embedding == null)
                return;

            if (!await EnsureCollectionAsync(sessionId, ct))
                return;

            var client = GetClient();
            var name = GetCollectionName(sessionId);

            var point = new PointStruct
            {
                Id = new PointId { Num = (ulong)messageId },
                Vectors = embedding,
                Payload =
                {
                    ["message_id"] = messageId,
                    ["role"] = role,
                    ["content"] = content,
                    ["created_at"] = createdAt.ToUnixTimeSeconds(),
                    ["token_estimate"] = EstimateTokens(content),
                    ["is_summary"] = false
                }
            };

            await client.UpsertAsync(name, new[] { point }, cancellationToken: ct);

            _logger.LogInformation("Stored message {MessageId} with role '{Role}' for session {SessionId} in collection {CollectionName}",
                messageId, role, sessionId, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store message {MessageId} in Qdrant for session {SessionId}. Semantic history disabled.", messageId, sessionId);
        }
    }

    public async Task StoreSummaryAsync(long sessionId, long summaryId, string summary, long[] summarizedMessageIds, CancellationToken ct = default)
    {
        try
        {
            var embedding = await _embedding.GetEmbeddingAsync(summary, ct);
            if (embedding == null)
                return;

            if (!await EnsureCollectionAsync(sessionId, ct))
                return;

            var client = GetClient();
            var name = GetCollectionName(sessionId);

            var point = new PointStruct
            {
                Id = new PointId { Num = (ulong)summaryId },
                Vectors = embedding,
                Payload =
                {
                    ["message_id"] = summaryId,
                    ["role"] = "system",
                    ["content"] = summary,
                    ["created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["token_estimate"] = EstimateTokens(summary),
                    ["is_summary"] = true,
                    ["summarized_ids"] = string.Join(",", summarizedMessageIds)
                }
            };

            await client.UpsertAsync(name, new[] { point }, cancellationToken: ct);

            _logger.LogInformation("Deleting {Count} summarized messages for session {SessionId}: [{MessageIds}]",
                summarizedMessageIds.Length, sessionId, string.Join(", ", summarizedMessageIds));
            await DeleteMessagesAsync(sessionId, summarizedMessageIds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store summary in Qdrant for session {SessionId}. Compaction skipped.", sessionId);
        }
    }

    public async Task<List<HistoryMessage>> SearchRelevantAsync(long sessionId, string query, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var embedding = await _embedding.GetEmbeddingAsync(query, ct);
            if (embedding == null)
                return new List<HistoryMessage>();

            var client = GetClient();
            var name = GetCollectionName(sessionId);

            var collections = await client.ListCollectionsAsync(ct);
            if (!collections.Any(c => c == name))
                return new List<HistoryMessage>();

            var results = await client.SearchAsync(
                name,
                embedding,
                limit: (ulong)limit,
                cancellationToken: ct);

            return results.Select(r => new HistoryMessage
            {
                MessageId = (long)r.Payload["message_id"].IntegerValue,
                Role = r.Payload["role"].StringValue,
                Content = r.Payload["content"].StringValue,
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)r.Payload["created_at"].IntegerValue),
                TokenEstimate = (int)r.Payload["token_estimate"].IntegerValue,
                IsSummary = r.Payload["is_summary"].BoolValue,
                Score = r.Score
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search relevant messages in Qdrant for session {SessionId}. Returning empty results.", sessionId);
            return new List<HistoryMessage>();
        }
    }

    public async Task<List<HistoryMessage>> GetRecentAsync(long sessionId, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var client = GetClient();
            var name = GetCollectionName(sessionId);

            var collections = await client.ListCollectionsAsync(ct);
            if (!collections.Any(c => c == name))
            {
                _logger.LogInformation("GetRecentAsync: Collection {CollectionName} does not exist for session {SessionId}",
                    name, sessionId);
                return new List<HistoryMessage>();
            }

            var result = new List<HistoryMessage>();
            PointId? nextOffset = null;

            do
            {
                var scrollResult = await client.ScrollAsync(
                    name,
                    limit: 100,
                    offset: nextOffset,
                    payloadSelector: true,
                    cancellationToken: ct);

                var points = scrollResult.Result.ToList();

                foreach (var point in points)
                {
                    result.Add(new HistoryMessage
                    {
                        MessageId = (long)point.Payload["message_id"].IntegerValue,
                        Role = point.Payload["role"].StringValue,
                        Content = point.Payload["content"].StringValue,
                        CreatedAt = DateTimeOffset.FromUnixTimeSeconds((long)point.Payload["created_at"].IntegerValue),
                        TokenEstimate = (int)point.Payload["token_estimate"].IntegerValue,
                        IsSummary = point.Payload["is_summary"].BoolValue
                    });
                }

                var lastPoint = points.LastOrDefault();
                nextOffset = lastPoint?.Id;

                if (points.Count < 100)
                    break;

            } while (nextOffset != null);

            var finalResult = result
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .OrderBy(m => m.CreatedAt)
                .ToList();

            _logger.LogInformation("GetRecentAsync: Retrieved {Count} messages for session {SessionId}. Roles: [{Roles}]",
                finalResult.Count, sessionId, string.Join(", ", finalResult.Select(m => $"{m.Role}:{m.MessageId}")));

            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get recent messages from Qdrant for session {SessionId}. Returning empty results.", sessionId);
            return new List<HistoryMessage>();
        }
    }

    public async Task<int> GetTotalTokensAsync(long sessionId, CancellationToken ct = default)
    {
        var messages = await GetRecentAsync(sessionId, int.MaxValue, ct);
        return messages.Sum(m => m.TokenEstimate);
    }

    public async Task DeleteMessagesAsync(long sessionId, long[] messageIds, CancellationToken ct = default)
    {
        if (messageIds.Length == 0)
            return;

        try
        {
            var client = GetClient();
            var name = GetCollectionName(sessionId);

            _logger.LogInformation("DeleteMessagesAsync called for session {SessionId}, deleting {Count} messages: [{MessageIds}]",
                sessionId, messageIds.Length, string.Join(", ", messageIds));

            var pointIds = messageIds.Select(id => (ulong)id).ToList();
            await client.DeleteAsync(name, pointIds, cancellationToken: ct);

            _logger.LogInformation("Successfully deleted {Count} messages from Qdrant collection {CollectionName}",
                messageIds.Length, name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete messages from Qdrant for session {SessionId}.", sessionId);
        }
    }

    public async Task DeleteCollectionAsync(long sessionId, CancellationToken ct = default)
    {
        try
        {
            var client = GetClient();
            var name = GetCollectionName(sessionId);

            var collections = await client.ListCollectionsAsync(ct);
            if (collections.Any(c => c == name))
            {
                await client.DeleteCollectionAsync(name, cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete Qdrant collection for session {SessionId}.", sessionId);
        }
    }

    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        return (int)(text.Length / 3.5);
    }

    private static string GetCollectionName(long sessionId) => $"{CollectionPrefix}{sessionId}";
}

public class HistoryMessage
{
    public long MessageId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int TokenEstimate { get; set; }
    public bool IsSummary { get; set; }
    public float Score { get; set; }
}
