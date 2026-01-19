using System.Text;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Chat;
using OpenFork.Core.Config;
using OpenFork.Core.Constants;
using OpenFork.Core.Domain;
using OpenFork.Core.Domain.Parts;
using OpenFork.Core.Events;

namespace OpenFork.Core.Services;

/// <summary>
/// Layer 3: Compacts conversation by summarizing older messages using LLM.
/// Creates compaction boundary markers for efficient message loading.
/// </summary>
public class CompactionService : ICompactionService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IMessagePartRepository _partRepository;
    private readonly IProviderResolver _providerResolver;
    private readonly AppSettings _settings;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly IEventBus _eventBus;
    private readonly ILogger<CompactionService> _logger;

    private const string CompactionSystemPrompt = """
        You are a conversation summarizer. Your task is to create a concise summary of the conversation history.

        Focus on:
        1. Key decisions made
        2. Important context and requirements discovered
        3. Files that were modified and why
        4. Current state of the task
        5. Any pending items or blockers

        Format the summary as:
        ## Conversation Summary

        ### Context
        [Brief context of what was being worked on]

        ### Key Decisions
        - [Decision 1]
        - [Decision 2]

        ### Changes Made
        - [File/change 1]
        - [File/change 2]

        ### Current State
        [Where things stand now]

        ### Pending Items
        - [If any]

        Keep the summary under 2000 tokens. Be concise but preserve critical context.
        """;

    public CompactionService(
        IMessageRepository messageRepository,
        IMessagePartRepository partRepository,
        IProviderResolver providerResolver,
        AppSettings settings,
        ITokenEstimator tokenEstimator,
        IEventBus eventBus,
        ILogger<CompactionService> logger)
    {
        _messageRepository = messageRepository;
        _partRepository = partRepository;
        _providerResolver = providerResolver;
        _settings = settings;
        _tokenEstimator = tokenEstimator;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsCompactionNeeded(int currentTokens, int contextLimit)
    {
        var threshold = (int)(contextLimit * TokenConstants.CompactionThreshold);
        return currentTokens >= threshold;
    }

    /// <inheritdoc />
    public async Task<CompactionResult> CompactConversationAsync(
        long sessionId,
        IReadOnlyList<Message> messages,
        int currentTokens,
        int contextLimit,
        CancellationToken ct = default)
    {
        // Check if compaction is needed
        if (!IsCompactionNeeded(currentTokens, contextLimit))
        {
            return new CompactionResult
            {
                WasCompacted = false,
                TokensBefore = currentTokens,
                TokensAfter = currentTokens
            };
        }

        _logger.LogInformation(
            "Starting conversation compaction: {Current:N0} tokens (threshold: {Threshold:N0})",
            currentTokens,
            (int)(contextLimit * TokenConstants.CompactionThreshold));

        // Calculate target size (50% of context)
        var targetTokens = (int)(contextLimit * TokenConstants.CompactionTargetPercent / 100.0);
        var tokensToRemove = currentTokens - targetTokens;

        // Find messages to compact (older portion of conversation)
        var messagesToCompact = new List<Message>();
        var accumulatedTokens = 0;

        // Skip system messages and find user/assistant messages to compact
        foreach (var message in messages.Where(m => m.Role != "system" && !m.IsCompacted))
        {
            var messageTokens = _tokenEstimator.EstimateMessageTokens(message);
            if (accumulatedTokens + messageTokens > tokensToRemove)
                break;

            messagesToCompact.Add(message);
            accumulatedTokens += messageTokens;
        }

        if (messagesToCompact.Count < 2)
        {
            _logger.LogWarning("Not enough messages to compact ({Count} found)", messagesToCompact.Count);
            return new CompactionResult
            {
                WasCompacted = false,
                TokensBefore = currentTokens,
                TokensAfter = currentTokens
            };
        }

        // Generate summary using LLM
        var summary = await GenerateSummaryAsync(messagesToCompact, ct);

        // Find the first non-compacted message (boundary)
        var boundaryMessage = messages
            .Where(m => !m.IsCompacted && !messagesToCompact.Contains(m))
            .FirstOrDefault();

        // Create compaction marker part
        var compactionPart = new CompactionPart
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            MessageId = boundaryMessage?.Id ?? 0,
            OrderIndex = 0,
            Summary = summary,
            CompactedMessageCount = messagesToCompact.Count,
            CompactedTokenCount = accumulatedTokens,
            CreatedAt = DateTimeOffset.UtcNow,
            CompactedAt = DateTimeOffset.UtcNow
        };

        await _partRepository.CreateAsync(compactionPart, ct);

        // Mark old messages as compacted
        foreach (var message in messagesToCompact)
        {
            message.IsCompacted = true;
            await _messageRepository.UpdateAsync(message, ct);
        }

        var summaryTokens = _tokenEstimator.EstimateTokens(summary);
        var tokensAfter = currentTokens - accumulatedTokens + summaryTokens;

        _logger.LogInformation(
            "Compaction complete: {Before:N0} → {After:N0} tokens ({Count} messages compacted)",
            currentTokens, tokensAfter, messagesToCompact.Count);

        // Publish event
        await _eventBus.PublishAsync(new MessageCompactedEvent
        {
            MessageId = boundaryMessage?.Id ?? 0,
            SessionId = sessionId,
            Summary = summary,
            MessagesCompacted = messagesToCompact.Count,
            TokensRemoved = accumulatedTokens - summaryTokens
        });

        return new CompactionResult
        {
            WasCompacted = true,
            TokensBefore = currentTokens,
            TokensAfter = tokensAfter,
            MessagesCompacted = messagesToCompact.Count,
            Summary = summary,
            CompactionPartId = compactionPart.Id
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> LoadMessagesWithCompactionBoundaryAsync(
        long sessionId,
        CancellationToken ct = default)
    {
        // Find most recent compaction marker
        var compactionPart = await _partRepository.GetMostRecentCompactionAsync(sessionId, ct);

        if (compactionPart == null)
        {
            // No compaction, load all active messages
            return await _messageRepository.ListActiveBySessionAsync(sessionId, ct);
        }

        // Build result with synthetic summary message + recent messages
        var messages = new List<Message>();

        // Add synthetic message with compaction summary
        messages.Add(new Message
        {
            Id = 0,  // Synthetic message
            SessionId = sessionId,
            Role = "system",
            Content = BuildCompactionSummaryContent(compactionPart),
            CreatedAt = compactionPart.CreatedAt
        });

        // Load messages after the compaction boundary
        if (compactionPart.MessageId > 0)
        {
            var recentMessages = await _messageRepository.ListAfterAsync(
                sessionId,
                compactionPart.MessageId,
                ct);
            messages.AddRange(recentMessages);
        }

        return messages;
    }

    private async Task<string> GenerateSummaryAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct)
    {
        // Build conversation text for summarization
        var conversationText = new StringBuilder();
        foreach (var message in messages)
        {
            conversationText.AppendLine($"[{message.Role.ToUpperInvariant()}]");
            conversationText.AppendLine(message.Content);
            conversationText.AppendLine();
        }

        var request = new ChatCompletionRequest
        {
            Model = _settings.DefaultModel ?? "gpt-4o-mini",
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = CompactionSystemPrompt },
                new() { Role = "user", Content = $"Summarize this conversation:\n\n{conversationText}" }
            }
        };

        try
        {
            var providerKey = _settings.DefaultProviderKey ?? "openai";
            var provider = _providerResolver.Resolve(providerKey);
            var response = await provider.ChatAsync(request, ct);
            var content = response?.Choices.FirstOrDefault()?.Message.Content;
            return content ?? "[Compaction summary generation failed]";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate compaction summary");
            return $"[Compaction summary generation failed: {ex.Message}]";
        }
    }

    private static string BuildCompactionSummaryContent(CompactionPart compaction)
    {
        return $"""
            [Previous conversation compacted]

            {compaction.Summary}

            [End of compacted summary - {compaction.CompactedMessageCount} messages, {compaction.CompactedTokenCount:N0} tokens]
            """;
    }
}
