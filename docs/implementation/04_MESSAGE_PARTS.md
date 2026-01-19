# Message Parts System Implementation Guide

## Overview

The message parts system provides fine-grained structure for conversation content, enabling rich UI rendering, state tracking, and precise context management.

---

## Architecture Analysis

### Current State (OpenFork)

```csharp
// Current simple message model
public class Message
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; }          // "user", "assistant", "tool", "system"
    public string Content { get; set; }       // Plain text content
    public string? ToolCallsJson { get; set; } // Serialized tool calls
    public Guid? AgentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

**Limitations**:
- Tool calls stored as JSON string (no structured access)
- No state machine for tool execution
- No distinction between text types (regular, reasoning, code)
- No support for attachments or rich content
- Compaction information not tracked

### Target State (opencode-aligned)

```
┌─────────────────────────────────────────────────────────────────┐
│                        Message                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Role, AgentId, ModelId, ParentId, SystemPrompt            │  │
│  │ Tokens: { input, output, reasoning, cache }               │  │
│  │ Cost, FinishReason, Path, Errors[]                        │  │
│  └───────────────────────────────────────────────────────────┘  │
│                              │                                   │
│                              ▼                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                    Message Parts (1:N)                     │  │
│  │                                                            │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐      │  │
│  │  │ TextPart │ │ToolPart  │ │Reasoning │ │ FilePart │      │  │
│  │  │          │ │(state    │ │Part      │ │          │      │  │
│  │  │          │ │machine)  │ │          │ │          │      │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘      │  │
│  │                                                            │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐      │  │
│  │  │ StepPart │ │PatchPart │ │ Snapshot │ │ Compact- │      │  │
│  │  │          │ │          │ │ Part     │ │ ionPart  │      │  │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘      │  │
│  │                                                            │  │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐                   │  │
│  │  │AgentPart │ │RetryPart │ │ Subtask  │                   │  │
│  │  │          │ │          │ │ Part     │                   │  │
│  │  └──────────┘ └──────────┘ └──────────┘                   │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Domain Model

### Enhanced Message

```csharp
namespace OpenFork.Core.Domain;

/// <summary>
/// A message in the conversation with rich metadata.
/// </summary>
public class Message
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? ParentId { get; set; }     // For threaded messages (user → assistant)

    // Identity
    public string Role { get; set; } = "user";  // user, assistant, tool, system
    public Guid? AgentId { get; set; }
    public string? ModelId { get; set; }    // e.g., "gpt-4o", "claude-3-opus"
    public string? ProviderId { get; set; } // e.g., "openai", "anthropic"

    // Content (summary - actual content in parts)
    public string? SystemPrompt { get; set; }   // For user messages
    public string? ToolsOverrideJson { get; set; }  // Custom tools for this message
    public string? Summary { get; set; }        // Brief summary for display

    // Token accounting (for assistant messages)
    public TokenUsage? Tokens { get; set; }
    public decimal? Cost { get; set; }

    // Execution metadata
    public string? FinishReason { get; set; }  // stop, length, tool_calls, error
    public string? WorkingDirectory { get; set; }
    public string? RootDirectory { get; set; }
    public List<MessageError>? Errors { get; set; }

    // State
    public bool IsCompacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation
    public ICollection<MessagePart> Parts { get; set; } = new List<MessagePart>();
}

public record TokenUsage
{
    public int Input { get; init; }
    public int Output { get; init; }
    public int Reasoning { get; init; }
    public CacheUsage? Cache { get; init; }
}

public record CacheUsage
{
    public int Read { get; init; }
    public int Write { get; init; }
}

public record MessageError
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}
```

### Message Part Base

```csharp
namespace OpenFork.Core.Domain;

/// <summary>
/// Base class for all message part types.
/// </summary>
public abstract class MessagePart
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public int OrderIndex { get; set; }     // Order within message
    public string Type { get; set; } = string.Empty;  // Discriminator
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation
    public Message? Message { get; set; }
}
```

### Part Type Implementations

```csharp
namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Plain text content from LLM response.
/// </summary>
public class TextPart : MessagePart
{
    public new string Type => "text";
    public string Content { get; set; } = string.Empty;
    public TextContentType ContentType { get; set; } = TextContentType.Markdown;
}

public enum TextContentType
{
    Plain,
    Markdown,
    Code
}

/// <summary>
/// Chain-of-thought reasoning (if model supports).
/// </summary>
public class ReasoningPart : MessagePart
{
    public new string Type => "reasoning";
    public string Content { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;  // Can be hidden in UI
}

/// <summary>
/// Tool invocation with full lifecycle tracking.
/// </summary>
public class ToolPart : MessagePart
{
    public new string Type => "tool";

    // Identity
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;  // Human-readable summary

    // State machine
    public ToolPartStatus Status { get; set; } = ToolPartStatus.Pending;

    // Input/Output
    public string? Input { get; set; }      // JSON arguments
    public string? Output { get; set; }     // Result content
    public bool IsPruned { get; set; }      // Output was pruned

    // Timing
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TimeSpan? Duration => CompletedAt.HasValue && StartedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;

    // Error handling
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }

    // Attachments (for rich tool outputs)
    public List<ToolAttachment>? Attachments { get; set; }

    // Disk spillover
    public string? SpillPath { get; set; }  // Path to full output if truncated
}

public enum ToolPartStatus
{
    Pending,    // Created, not yet started
    Running,    // Actively executing
    Completed,  // Successfully finished
    Error       // Failed with error
}

public record ToolAttachment
{
    public string Type { get; init; } = string.Empty;  // file, image, chart, etc.
    public string Name { get; init; } = string.Empty;
    public string? Path { get; init; }
    public string? ContentType { get; init; }
    public long? Size { get; init; }
}

/// <summary>
/// File attachment or reference.
/// </summary>
public class FilePart : MessagePart
{
    public new string Type => "file";
    public string FilePath { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? Size { get; set; }
    public string? Content { get; set; }    // Inline content if small
    public bool IsInline { get; set; }      // Stored inline vs referenced
}

/// <summary>
/// Code diff/patch representation.
/// </summary>
public class PatchPart : MessagePart
{
    public new string Type => "patch";
    public string FilePath { get; set; } = string.Empty;
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
    public string? UnifiedDiff { get; set; }
    public int Additions { get; set; }
    public int Deletions { get; set; }
}

/// <summary>
/// State snapshot for restoration/debugging.
/// </summary>
public class SnapshotPart : MessagePart
{
    public new string Type => "snapshot";
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, object?>? State { get; set; }
    public string? GitCommit { get; set; }  // Optional commit reference
}

/// <summary>
/// Agent step boundary marker.
/// </summary>
public class StepPart : MessagePart
{
    public new string Type => "step";
    public int StepNumber { get; set; }
    public string? Description { get; set; }
    public StepStatus Status { get; set; } = StepStatus.InProgress;
}

public enum StepStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed
}

/// <summary>
/// Agent invocation/handoff marker.
/// </summary>
public class AgentPart : MessagePart
{
    public new string Type => "agent";
    public string AgentType { get; set; } = string.Empty;
    public string? AgentName { get; set; }
    public Guid? TargetAgentId { get; set; }
    public string? Reason { get; set; }     // Why agent was invoked
}

/// <summary>
/// Retry marker for failed operations.
/// </summary>
public class RetryPart : MessagePart
{
    public new string Type => "retry";
    public int AttemptNumber { get; set; }
    public string? Reason { get; set; }
    public string? OriginalError { get; set; }
    public TimeSpan? DelayBefore { get; set; }
}

/// <summary>
/// Compaction boundary marker.
/// </summary>
public class CompactionPart : MessagePart
{
    public new string Type => "compaction";
    public string? Summary { get; set; }
    public int CompactedMessageCount { get; set; }
    public int CompactedTokenCount { get; set; }
    public DateTimeOffset CompactedAt { get; set; }
}

/// <summary>
/// Subtask/subagent reference.
/// </summary>
public class SubtaskPart : MessagePart
{
    public new string Type => "subtask";
    public Guid SubSessionId { get; set; }
    public string AgentType { get; set; } = string.Empty;
    public string? Prompt { get; set; }
    public SubtaskStatus Status { get; set; } = SubtaskStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
}

public enum SubtaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
```

---

## Repository Layer

### Part Repository Interface

```csharp
namespace OpenFork.Core.Abstractions;

public interface IMessagePartRepository
{
    // CRUD
    Task<MessagePart> CreateAsync(MessagePart part, CancellationToken ct = default);
    Task<MessagePart?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(MessagePart part, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    // Queries
    Task<IReadOnlyList<MessagePart>> GetByMessageIdAsync(
        Guid messageId,
        CancellationToken ct = default);

    Task<IReadOnlyList<MessagePart>> GetBySessionIdAsync(
        Guid sessionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ToolPart>> GetToolPartsByStatusAsync(
        Guid sessionId,
        ToolPartStatus status,
        CancellationToken ct = default);

    Task<CompactionPart?> GetMostRecentCompactionAsync(
        Guid sessionId,
        CancellationToken ct = default);

    // Typed queries
    Task<T?> GetTypedPartAsync<T>(Guid id, CancellationToken ct = default)
        where T : MessagePart;
}
```

### Dapper Implementation

```csharp
namespace OpenFork.Storage.Repositories;

public class MessagePartRepository : IMessagePartRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<MessagePartRepository> _logger;

    public MessagePartRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<MessagePartRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<MessagePart> CreateAsync(MessagePart part, CancellationToken ct = default)
    {
        part.Id = part.Id == Guid.Empty ? Guid.NewGuid() : part.Id;
        part.CreatedAt = DateTimeOffset.UtcNow;

        using var connection = _connectionFactory.CreateConnection();

        var sql = """
            INSERT INTO MessageParts (
                Id, MessageId, OrderIndex, Type, CreatedAt, UpdatedAt, DataJson
            ) VALUES (
                @Id, @MessageId, @OrderIndex, @Type, @CreatedAt, @UpdatedAt, @DataJson
            )
            """;

        await connection.ExecuteAsync(sql, new
        {
            part.Id,
            part.MessageId,
            part.OrderIndex,
            Type = part.GetType().Name,
            part.CreatedAt,
            part.UpdatedAt,
            DataJson = SerializePart(part)
        });

        return part;
    }

    public async Task<MessagePart?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = "SELECT * FROM MessageParts WHERE Id = @Id";
        var row = await connection.QuerySingleOrDefaultAsync<MessagePartRow>(sql, new { Id = id });

        return row != null ? DeserializePart(row) : null;
    }

    public async Task<IReadOnlyList<MessagePart>> GetByMessageIdAsync(
        Guid messageId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = """
            SELECT * FROM MessageParts
            WHERE MessageId = @MessageId
            ORDER BY OrderIndex
            """;

        var rows = await connection.QueryAsync<MessagePartRow>(sql, new { MessageId = messageId });
        return rows.Select(DeserializePart).ToList();
    }

    public async Task<IReadOnlyList<ToolPart>> GetToolPartsByStatusAsync(
        Guid sessionId,
        ToolPartStatus status,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = """
            SELECT mp.* FROM MessageParts mp
            INNER JOIN Messages m ON mp.MessageId = m.Id
            WHERE m.SessionId = @SessionId
              AND mp.Type = 'ToolPart'
              AND json_extract(mp.DataJson, '$.Status') = @Status
            ORDER BY mp.CreatedAt
            """;

        var rows = await connection.QueryAsync<MessagePartRow>(sql, new
        {
            SessionId = sessionId,
            Status = status.ToString()
        });

        return rows.Select(DeserializePart).OfType<ToolPart>().ToList();
    }

    public async Task<CompactionPart?> GetMostRecentCompactionAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = """
            SELECT mp.* FROM MessageParts mp
            INNER JOIN Messages m ON mp.MessageId = m.Id
            WHERE m.SessionId = @SessionId
              AND mp.Type = 'CompactionPart'
            ORDER BY mp.CreatedAt DESC
            LIMIT 1
            """;

        var row = await connection.QuerySingleOrDefaultAsync<MessagePartRow>(sql,
            new { SessionId = sessionId });

        return row != null ? DeserializePart(row) as CompactionPart : null;
    }

    // Serialization helpers
    private static string SerializePart(MessagePart part)
    {
        return JsonSerializer.Serialize(part, part.GetType(), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    private MessagePart DeserializePart(MessagePartRow row)
    {
        var type = GetPartType(row.Type);
        var part = (MessagePart)JsonSerializer.Deserialize(row.DataJson, type,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;

        part.Id = Guid.Parse(row.Id);
        part.MessageId = Guid.Parse(row.MessageId);
        part.OrderIndex = row.OrderIndex;
        part.CreatedAt = DateTimeOffset.Parse(row.CreatedAt);
        part.UpdatedAt = row.UpdatedAt != null ? DateTimeOffset.Parse(row.UpdatedAt) : null;

        return part;
    }

    private static Type GetPartType(string typeName) => typeName switch
    {
        "TextPart" => typeof(TextPart),
        "ReasoningPart" => typeof(ReasoningPart),
        "ToolPart" => typeof(ToolPart),
        "FilePart" => typeof(FilePart),
        "PatchPart" => typeof(PatchPart),
        "SnapshotPart" => typeof(SnapshotPart),
        "StepPart" => typeof(StepPart),
        "AgentPart" => typeof(AgentPart),
        "RetryPart" => typeof(RetryPart),
        "CompactionPart" => typeof(CompactionPart),
        "SubtaskPart" => typeof(SubtaskPart),
        _ => throw new InvalidOperationException($"Unknown part type: {typeName}")
    };

    private record MessagePartRow(
        string Id,
        string MessageId,
        int OrderIndex,
        string Type,
        string CreatedAt,
        string? UpdatedAt,
        string DataJson);
}
```

---

## Database Schema

```sql
-- Messages table (enhanced)
CREATE TABLE IF NOT EXISTS Messages (
    Id TEXT PRIMARY KEY,
    SessionId TEXT NOT NULL,
    ParentId TEXT,
    Role TEXT NOT NULL,
    AgentId TEXT,
    ModelId TEXT,
    ProviderId TEXT,
    SystemPrompt TEXT,
    ToolsOverrideJson TEXT,
    Summary TEXT,
    TokensJson TEXT,          -- Serialized TokenUsage
    Cost REAL,
    FinishReason TEXT,
    WorkingDirectory TEXT,
    RootDirectory TEXT,
    ErrorsJson TEXT,          -- Serialized List<MessageError>
    IsCompacted INTEGER DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    FOREIGN KEY (SessionId) REFERENCES Sessions(Id) ON DELETE CASCADE,
    FOREIGN KEY (ParentId) REFERENCES Messages(Id)
);

CREATE INDEX IF NOT EXISTS idx_messages_session ON Messages(SessionId);
CREATE INDEX IF NOT EXISTS idx_messages_parent ON Messages(ParentId);
CREATE INDEX IF NOT EXISTS idx_messages_agent ON Messages(AgentId);

-- Message parts table (polymorphic)
CREATE TABLE IF NOT EXISTS MessageParts (
    Id TEXT PRIMARY KEY,
    MessageId TEXT NOT NULL,
    OrderIndex INTEGER NOT NULL,
    Type TEXT NOT NULL,       -- Discriminator
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT,
    DataJson TEXT NOT NULL,   -- Type-specific data as JSON
    FOREIGN KEY (MessageId) REFERENCES Messages(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_parts_message ON MessageParts(MessageId);
CREATE INDEX IF NOT EXISTS idx_parts_type ON MessageParts(Type);
CREATE INDEX IF NOT EXISTS idx_parts_order ON MessageParts(MessageId, OrderIndex);
```

---

## ChatService Integration

### Building Parts During Execution

```csharp
// In ChatService

private async Task ProcessLlmResponseAsync(
    Message message,
    StreamingChatResponse response,
    CancellationToken ct)
{
    var partIndex = 0;

    // Handle text content
    if (!string.IsNullOrEmpty(response.Content))
    {
        var textPart = new TextPart
        {
            MessageId = message.Id,
            OrderIndex = partIndex++,
            Content = response.Content,
            ContentType = TextContentType.Markdown
        };
        await _partRepository.CreateAsync(textPart, ct);
        await _eventBus.PublishAsync(new PartCreatedEvent(textPart), ct);
    }

    // Handle reasoning (if model supports)
    if (!string.IsNullOrEmpty(response.Reasoning))
    {
        var reasoningPart = new ReasoningPart
        {
            MessageId = message.Id,
            OrderIndex = partIndex++,
            Content = response.Reasoning
        };
        await _partRepository.CreateAsync(reasoningPart, ct);
        await _eventBus.PublishAsync(new PartCreatedEvent(reasoningPart), ct);
    }

    // Handle tool calls
    if (response.ToolCalls != null)
    {
        foreach (var toolCall in response.ToolCalls)
        {
            var toolPart = new ToolPart
            {
                MessageId = message.Id,
                OrderIndex = partIndex++,
                ToolCallId = toolCall.Id,
                ToolName = toolCall.Function.Name,
                Title = $"Calling {toolCall.Function.Name}",
                Input = toolCall.Function.Arguments,
                Status = ToolPartStatus.Pending
            };
            await _partRepository.CreateAsync(toolPart, ct);
            await _eventBus.PublishAsync(new PartCreatedEvent(toolPart), ct);
        }
    }
}

private async Task ExecuteToolPartAsync(
    ToolPart toolPart,
    ToolContext context,
    PermissionRuleset permissions,
    CancellationToken ct)
{
    // Update to running
    toolPart.Status = ToolPartStatus.Running;
    toolPart.StartedAt = DateTimeOffset.UtcNow;
    await _partRepository.UpdateAsync(toolPart, ct);
    await _eventBus.PublishAsync(new PartUpdatedEvent(toolPart), ct);

    try
    {
        var arguments = JsonNode.Parse(toolPart.Input!)!;
        var result = await _toolRegistry.ExecuteWithPermissionAsync(
            toolPart.ToolName,
            arguments,
            context,
            permissions,
            ct);

        // Truncate output
        var truncated = _truncationService.Truncate(result.Output, toolPart.ToolName);

        // Update part
        toolPart.Status = result.Success ? ToolPartStatus.Completed : ToolPartStatus.Error;
        toolPart.Output = truncated.Output;
        toolPart.IsPruned = truncated.WasTruncated;
        toolPart.SpillPath = truncated.SpillPath;
        toolPart.CompletedAt = DateTimeOffset.UtcNow;

        if (!result.Success)
        {
            toolPart.ErrorMessage = result.Output;
        }

        // Handle file changes
        if (context.FileChangeTracker.HasChanges)
        {
            var changes = context.FileChangeTracker.GetChanges();
            toolPart.Attachments = changes.Select(c => new ToolAttachment
            {
                Type = "file_change",
                Name = Path.GetFileName(c.Path),
                Path = c.Path
            }).ToList();
        }
    }
    catch (Exception ex)
    {
        toolPart.Status = ToolPartStatus.Error;
        toolPart.ErrorMessage = ex.Message;
        toolPart.ErrorCode = ex.GetType().Name;
        toolPart.CompletedAt = DateTimeOffset.UtcNow;
    }

    await _partRepository.UpdateAsync(toolPart, ct);
    await _eventBus.PublishAsync(new PartUpdatedEvent(toolPart), ct);
}
```

### Converting Parts to Chat Messages

```csharp
// For sending to LLM
public static ChatMessage ToApiMessage(this Message message, IReadOnlyList<MessagePart> parts)
{
    if (message.Role == "user")
    {
        var content = string.Join("\n\n",
            parts.OfType<TextPart>().Select(p => p.Content));

        var files = parts.OfType<FilePart>()
            .Select(f => new { path = f.FilePath, content = f.Content })
            .ToList();

        return new ChatMessage
        {
            Role = "user",
            Content = content
        };
    }

    if (message.Role == "assistant")
    {
        var textContent = string.Join("\n\n",
            parts.OfType<TextPart>().Select(p => p.Content));

        var toolCalls = parts.OfType<ToolPart>()
            .Select(tp => new ToolCall
            {
                Id = tp.ToolCallId,
                Type = "function",
                Function = new FunctionCall
                {
                    Name = tp.ToolName,
                    Arguments = tp.Input ?? "{}"
                }
            })
            .ToList();

        return new ChatMessage
        {
            Role = "assistant",
            Content = textContent,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null
        };
    }

    if (message.Role == "tool")
    {
        var toolPart = parts.OfType<ToolPart>().FirstOrDefault();
        return new ChatMessage
        {
            Role = "tool",
            ToolCallId = toolPart?.ToolCallId,
            Content = toolPart?.Output ?? ""
        };
    }

    return new ChatMessage
    {
        Role = message.Role,
        Content = parts.OfType<TextPart>().FirstOrDefault()?.Content ?? ""
    };
}
```

---

## UI Rendering

### Part Renderers

```csharp
namespace OpenFork.Cli.Tui;

public interface IPartRenderer
{
    void Render(MessagePart part);
}

public class PartRendererFactory
{
    public IPartRenderer GetRenderer(MessagePart part) => part switch
    {
        TextPart => new TextPartRenderer(),
        ReasoningPart => new ReasoningPartRenderer(),
        ToolPart => new ToolPartRenderer(),
        FilePart => new FilePartRenderer(),
        PatchPart => new PatchPartRenderer(),
        CompactionPart => new CompactionPartRenderer(),
        SubtaskPart => new SubtaskPartRenderer(),
        _ => new DefaultPartRenderer()
    };
}

public class ToolPartRenderer : IPartRenderer
{
    public void Render(MessagePart part)
    {
        var toolPart = (ToolPart)part;

        var statusIcon = toolPart.Status switch
        {
            ToolPartStatus.Pending => "⏳",
            ToolPartStatus.Running => "🔄",
            ToolPartStatus.Completed => "✅",
            ToolPartStatus.Error => "❌",
            _ => "?"
        };

        var statusColor = toolPart.Status switch
        {
            ToolPartStatus.Pending => Color.Grey,
            ToolPartStatus.Running => Color.Yellow,
            ToolPartStatus.Completed => Color.Green,
            ToolPartStatus.Error => Color.Red,
            _ => Color.White
        };

        // Header
        AnsiConsole.MarkupLine($"[bold]{statusIcon}[/] [{statusColor}]{toolPart.ToolName}[/] - {toolPart.Title}");

        // Input (collapsed by default)
        if (!string.IsNullOrEmpty(toolPart.Input))
        {
            var inputPreview = toolPart.Input.Length > 100
                ? toolPart.Input[..100] + "..."
                : toolPart.Input;
            AnsiConsole.MarkupLine($"  [dim]Input:[/] [grey]{Markup.Escape(inputPreview)}[/]");
        }

        // Output
        if (!string.IsNullOrEmpty(toolPart.Output))
        {
            var panel = new Panel(new Text(toolPart.Output.Truncate(500)))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(statusColor),
                Padding = new Padding(1, 0)
            };

            if (toolPart.IsPruned)
            {
                panel.Header = new PanelHeader("[dim]Output (truncated)[/]");
            }

            AnsiConsole.Write(panel);
        }

        // Duration
        if (toolPart.Duration.HasValue)
        {
            AnsiConsole.MarkupLine($"  [dim]Duration: {toolPart.Duration.Value.TotalSeconds:F2}s[/]");
        }

        // Error
        if (toolPart.Status == ToolPartStatus.Error && !string.IsNullOrEmpty(toolPart.ErrorMessage))
        {
            AnsiConsole.MarkupLine($"  [red]Error: {Markup.Escape(toolPart.ErrorMessage)}[/]");
        }
    }
}

public class PatchPartRenderer : IPartRenderer
{
    public void Render(MessagePart part)
    {
        var patchPart = (PatchPart)part;

        AnsiConsole.MarkupLine($"[bold]📝 {Path.GetFileName(patchPart.FilePath)}[/]");
        AnsiConsole.MarkupLine($"  [green]+{patchPart.Additions}[/] [red]-{patchPart.Deletions}[/]");

        if (!string.IsNullOrEmpty(patchPart.UnifiedDiff))
        {
            // Syntax highlight the diff
            foreach (var line in patchPart.UnifiedDiff.Split('\n').Take(20))
            {
                var color = line.StartsWith('+') ? "green" :
                            line.StartsWith('-') ? "red" :
                            line.StartsWith('@') ? "cyan" : "white";

                AnsiConsole.MarkupLine($"  [{color}]{Markup.Escape(line)}[/]");
            }
        }
    }
}
```

---

## Event System Integration

```csharp
namespace OpenFork.Core.Events;

public record PartCreatedEvent(MessagePart Part);
public record PartUpdatedEvent(MessagePart Part);
public record PartDeletedEvent(Guid PartId, Guid MessageId);

// In TUI, subscribe to part events for live updates
public partial class ConsoleApp
{
    private void SubscribeToPartEvents()
    {
        _eventBus.Subscribe<PartCreatedEvent>(evt =>
        {
            Application.Invoke(() =>
            {
                var renderer = _partRendererFactory.GetRenderer(evt.Part);
                renderer.Render(evt.Part);
            });
        });

        _eventBus.Subscribe<PartUpdatedEvent>(evt =>
        {
            if (evt.Part is ToolPart toolPart)
            {
                Application.Invoke(() =>
                {
                    // Update status indicator in UI
                    UpdateToolPartStatus(toolPart);
                });
            }
        });
    }
}
```

---

## Migration from Current Model

### Step 1: Database Migration

```sql
-- Create new parts table
CREATE TABLE MessageParts (...);

-- Migrate existing messages
INSERT INTO MessageParts (Id, MessageId, OrderIndex, Type, CreatedAt, DataJson)
SELECT
    lower(hex(randomblob(16))),
    Id,
    0,
    'TextPart',
    CreatedAt,
    json_object('Content', Content, 'ContentType', 'Plain')
FROM Messages
WHERE Content IS NOT NULL AND Content != '';

-- Migrate tool calls
-- (requires parsing ToolCallsJson and creating ToolPart entries)
```

### Step 2: Code Migration

```csharp
// Backward compatibility extension
public static class MessageCompatExtensions
{
    // For code that still uses Message.Content
    public static string GetContent(this Message message, IMessagePartRepository partRepo)
    {
        var parts = partRepo.GetByMessageIdAsync(message.Id).GetAwaiter().GetResult();
        return string.Join("\n\n", parts.OfType<TextPart>().Select(p => p.Content));
    }

    // For code that still uses ToolCallsJson
    public static IReadOnlyList<ToolCall> GetToolCalls(this Message message, IMessagePartRepository partRepo)
    {
        var parts = partRepo.GetByMessageIdAsync(message.Id).GetAwaiter().GetResult();
        return parts.OfType<ToolPart>()
            .Select(tp => new ToolCall
            {
                Id = tp.ToolCallId,
                Function = new FunctionCall
                {
                    Name = tp.ToolName,
                    Arguments = tp.Input ?? "{}"
                }
            })
            .ToList();
    }
}
```

---

## Testing Strategy

```csharp
[Fact]
public async Task CreateTextPart_PersistsCorrectly()
{
    var part = new TextPart
    {
        MessageId = Guid.NewGuid(),
        OrderIndex = 0,
        Content = "Hello, world!",
        ContentType = TextContentType.Markdown
    };

    await _repository.CreateAsync(part);
    var loaded = await _repository.GetByIdAsync(part.Id);

    Assert.IsType<TextPart>(loaded);
    Assert.Equal("Hello, world!", ((TextPart)loaded!).Content);
}

[Fact]
public async Task ToolPart_TracksStateTransitions()
{
    var toolPart = new ToolPart
    {
        MessageId = Guid.NewGuid(),
        ToolCallId = "call_123",
        ToolName = "read",
        Status = ToolPartStatus.Pending
    };

    await _repository.CreateAsync(toolPart);

    // Transition to running
    toolPart.Status = ToolPartStatus.Running;
    toolPart.StartedAt = DateTimeOffset.UtcNow;
    await _repository.UpdateAsync(toolPart);

    // Transition to completed
    toolPart.Status = ToolPartStatus.Completed;
    toolPart.Output = "File contents here";
    toolPart.CompletedAt = DateTimeOffset.UtcNow;
    await _repository.UpdateAsync(toolPart);

    var loaded = await _repository.GetByIdAsync(toolPart.Id) as ToolPart;
    Assert.Equal(ToolPartStatus.Completed, loaded!.Status);
    Assert.NotNull(loaded.Duration);
}
```

---

## Performance Considerations

1. **Lazy Loading**: Parts loaded on demand, not with message
2. **Indexed Queries**: OrderIndex and Type are indexed
3. **JSON Storage**: Flexible schema, easy migration
4. **Batch Operations**: Create multiple parts in single transaction
5. **Event Batching**: UI updates batched at 16ms intervals

---

## Next Steps

1. Create database migration
2. Implement MessagePartRepository
3. Update ChatService to create parts
4. Update UI renderers
5. Add event publishing
6. Migrate existing data
7. Remove deprecated Message.Content usage
