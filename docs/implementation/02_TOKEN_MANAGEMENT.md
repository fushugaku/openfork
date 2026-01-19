# Token Management Implementation Guide

## Overview

Token management is critical for maintaining effective LLM conversations within context window limits. This guide details implementing the 3-layer token management strategy from the opencode reference.

---

## Architecture Analysis

### Current State (OpenFork)

```
┌─────────────────────────────────────────┐
│            ChatService                   │
│  ┌───────────────────────────────────┐  │
│  │     Simple Token Estimation       │  │
│  │     (4 chars ≈ 1 token)          │  │
│  └───────────────────────────────────┘  │
│                   │                      │
│                   ▼                      │
│  ┌───────────────────────────────────┐  │
│  │   Compact at 85% capacity         │  │
│  │   • Prune old tool outputs        │  │
│  │   • LLM summarization             │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────┘
```

**Limitations**:
- Single compaction threshold (85%)
- No output truncation per tool type
- No disk spillover for large outputs
- Reactive only (waits until overflow)

### Target State (opencode-aligned)

```
┌─────────────────────────────────────────────────────────────────┐
│                    3-Layer Token Management                      │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐     │
│  │ Layer 1: TOOL OUTPUT TRUNCATION (Immediate)            │     │
│  │ ├── 2000 lines max per output                          │     │
│  │ ├── 50KB max size per output                           │     │
│  │ ├── Disk spillover for overflow                        │     │
│  │ └── Per-tool truncation rules                          │     │
│  └────────────────────────────────────────────────────────┘     │
│                              │                                   │
│                              ▼                                   │
│  ┌────────────────────────────────────────────────────────┐     │
│  │ Layer 2: TOOL OUTPUT PRUNING (When approaching limit)  │     │
│  │ ├── Protect first 40K tokens (PRUNE_PROTECT)           │     │
│  │ ├── Min 20K token reduction (PRUNE_MINIMUM)            │     │
│  │ ├── Progressive old output removal                      │     │
│  │ └── Keep tool invocation metadata                       │     │
│  └────────────────────────────────────────────────────────┘     │
│                              │                                   │
│                              ▼                                   │
│  ┌────────────────────────────────────────────────────────┐     │
│  │ Layer 3: CONVERSATION COMPACTION (Overflow imminent)   │     │
│  │ ├── LLM summarizes older messages                       │     │
│  │ ├── Preserves key decisions/context                     │     │
│  │ ├── Inserts compaction boundary marker                  │     │
│  │ └── Stops loading messages before marker                │     │
│  └────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────┘
```

---

## Key Constants

```csharp
namespace OpenFork.Core.Constants;

public static class TokenConstants
{
    // Estimation
    public const double CharsPerToken = 4.0;

    // Context limits (configurable per model)
    public const int DefaultContextWindow = 128_000;
    public const int DefaultMaxOutputTokens = 16_384;

    // Layer 1: Truncation
    public const int MaxOutputLines = 2_000;
    public const int MaxOutputBytes = 50 * 1024;  // 50KB
    public const int MaxLineLength = 2_000;

    // Layer 2: Pruning
    public const int PruneProtectTokens = 40_000;   // Don't prune if under this
    public const int PruneMinimumTokens = 20_000;   // Minimum reduction target
    public const int PruneOutputRetainChars = 2_000; // Keep first N chars of pruned output

    // Layer 3: Compaction
    public const double CompactionThreshold = 0.90;  // 90% of context
    public const int CompactionTargetPercent = 50;   // Target 50% after compaction

    // Per-tool output limits (chars)
    public static readonly Dictionary<string, int> ToolOutputLimits = new()
    {
        ["read"] = 100_000,      // 100KB for file reads
        ["bash"] = 50_000,       // 50KB for command output
        ["grep"] = 30_000,       // 30KB for search results
        ["glob"] = 20_000,       // 20KB for file lists
        ["webfetch"] = 50_000,   // 50KB for web content
        ["websearch"] = 20_000,  // 20KB for search results
        ["list"] = 10_000,       // 10KB for directory listings
        ["default"] = 50_000     // Default limit
    };
}
```

---

## Layer 1: Tool Output Truncation

### Truncation Service

```csharp
namespace OpenFork.Core.Services;

public interface IOutputTruncationService
{
    TruncationResult Truncate(string output, string toolName, string? spillPath = null);
    Task<string> RetrieveSpilledAsync(string spillPath, CancellationToken ct = default);
}

public record TruncationResult
{
    public string Output { get; init; } = string.Empty;
    public bool WasTruncated { get; init; }
    public int OriginalLines { get; init; }
    public int OriginalBytes { get; init; }
    public int TruncatedLines { get; init; }
    public int TruncatedBytes { get; init; }
    public string? SpillPath { get; init; }
    public string? TruncationMessage { get; init; }
}

public class OutputTruncationService : IOutputTruncationService
{
    private readonly ILogger<OutputTruncationService> _logger;
    private readonly string _spillDirectory;

    public OutputTruncationService(
        ILogger<OutputTruncationService> logger,
        IOptions<AppSettings> settings)
    {
        _logger = logger;
        _spillDirectory = Path.Combine(
            Path.GetDirectoryName(settings.Value.DatabasePath) ?? "data",
            "spill");
        Directory.CreateDirectory(_spillDirectory);
    }

    public TruncationResult Truncate(string output, string toolName, string? spillPath = null)
    {
        var originalBytes = Encoding.UTF8.GetByteCount(output);
        var lines = output.Split('\n');
        var originalLines = lines.Length;

        // Get tool-specific limit
        var charLimit = TokenConstants.ToolOutputLimits.GetValueOrDefault(
            toolName,
            TokenConstants.ToolOutputLimits["default"]);

        // Check if truncation needed
        bool needsTruncation = originalLines > TokenConstants.MaxOutputLines ||
                               originalBytes > TokenConstants.MaxOutputBytes ||
                               output.Length > charLimit;

        if (!needsTruncation)
        {
            return new TruncationResult
            {
                Output = TruncateLineLength(output),
                WasTruncated = false,
                OriginalLines = originalLines,
                OriginalBytes = originalBytes,
                TruncatedLines = originalLines,
                TruncatedBytes = originalBytes
            };
        }

        // Spill full output to disk if requested
        string? actualSpillPath = null;
        if (spillPath != null || originalBytes > TokenConstants.MaxOutputBytes)
        {
            actualSpillPath = spillPath ?? Path.Combine(
                _spillDirectory,
                $"{Guid.NewGuid():N}.txt");

            File.WriteAllText(actualSpillPath, output);
            _logger.LogDebug("Spilled {Bytes} bytes to {Path}", originalBytes, actualSpillPath);
        }

        // Truncate to limits
        var truncatedLines = new List<string>();
        var currentBytes = 0;
        var lineCount = 0;

        foreach (var line in lines)
        {
            if (lineCount >= TokenConstants.MaxOutputLines)
                break;

            var truncatedLine = TruncateLine(line);
            var lineBytes = Encoding.UTF8.GetByteCount(truncatedLine) + 1; // +1 for newline

            if (currentBytes + lineBytes > TokenConstants.MaxOutputBytes)
                break;

            truncatedLines.Add(truncatedLine);
            currentBytes += lineBytes;
            lineCount++;
        }

        // Apply char limit
        var truncatedOutput = string.Join('\n', truncatedLines);
        if (truncatedOutput.Length > charLimit)
        {
            truncatedOutput = truncatedOutput[..charLimit];
        }

        // Build truncation message
        var truncationMessage = BuildTruncationMessage(
            originalLines, originalBytes,
            truncatedLines.Count, currentBytes,
            actualSpillPath);

        return new TruncationResult
        {
            Output = truncatedOutput + "\n\n" + truncationMessage,
            WasTruncated = true,
            OriginalLines = originalLines,
            OriginalBytes = originalBytes,
            TruncatedLines = truncatedLines.Count,
            TruncatedBytes = currentBytes,
            SpillPath = actualSpillPath,
            TruncationMessage = truncationMessage
        };
    }

    private static string TruncateLine(string line)
    {
        if (line.Length <= TokenConstants.MaxLineLength)
            return line;

        return line[..TokenConstants.MaxLineLength] + "... (line truncated)";
    }

    private static string TruncateLineLength(string output)
    {
        var lines = output.Split('\n');
        var truncated = lines.Select(TruncateLine);
        return string.Join('\n', truncated);
    }

    private static string BuildTruncationMessage(
        int origLines, int origBytes,
        int truncLines, int truncBytes,
        string? spillPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"[Output truncated: {origLines} → {truncLines} lines, {FormatBytes(origBytes)} → {FormatBytes(truncBytes)}]");

        if (spillPath != null)
        {
            sb.AppendLine($"[Full output saved to: {spillPath}]");
            sb.AppendLine("[Use 'read' tool with the path above to see full content]");
        }

        return sb.ToString();
    }

    private static string FormatBytes(int bytes) => bytes switch
    {
        < 1024 => $"{bytes}B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1}KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1}MB"
    };

    public async Task<string> RetrieveSpilledAsync(string spillPath, CancellationToken ct = default)
    {
        if (!File.Exists(spillPath))
            throw new FileNotFoundException($"Spill file not found: {spillPath}");

        return await File.ReadAllTextAsync(spillPath, ct);
    }
}
```

---

## Layer 2: Tool Output Pruning

### Pruning Service

```csharp
namespace OpenFork.Core.Services;

public interface IOutputPruningService
{
    PruneResult PruneMessages(
        IReadOnlyList<MessagePart> parts,
        int currentTokens,
        int contextLimit);
}

public record PruneResult
{
    public IReadOnlyList<MessagePart> PrunedParts { get; init; } = Array.Empty<MessagePart>();
    public int TokensBefore { get; init; }
    public int TokensAfter { get; init; }
    public int PartsPruned { get; init; }
    public bool WasPruned { get; init; }
}

public class OutputPruningService : IOutputPruningService
{
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<OutputPruningService> _logger;

    public OutputPruningService(
        ITokenEstimator tokenEstimator,
        ILogger<OutputPruningService> logger)
    {
        _tokenEstimator = tokenEstimator;
        _logger = logger;
    }

    public PruneResult PruneMessages(
        IReadOnlyList<MessagePart> parts,
        int currentTokens,
        int contextLimit)
    {
        // Check if pruning is needed
        var availableForOutput = contextLimit - TokenConstants.DefaultMaxOutputTokens;
        if (currentTokens < availableForOutput)
        {
            return new PruneResult
            {
                PrunedParts = parts.ToList(),
                TokensBefore = currentTokens,
                TokensAfter = currentTokens,
                PartsPruned = 0,
                WasPruned = false
            };
        }

        // Don't prune if we don't have enough tokens yet
        if (currentTokens < TokenConstants.PruneProtectTokens)
        {
            return new PruneResult
            {
                PrunedParts = parts.ToList(),
                TokensBefore = currentTokens,
                TokensAfter = currentTokens,
                PartsPruned = 0,
                WasPruned = false
            };
        }

        _logger.LogInformation(
            "Starting output pruning: {Current} tokens, target reduction: {Target}",
            currentTokens,
            TokenConstants.PruneMinimumTokens);

        var prunedParts = new List<MessagePart>(parts.Count);
        var tokensRemoved = 0;
        var partsPruned = 0;

        // Process parts from oldest to newest
        // Protect recent parts (within PruneProtect window)
        var protectedTokens = 0;
        var protectedIndex = parts.Count;

        // Find protection boundary from end
        for (int i = parts.Count - 1; i >= 0; i--)
        {
            var partTokens = _tokenEstimator.EstimateTokens(parts[i]);
            if (protectedTokens + partTokens > TokenConstants.PruneProtectTokens)
            {
                protectedIndex = i + 1;
                break;
            }
            protectedTokens += partTokens;
        }

        // Prune older tool outputs
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];

            // Protect recent parts
            if (i >= protectedIndex)
            {
                prunedParts.Add(part);
                continue;
            }

            // Only prune tool output parts
            if (part is ToolPart toolPart && toolPart.Output?.Length > TokenConstants.PruneOutputRetainChars)
            {
                var prunedOutput = toolPart.Output[..TokenConstants.PruneOutputRetainChars];
                var originalTokens = _tokenEstimator.EstimateTokens(toolPart.Output);
                var prunedTokens = _tokenEstimator.EstimateTokens(prunedOutput);

                prunedParts.Add(toolPart with
                {
                    Output = prunedOutput + $"\n\n[Output pruned: kept first {TokenConstants.PruneOutputRetainChars} chars]",
                    IsPruned = true
                });

                tokensRemoved += originalTokens - prunedTokens;
                partsPruned++;

                // Stop if we've removed enough
                if (tokensRemoved >= TokenConstants.PruneMinimumTokens)
                {
                    // Add remaining parts unpruned
                    for (int j = i + 1; j < parts.Count; j++)
                    {
                        prunedParts.Add(parts[j]);
                    }
                    break;
                }
            }
            else
            {
                prunedParts.Add(part);
            }
        }

        var tokensAfter = currentTokens - tokensRemoved;
        _logger.LogInformation(
            "Pruning complete: {Before} → {After} tokens ({Removed} removed, {Parts} parts pruned)",
            currentTokens, tokensAfter, tokensRemoved, partsPruned);

        return new PruneResult
        {
            PrunedParts = prunedParts,
            TokensBefore = currentTokens,
            TokensAfter = tokensAfter,
            PartsPruned = partsPruned,
            WasPruned = tokensRemoved > 0
        };
    }
}
```

---

## Layer 3: Conversation Compaction

### Compaction Service

```csharp
namespace OpenFork.Core.Services;

public interface ICompactionService
{
    Task<CompactionResult> CompactConversationAsync(
        Guid sessionId,
        IReadOnlyList<Message> messages,
        int currentTokens,
        int contextLimit,
        CancellationToken ct = default);

    Task<IReadOnlyList<Message>> LoadMessagesWithCompactionBoundaryAsync(
        Guid sessionId,
        CancellationToken ct = default);
}

public record CompactionResult
{
    public bool WasCompacted { get; init; }
    public int TokensBefore { get; init; }
    public int TokensAfter { get; init; }
    public int MessagesCompacted { get; init; }
    public string? Summary { get; init; }
    public Guid? CompactionPartId { get; init; }
}

public class CompactionService : ICompactionService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IMessagePartRepository _partRepository;
    private readonly IChatProvider _chatProvider;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly ILogger<CompactionService> _logger;

    // Compaction agent system prompt
    private const string CompactionPrompt = """
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
        IChatProvider chatProvider,
        ITokenEstimator tokenEstimator,
        ILogger<CompactionService> logger)
    {
        _messageRepository = messageRepository;
        _partRepository = partRepository;
        _chatProvider = chatProvider;
        _tokenEstimator = tokenEstimator;
        _logger = logger;
    }

    public async Task<CompactionResult> CompactConversationAsync(
        Guid sessionId,
        IReadOnlyList<Message> messages,
        int currentTokens,
        int contextLimit,
        CancellationToken ct = default)
    {
        // Check if compaction is needed
        var threshold = (int)(contextLimit * TokenConstants.CompactionThreshold);
        if (currentTokens < threshold)
        {
            return new CompactionResult
            {
                WasCompacted = false,
                TokensBefore = currentTokens,
                TokensAfter = currentTokens
            };
        }

        _logger.LogInformation(
            "Starting conversation compaction: {Current} tokens (threshold: {Threshold})",
            currentTokens, threshold);

        // Calculate target size (50% of context)
        var targetTokens = (int)(contextLimit * TokenConstants.CompactionTargetPercent / 100.0);

        // Find messages to compact (older half of conversation)
        var tokensToRemove = currentTokens - targetTokens;
        var messagesToCompact = new List<Message>();
        var accumulatedTokens = 0;

        foreach (var message in messages)
        {
            var messageTokens = _tokenEstimator.EstimateMessageTokens(message);
            if (accumulatedTokens + messageTokens > tokensToRemove)
                break;

            messagesToCompact.Add(message);
            accumulatedTokens += messageTokens;
        }

        if (messagesToCompact.Count < 2)
        {
            _logger.LogWarning("Not enough messages to compact");
            return new CompactionResult
            {
                WasCompacted = false,
                TokensBefore = currentTokens,
                TokensAfter = currentTokens
            };
        }

        // Generate summary using LLM
        var summary = await GenerateSummaryAsync(messagesToCompact, ct);

        // Create compaction marker part
        var compactionPart = new CompactionPart
        {
            Id = Guid.NewGuid(),
            MessageId = messages[messagesToCompact.Count].Id,  // First non-compacted message
            Summary = summary,
            CompactedMessageCount = messagesToCompact.Count,
            CompactedTokenCount = accumulatedTokens,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _partRepository.CreateAsync(compactionPart, ct);

        // Mark old messages as compacted (don't delete, for audit trail)
        foreach (var message in messagesToCompact)
        {
            message.IsCompacted = true;
            await _messageRepository.UpdateAsync(message, ct);
        }

        var summaryTokens = _tokenEstimator.EstimateTokens(summary);
        var tokensAfter = currentTokens - accumulatedTokens + summaryTokens;

        _logger.LogInformation(
            "Compaction complete: {Before} → {After} tokens ({Count} messages compacted)",
            currentTokens, tokensAfter, messagesToCompact.Count);

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

    private async Task<string> GenerateSummaryAsync(
        IReadOnlyList<Message> messages,
        CancellationToken ct)
    {
        // Build conversation text for summarization
        var conversationText = new StringBuilder();
        foreach (var message in messages)
        {
            conversationText.AppendLine($"[{message.Role}]");
            conversationText.AppendLine(message.Content);
            conversationText.AppendLine();
        }

        var request = new ChatCompletionRequest
        {
            Messages = new List<ChatMessage>
            {
                new() { Role = "system", Content = CompactionPrompt },
                new() { Role = "user", Content = $"Summarize this conversation:\n\n{conversationText}" }
            },
            MaxTokens = 2000
        };

        var response = await _chatProvider.CompleteAsync(request, ct);
        return response.Content ?? "[Compaction failed]";
    }

    public async Task<IReadOnlyList<Message>> LoadMessagesWithCompactionBoundaryAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        // Find most recent compaction marker
        var compactionPart = await _partRepository.GetMostRecentCompactionAsync(sessionId, ct);

        if (compactionPart == null)
        {
            // No compaction, load all messages
            return await _messageRepository.GetBySessionIdAsync(sessionId, ct);
        }

        // Load messages starting from compaction boundary
        var messages = new List<Message>();

        // First, add synthetic message with compaction summary
        messages.Add(new Message
        {
            Id = Guid.Empty,
            SessionId = sessionId,
            Role = "system",
            Content = $"""
                [Previous conversation compacted]

                {compactionPart.Summary}

                [End of compacted summary - {compactionPart.CompactedMessageCount} messages, {compactionPart.CompactedTokenCount} tokens]
                """,
            CreatedAt = compactionPart.CreatedAt
        });

        // Then load messages after the compaction boundary
        var recentMessages = await _messageRepository.GetMessagesAfterAsync(
            sessionId,
            compactionPart.MessageId,
            ct);

        messages.AddRange(recentMessages);
        return messages;
    }
}
```

---

## Token Estimator

```csharp
namespace OpenFork.Core.Services;

public interface ITokenEstimator
{
    int EstimateTokens(string text);
    int EstimateTokens(MessagePart part);
    int EstimateMessageTokens(Message message);
    int EstimateRequestTokens(ChatCompletionRequest request);
}

public class TokenEstimator : ITokenEstimator
{
    // Simple estimation: 4 chars ≈ 1 token
    // For more accuracy, consider using tiktoken or similar
    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / TokenConstants.CharsPerToken);
    }

    public int EstimateTokens(MessagePart part)
    {
        return part switch
        {
            TextPart tp => EstimateTokens(tp.Content),
            ToolPart toolPart => EstimateTokens(toolPart.Input ?? "") +
                                 EstimateTokens(toolPart.Output ?? ""),
            CompactionPart cp => EstimateTokens(cp.Summary ?? ""),
            ReasoningPart rp => EstimateTokens(rp.Content),
            _ => 0
        };
    }

    public int EstimateMessageTokens(Message message)
    {
        var tokens = EstimateTokens(message.Content ?? "");

        // Add overhead for role/structure
        tokens += 4;  // Role marker overhead

        // Add tool calls if present
        if (!string.IsNullOrEmpty(message.ToolCallsJson))
        {
            tokens += EstimateTokens(message.ToolCallsJson);
        }

        return tokens;
    }

    public int EstimateRequestTokens(ChatCompletionRequest request)
    {
        var tokens = 0;

        // System prompt
        tokens += EstimateTokens(request.SystemPrompt ?? "");

        // Messages
        foreach (var message in request.Messages)
        {
            tokens += EstimateTokens(message.Content ?? "");
            tokens += 4;  // Message overhead

            if (message.ToolCalls != null)
            {
                foreach (var tc in message.ToolCalls)
                {
                    tokens += EstimateTokens(tc.Function.Name);
                    tokens += EstimateTokens(tc.Function.Arguments);
                }
            }
        }

        // Tools schema
        if (request.Tools != null)
        {
            foreach (var tool in request.Tools)
            {
                tokens += EstimateTokens(tool.Function.Name);
                tokens += EstimateTokens(tool.Function.Description ?? "");
                tokens += EstimateTokens(tool.Function.Parameters?.ToString() ?? "");
            }
        }

        return tokens;
    }
}
```

---

## Integration with ChatService

```csharp
// Add to ChatService.cs

private readonly IOutputTruncationService _truncationService;
private readonly IOutputPruningService _pruningService;
private readonly ICompactionService _compactionService;
private readonly ITokenEstimator _tokenEstimator;

public async Task<string> RunAsync(
    Session session,
    string userInput,
    CancellationToken ct = default)
{
    // Load messages with compaction boundary awareness
    var messages = await _compactionService.LoadMessagesWithCompactionBoundaryAsync(
        session.Id, ct);

    var chatMessages = messages.Select(m => m.ToChatMessage()).ToList();
    chatMessages.Add(new ChatMessage { Role = "user", Content = userInput });

    var iteration = 0;
    while (iteration < _maxIterations)
    {
        iteration++;

        // Estimate current token usage
        var currentTokens = _tokenEstimator.EstimateRequestTokens(
            BuildRequest(chatMessages));

        var contextLimit = await GetContextLimitAsync(session);

        // Layer 2: Prune if needed
        if (currentTokens > contextLimit * 0.85)
        {
            var parts = await GetMessagePartsAsync(session.Id, ct);
            var pruneResult = _pruningService.PruneMessages(
                parts, currentTokens, contextLimit);

            if (pruneResult.WasPruned)
            {
                _logger.LogInformation(
                    "Pruned {Tokens} tokens from tool outputs",
                    pruneResult.TokensBefore - pruneResult.TokensAfter);
            }
        }

        // Layer 3: Compact if still over threshold
        currentTokens = _tokenEstimator.EstimateRequestTokens(
            BuildRequest(chatMessages));

        if (currentTokens > contextLimit * TokenConstants.CompactionThreshold)
        {
            var compactionResult = await _compactionService.CompactConversationAsync(
                session.Id,
                await _messageRepository.GetBySessionIdAsync(session.Id, ct),
                currentTokens,
                contextLimit,
                ct);

            if (compactionResult.WasCompacted)
            {
                // Reload messages with compaction boundary
                messages = await _compactionService.LoadMessagesWithCompactionBoundaryAsync(
                    session.Id, ct);
                chatMessages = messages.Select(m => m.ToChatMessage()).ToList();
            }
        }

        // Execute LLM call
        var response = await ExecuteLlmAsync(chatMessages, ct);

        // Process tool calls with Layer 1 truncation
        if (response.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in response.ToolCalls)
            {
                var toolResult = await ExecuteToolAsync(toolCall, session, ct);

                // Layer 1: Truncate tool output
                var truncated = _truncationService.Truncate(
                    toolResult,
                    toolCall.Function.Name);

                chatMessages.Add(new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = truncated.Output
                });

                if (truncated.WasTruncated)
                {
                    _logger.LogDebug(
                        "Truncated {Tool} output: {Before} → {After} bytes",
                        toolCall.Function.Name,
                        truncated.OriginalBytes,
                        truncated.TruncatedBytes);
                }
            }
        }
        else
        {
            break;  // No more tool calls
        }
    }

    return ExtractFinalResponse(chatMessages);
}
```

---

## Database Schema Additions

```sql
-- Add compaction tracking
ALTER TABLE Messages ADD COLUMN IsCompacted INTEGER DEFAULT 0;

-- Compaction parts table
CREATE TABLE IF NOT EXISTS CompactionParts (
    Id TEXT PRIMARY KEY,
    MessageId TEXT NOT NULL,
    Summary TEXT NOT NULL,
    CompactedMessageCount INTEGER NOT NULL,
    CompactedTokenCount INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (MessageId) REFERENCES Messages(Id)
);

CREATE INDEX IF NOT EXISTS idx_compaction_message ON CompactionParts(MessageId);
```

---

## Configuration

```json
// appsettings.json additions
{
  "TokenManagement": {
    "MaxOutputLines": 2000,
    "MaxOutputBytes": 51200,
    "MaxLineLength": 2000,
    "PruneProtectTokens": 40000,
    "PruneMinimumTokens": 20000,
    "CompactionThreshold": 0.90,
    "CompactionTargetPercent": 50,
    "EnableDiskSpillover": true,
    "SpillDirectory": "data/spill",
    "ToolOutputLimits": {
      "read": 100000,
      "bash": 50000,
      "grep": 30000,
      "glob": 20000,
      "default": 50000
    }
  }
}
```

---

## Monitoring and Metrics

```csharp
public class TokenManagementMetrics
{
    public int TotalTruncations { get; set; }
    public int TotalPrunings { get; set; }
    public int TotalCompactions { get; set; }
    public long BytesTruncated { get; set; }
    public long TokensPruned { get; set; }
    public long TokensCompacted { get; set; }
    public long SpillFilesCreated { get; set; }
    public long SpillBytesWritten { get; set; }
}

// Track in ChatService or dedicated metrics service
```

---

## Testing Strategy

```csharp
[Fact]
public void TruncateOutput_RespectsLineLimit()
{
    var output = string.Join('\n', Enumerable.Repeat("line", 3000));
    var result = _service.Truncate(output, "bash");

    Assert.True(result.WasTruncated);
    Assert.True(result.TruncatedLines <= TokenConstants.MaxOutputLines);
}

[Fact]
public void PruneMessages_ProtectsRecentContent()
{
    // Verify recent messages are not pruned
}

[Fact]
public async Task CompactConversation_GeneratesSummary()
{
    // Verify LLM summarization and marker insertion
}

[Fact]
public async Task LoadMessages_RespectsCompactionBoundary()
{
    // Verify messages before compaction are replaced with summary
}
```

---

## Performance Considerations

1. **Lazy Truncation**: Only truncate when output exceeds limits
2. **Incremental Pruning**: Stop pruning once minimum reduction achieved
3. **Async Compaction**: Run compaction in background when possible
4. **Spill Cleanup**: Schedule cleanup of old spill files
5. **Token Caching**: Cache token estimates for unchanged content

---

## Migration Path

1. Add new services to DI container
2. Update ChatService to use 3-layer approach
3. Run migration for schema changes
4. Configure per-tool limits in appsettings
5. Enable disk spillover
6. Monitor metrics and tune thresholds
