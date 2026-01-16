using Microsoft.Extensions.Logging;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Chat;
using OpenFork.Search.Config;

namespace OpenFork.Search.Services;

public class HistoryCompactService
{
    private readonly HistoryService _history;
    private readonly IProviderResolver _providers;
    private readonly SearchConfig _config;
    private readonly ILogger<HistoryCompactService> _logger;

    private const int ReserveTokens = 8000;
    private const int MinMessagesToSummarize = 10;
    private const string SummarizePrompt = @"You are a conversation summarizer. Create a concise summary of the following conversation that preserves:
1. Key decisions and conclusions
2. Important context and requirements
3. Technical details and code snippets that were discussed
4. Any unresolved questions or pending tasks

Keep the summary factual and actionable. Do not include pleasantries or meta-commentary.

Conversation to summarize:
";

    public HistoryCompactService(
        HistoryService history,
        IProviderResolver providers,
        SearchConfig config,
        ILogger<HistoryCompactService> logger)
    {
        _history = history;
        _providers = providers;
        _config = config;
        _logger = logger;
    }

    public async Task<List<HistoryMessage>> GetContextAsync(
        long sessionId,
        string currentQuery,
        int maxTokens,
        string providerKey,
        string model,
        CancellationToken ct = default)
    {
        var availableTokens = maxTokens - ReserveTokens;
        if (availableTokens <= 0)
            availableTokens = maxTokens / 2;

        _logger.LogInformation("GetContextAsync: session={SessionId}, maxTokens={MaxTokens}, availableTokens={AvailableTokens}",
            sessionId, maxTokens, availableTokens);

        var recentMessages = await _history.GetRecentAsync(sessionId, 100, ct);
        var totalTokens = recentMessages.Sum(m => m.TokenEstimate);

        _logger.LogInformation("GetContextAsync: Retrieved {Count} recent messages, totalTokens={TotalTokens}",
            recentMessages.Count, totalTokens);

        if (totalTokens <= availableTokens)
        {
            _logger.LogInformation("GetContextAsync: Tokens within limit, returning all {Count} recent messages",
                recentMessages.Count);
            return recentMessages;
        }

        _logger.LogInformation("GetContextAsync: Token limit exceeded, searching for relevant messages");
        var relevantMessages = await _history.SearchRelevantAsync(sessionId, currentQuery, 30, ct);

        var combined = new Dictionary<long, HistoryMessage>();
        
        var recentCount = Math.Min(10, recentMessages.Count);
        foreach (var msg in recentMessages.TakeLast(recentCount))
        {
            combined[msg.MessageId] = msg;
        }

        foreach (var msg in relevantMessages)
        {
            if (!combined.ContainsKey(msg.MessageId))
            {
                combined[msg.MessageId] = msg;
            }
        }

        var result = combined.Values
            .OrderBy(m => m.CreatedAt)
            .ToList();

        var currentTokens = result.Sum(m => m.TokenEstimate);

        _logger.LogInformation("GetContextAsync: Combined {Count} messages, currentTokens={CurrentTokens}, threshold={Threshold}",
            result.Count, currentTokens, availableTokens);

        if (currentTokens > availableTokens && result.Count > MinMessagesToSummarize)
        {
            _logger.LogInformation("GetContextAsync: Triggering compaction for session {SessionId}", sessionId);
            await CompactOldMessagesAsync(sessionId, result, availableTokens, providerKey, model, ct);
            return await _history.GetRecentAsync(sessionId, 100, ct);
        }

        while (currentTokens > availableTokens && result.Count > recentCount)
        {
            var removedMsg = result[0];
            _logger.LogInformation("GetContextAsync: Removing message {MessageId} (role={Role}) to reduce token count",
                removedMsg.MessageId, removedMsg.Role);
            result.RemoveAt(0);
            currentTokens = result.Sum(m => m.TokenEstimate);
        }

        _logger.LogInformation("GetContextAsync: Returning {Count} messages with {Tokens} tokens",
            result.Count, currentTokens);

        return result;
    }

    public async Task CompactOldMessagesAsync(
        long sessionId,
        List<HistoryMessage> messages,
        int targetTokens,
        string providerKey,
        string model,
        CancellationToken ct = default)
    {
        if (messages.Count < MinMessagesToSummarize)
            return;

        var toSummarize = new List<HistoryMessage>();
        var currentTokens = messages.Sum(m => m.TokenEstimate);

        foreach (var msg in messages)
        {
            if (msg.IsSummary)
                continue;

            toSummarize.Add(msg);
            currentTokens -= msg.TokenEstimate;

            if (currentTokens <= targetTokens)
                break;
        }

        if (toSummarize.Count < MinMessagesToSummarize)
            return;

        var conversationText = string.Join("\n\n", toSummarize.Select(m => $"[{m.Role}]: {m.Content}"));

        var summary = await GenerateSummaryAsync(conversationText, providerKey, model, ct);
        if (string.IsNullOrEmpty(summary))
            return;

        var summaryId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var summarizedIds = toSummarize.Select(m => m.MessageId).ToArray();

        _logger.LogInformation("Compacting {Count} messages into summary for session {SessionId}. Messages to delete: [{MessageIds}]",
            toSummarize.Count, sessionId, string.Join(", ", summarizedIds));

        await _history.StoreSummaryAsync(sessionId, summaryId, summary, summarizedIds, ct);

        _logger.LogInformation("Compacted {Count} messages into summary for session {SessionId}",
            toSummarize.Count, sessionId);
    }

    private async Task<string?> GenerateSummaryAsync(string conversation, string providerKey, string model, CancellationToken ct)
    {
        var provider = _providers.Resolve(providerKey);

        var request = new ChatCompletionRequest
        {
            Model = model,
            Messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = SummarizePrompt + conversation }
            },
            Stream = false
        };

        var response = await provider.ChatAsync(request, ct);
        return response?.Choices?.FirstOrDefault()?.Message?.Content;
    }
}
