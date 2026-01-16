using Microsoft.Extensions.Logging;
using OpenFork.Core.Services;

namespace OpenFork.Search.Services;

public class SemanticHistoryProvider : IHistoryProvider
{
    private readonly HistoryService _history;
    private readonly HistoryCompactService _compact;
    private readonly ILogger<SemanticHistoryProvider> _logger;

    public SemanticHistoryProvider(HistoryService history, HistoryCompactService compact, ILogger<SemanticHistoryProvider> logger)
    {
        _history = history;
        _compact = compact;
        _logger = logger;
    }

    public async Task StoreMessageAsync(long sessionId, long messageId, string role, string content, DateTimeOffset createdAt, CancellationToken ct = default)
    {
        _logger.LogInformation("StoreMessageAsync: Storing message {MessageId} with role '{Role}' for session {SessionId}",
            messageId, role, sessionId);
        await _history.StoreMessageAsync(sessionId, messageId, role, content, createdAt, ct);
    }

    public async Task<List<HistoryEntry>> GetContextAsync(long sessionId, string currentQuery, int maxTokens, string providerKey, string model, CancellationToken ct = default)
    {
        _logger.LogInformation("GetContextAsync: Getting context for session {SessionId}, maxTokens={MaxTokens}",
            sessionId, maxTokens);
        
        var messages = await _compact.GetContextAsync(sessionId, currentQuery, maxTokens, providerKey, model, ct);

        _logger.LogInformation("GetContextAsync: Returning {Count} messages for session {SessionId}. Roles: [{Roles}]",
            messages.Count, sessionId, string.Join(", ", messages.Select(m => $"{m.Role}:{m.MessageId}")));

        return messages.Select(m => new HistoryEntry
        {
            MessageId = m.MessageId,
            Role = m.Role,
            Content = m.Content,
            CreatedAt = m.CreatedAt
        }).ToList();
    }
}
