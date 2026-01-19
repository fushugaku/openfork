# Event System Implementation Guide

## Overview

The event system provides decoupled communication between components via a pub/sub pattern, enabling real-time UI updates, cross-module coordination, and extensible architecture.

---

## Architecture Analysis

### Current State (OpenFork)

```
┌─────────────────────────────────────────┐
│          Direct Method Calls            │
│                                         │
│   ChatService ───────► MessageRepo      │
│        │                                │
│        └────────────► TUI (polling)     │
│                                         │
└─────────────────────────────────────────┘
```

**Limitations**:
- Tight coupling between components
- TUI must poll for updates
- No way to extend without modifying core
- Difficult to add observability

### Target State (opencode-aligned)

```
┌─────────────────────────────────────────────────────────────────┐
│                       Event Bus                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                    In-Memory Bus                          │  │
│  │                                                           │  │
│  │  Publishers ────────────────────────► Subscribers         │  │
│  │                                                           │  │
│  │  ChatService                           TUI                │  │
│  │  ToolRegistry     ┌───────────┐       Metrics             │  │
│  │  SubagentService  │  Events   │       Hooks               │  │
│  │  PermissionSvc    │  Queue    │       Audit               │  │
│  │                   └───────────┘       Extensions          │  │
│  │                        │                                  │  │
│  │                        ▼                                  │  │
│  │              Event Batching (16ms)                       │  │
│  │                        │                                  │  │
│  │                        ▼                                  │  │
│  │              Subscriber Dispatch                          │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Domain Model

### Event Base

```csharp
namespace OpenFork.Core.Events;

/// <summary>
/// Base interface for all events.
/// </summary>
public interface IEvent
{
    /// <summary>Unique event identifier.</summary>
    Guid EventId { get; }

    /// <summary>When the event occurred.</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>Source component that raised the event.</summary>
    string Source { get; }
}

/// <summary>
/// Base record for events with common properties.
/// </summary>
public abstract record EventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = string.Empty;
}
```

### Event Categories

```csharp
namespace OpenFork.Core.Events;

// ═══════════════════════════════════════════════════════════════
// SESSION EVENTS
// ═══════════════════════════════════════════════════════════════

public record SessionCreatedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; init; } = string.Empty;
}

public record SessionUpdatedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public string? OldName { get; init; }
    public string? NewName { get; init; }
    public Guid? OldAgentId { get; init; }
    public Guid? NewAgentId { get; init; }
}

public record SessionDeletedEvent : EventBase
{
    public Guid SessionId { get; init; }
}

public record SessionActivatedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public Guid? PreviousSessionId { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// MESSAGE EVENTS
// ═══════════════════════════════════════════════════════════════

public record MessageCreatedEvent : EventBase
{
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? Preview { get; init; }
}

public record MessageStreamStartedEvent : EventBase
{
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
}

public record MessageStreamChunkEvent : EventBase
{
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
    public string Chunk { get; init; } = string.Empty;
    public int TotalLength { get; init; }
}

public record MessageStreamCompletedEvent : EventBase
{
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
    public string? FinishReason { get; init; }
    public TokenUsage? Tokens { get; init; }
}

public record MessageCompactedEvent : EventBase
{
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
    public string? Summary { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// MESSAGE PART EVENTS
// ═══════════════════════════════════════════════════════════════

public record PartCreatedEvent : EventBase
{
    public MessagePart Part { get; init; } = null!;
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
}

public record PartUpdatedEvent : EventBase
{
    public MessagePart Part { get; init; } = null!;
    public Guid MessageId { get; init; }
    public Guid SessionId { get; init; }
    public string? ChangedProperty { get; init; }
}

public record PartDeletedEvent : EventBase
{
    public Guid PartId { get; init; }
    public Guid MessageId { get; init; }
    public string PartType { get; init; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// TOOL EVENTS
// ═══════════════════════════════════════════════════════════════

public record ToolExecutionStartedEvent : EventBase
{
    public Guid PartId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? Input { get; init; }
    public Guid SessionId { get; init; }
}

public record ToolExecutionProgressEvent : EventBase
{
    public Guid PartId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string Progress { get; init; } = string.Empty;
    public double? PercentComplete { get; init; }
}

public record ToolExecutionCompletedEvent : EventBase
{
    public Guid PartId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public TimeSpan Duration { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// PERMISSION EVENTS
// ═══════════════════════════════════════════════════════════════

public record PermissionRequestedEvent : EventBase
{
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
    public PermissionAction RequestedAction { get; init; }
    public Guid SessionId { get; init; }
}

public record PermissionGrantedEvent : EventBase
{
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
    public PermissionScope Scope { get; init; }
    public Guid SessionId { get; init; }
}

public record PermissionDeniedEvent : EventBase
{
    public string Tool { get; init; } = string.Empty;
    public string Resource { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public Guid SessionId { get; init; }
}

// ═══════════════════════════════════════════════════════════════
// SUBAGENT EVENTS
// ═══════════════════════════════════════════════════════════════

public record SubSessionCreatedEvent : EventBase
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string AgentType { get; init; } = string.Empty;
    public string? Prompt { get; init; }
}

public record SubSessionStatusChangedEvent : EventBase
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public SubSessionStatus OldStatus { get; init; }
    public SubSessionStatus NewStatus { get; init; }
}

public record SubSessionProgressEvent : EventBase
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string ProgressType { get; init; } = string.Empty;  // text, tool, step
    public string Content { get; init; } = string.Empty;
}

public record SubSessionCompletedEvent : EventBase
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string? Result { get; init; }
}

public record SubSessionFailedEvent : EventBase
{
    public Guid SubSessionId { get; init; }
    public Guid ParentSessionId { get; init; }
    public string Error { get; init; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// AGENT EVENTS
// ═══════════════════════════════════════════════════════════════

public record AgentIterationStartedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public int IterationNumber { get; init; }
    public string AgentName { get; init; } = string.Empty;
}

public record AgentIterationCompletedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public int IterationNumber { get; init; }
    public int ToolCallCount { get; init; }
    public bool HasMoreWork { get; init; }
}

public record AgentMaxIterationsReachedEvent : EventBase
{
    public Guid SessionId { get; init; }
    public int MaxIterations { get; init; }
    public string AgentName { get; init; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════
// SYSTEM EVENTS
// ═══════════════════════════════════════════════════════════════

public record ErrorOccurredEvent : EventBase
{
    public string Component { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? StackTrace { get; init; }
    public string? ExceptionType { get; init; }
    public Guid? SessionId { get; init; }
}

public record WarningRaisedEvent : EventBase
{
    public string Component { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Guid? SessionId { get; init; }
}

public record MetricRecordedEvent : EventBase
{
    public string Name { get; init; } = string.Empty;
    public double Value { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
}
```

---

## Event Bus Implementation

### Interface

```csharp
namespace OpenFork.Core.Events;

/// <summary>
/// Central event bus for pub/sub communication.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IEvent;

    /// <summary>
    /// Subscribe to events of a specific type.
    /// </summary>
    IDisposable Subscribe<T>(Action<T> handler) where T : IEvent;

    /// <summary>
    /// Subscribe to events of a specific type asynchronously.
    /// </summary>
    IDisposable Subscribe<T>(Func<T, Task> handler) where T : IEvent;

    /// <summary>
    /// Subscribe with a filter predicate.
    /// </summary>
    IDisposable Subscribe<T>(Func<T, bool> filter, Action<T> handler) where T : IEvent;

    /// <summary>
    /// Get event stream as IObservable for reactive patterns.
    /// </summary>
    IObservable<T> AsObservable<T>() where T : IEvent;
}
```

### Implementation with Batching

```csharp
namespace OpenFork.Core.Events;

public class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly Channel<(IEvent Event, Type Type)> _eventQueue;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<InMemoryEventBus> _logger;

    // Batching configuration
    private readonly TimeSpan _batchInterval = TimeSpan.FromMilliseconds(16);  // ~60fps
    private readonly int _maxBatchSize = 100;

    public InMemoryEventBus(ILogger<InMemoryEventBus> logger)
    {
        _logger = logger;
        _eventQueue = Channel.CreateUnbounded<(IEvent, Type)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _processingTask = ProcessEventsAsync(_cts.Token);
    }

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IEvent
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        await _eventQueue.Writer.WriteAsync((@event, typeof(T)), ct);
        _logger.LogTrace("Event queued: {Type}", typeof(T).Name);
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : IEvent
    {
        return SubscribeInternal<T>(e => { handler(e); return Task.CompletedTask; });
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : IEvent
    {
        return SubscribeInternal(handler);
    }

    public IDisposable Subscribe<T>(Func<T, bool> filter, Action<T> handler) where T : IEvent
    {
        return SubscribeInternal<T>(e =>
        {
            if (filter(e)) handler(e);
            return Task.CompletedTask;
        });
    }

    public IObservable<T> AsObservable<T>() where T : IEvent
    {
        return Observable.Create<T>(observer =>
        {
            return Subscribe<T>(e => observer.OnNext(e));
        });
    }

    private IDisposable SubscribeInternal<T>(Func<T, Task> handler) where T : IEvent
    {
        var type = typeof(T);
        var handlers = _handlers.GetOrAdd(type, _ => new List<Delegate>());

        lock (handlers)
        {
            handlers.Add(handler);
        }

        _logger.LogDebug("Subscribed to {Type}, total handlers: {Count}", type.Name, handlers.Count);

        return new Subscription(() =>
        {
            lock (handlers)
            {
                handlers.Remove(handler);
            }
            _logger.LogDebug("Unsubscribed from {Type}", type.Name);
        });
    }

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        var batch = new List<(IEvent Event, Type Type)>();
        var timer = new PeriodicTimer(_batchInterval);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Collect batch
                batch.Clear();
                var deadline = DateTimeOffset.UtcNow.Add(_batchInterval);

                while (batch.Count < _maxBatchSize)
                {
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    if (_eventQueue.Reader.TryRead(out var item))
                    {
                        batch.Add(item);
                    }
                    else
                    {
                        // Wait for more events or timeout
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(remaining);

                        try
                        {
                            await _eventQueue.Reader.WaitToReadAsync(cts.Token);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            break;  // Timeout, process batch
                        }
                    }
                }

                // Dispatch batch
                if (batch.Count > 0)
                {
                    await DispatchBatchAsync(batch);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event processing failed");
        }
    }

    private async Task DispatchBatchAsync(List<(IEvent Event, Type Type)> batch)
    {
        foreach (var (evt, type) in batch)
        {
            await DispatchEventAsync(evt, type);
        }
    }

    private async Task DispatchEventAsync(IEvent evt, Type type)
    {
        // Get handlers for exact type and base types
        var typesToCheck = GetTypeHierarchy(type);

        foreach (var t in typesToCheck)
        {
            if (_handlers.TryGetValue(t, out var handlers))
            {
                List<Delegate> handlersCopy;
                lock (handlers)
                {
                    handlersCopy = handlers.ToList();
                }

                foreach (var handler in handlersCopy)
                {
                    try
                    {
                        var task = (Task)handler.DynamicInvoke(evt)!;
                        await task;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Handler failed for {Type}", type.Name);
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> GetTypeHierarchy(Type type)
    {
        yield return type;

        var current = type.BaseType;
        while (current != null && current != typeof(object))
        {
            yield return current;
            current = current.BaseType;
        }

        foreach (var iface in type.GetInterfaces())
        {
            yield return iface;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _eventQueue.Writer.Complete();
        _processingTask.Wait(TimeSpan.FromSeconds(5));
        _cts.Dispose();
    }

    private class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _unsubscribe();
            }
        }
    }
}
```

---

## Event Scoping

### Session-Scoped Events

```csharp
namespace OpenFork.Core.Events;

/// <summary>
/// Event bus that filters events by session scope.
/// </summary>
public interface ISessionEventBus
{
    Task PublishAsync<T>(Guid sessionId, T @event, CancellationToken ct = default)
        where T : IEvent;

    IDisposable Subscribe<T>(Guid sessionId, Action<T> handler)
        where T : IEvent;

    IDisposable SubscribeAll<T>(Action<(Guid SessionId, T Event)> handler)
        where T : IEvent;
}

public class SessionEventBus : ISessionEventBus
{
    private readonly IEventBus _eventBus;
    private readonly ConcurrentDictionary<Guid, List<IDisposable>> _sessionSubscriptions = new();

    public SessionEventBus(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public Task PublishAsync<T>(Guid sessionId, T @event, CancellationToken ct = default)
        where T : IEvent
    {
        // Wrap in session envelope
        var envelope = new SessionEventEnvelope<T>
        {
            SessionId = sessionId,
            InnerEvent = @event
        };

        return _eventBus.PublishAsync(envelope, ct);
    }

    public IDisposable Subscribe<T>(Guid sessionId, Action<T> handler)
        where T : IEvent
    {
        var subscription = _eventBus.Subscribe<SessionEventEnvelope<T>>(
            env => env.SessionId == sessionId,
            env => handler(env.InnerEvent));

        // Track for cleanup
        var subscriptions = _sessionSubscriptions.GetOrAdd(sessionId, _ => new List<IDisposable>());
        lock (subscriptions)
        {
            subscriptions.Add(subscription);
        }

        return subscription;
    }

    public IDisposable SubscribeAll<T>(Action<(Guid SessionId, T Event)> handler)
        where T : IEvent
    {
        return _eventBus.Subscribe<SessionEventEnvelope<T>>(
            env => handler((env.SessionId, env.InnerEvent)));
    }

    public void CleanupSession(Guid sessionId)
    {
        if (_sessionSubscriptions.TryRemove(sessionId, out var subscriptions))
        {
            foreach (var sub in subscriptions)
            {
                sub.Dispose();
            }
        }
    }

    private record SessionEventEnvelope<T> : EventBase where T : IEvent
    {
        public Guid SessionId { get; init; }
        public T InnerEvent { get; init; } = default!;
    }
}
```

---

## Integration Points

### ChatService Integration

```csharp
public class ChatService
{
    private readonly IEventBus _eventBus;

    public async Task<string> RunAsync(Session session, string userInput, CancellationToken ct)
    {
        // Publish message created
        await _eventBus.PublishAsync(new MessageCreatedEvent
        {
            Source = nameof(ChatService),
            MessageId = userMessage.Id,
            SessionId = session.Id,
            Role = "user",
            Preview = userInput.Truncate(100)
        }, ct);

        // Start streaming
        await _eventBus.PublishAsync(new MessageStreamStartedEvent
        {
            Source = nameof(ChatService),
            MessageId = assistantMessage.Id,
            SessionId = session.Id
        }, ct);

        // Stream chunks
        await foreach (var chunk in response.StreamAsync(ct))
        {
            await _eventBus.PublishAsync(new MessageStreamChunkEvent
            {
                Source = nameof(ChatService),
                MessageId = assistantMessage.Id,
                SessionId = session.Id,
                Chunk = chunk.Content,
                TotalLength = totalLength
            }, ct);
        }

        // Complete
        await _eventBus.PublishAsync(new MessageStreamCompletedEvent
        {
            Source = nameof(ChatService),
            MessageId = assistantMessage.Id,
            SessionId = session.Id,
            FinishReason = response.FinishReason,
            Tokens = response.Usage
        }, ct);
    }
}
```

### TUI Integration

```csharp
public partial class ConsoleApp
{
    private readonly IEventBus _eventBus;
    private readonly List<IDisposable> _subscriptions = new();

    private void SetupEventSubscriptions()
    {
        // Stream chunks → update display
        _subscriptions.Add(_eventBus.Subscribe<MessageStreamChunkEvent>(evt =>
        {
            Application.Invoke(() =>
            {
                AppendToStreamingOutput(evt.Chunk);
            });
        }));

        // Tool started → show spinner
        _subscriptions.Add(_eventBus.Subscribe<ToolExecutionStartedEvent>(evt =>
        {
            Application.Invoke(() =>
            {
                ShowToolSpinner(evt.ToolName);
            });
        }));

        // Tool completed → update display
        _subscriptions.Add(_eventBus.Subscribe<ToolExecutionCompletedEvent>(evt =>
        {
            Application.Invoke(() =>
            {
                HideToolSpinner();
                RenderToolResult(evt);
            });
        }));

        // Permission requested → show prompt
        _subscriptions.Add(_eventBus.Subscribe<PermissionRequestedEvent>(async evt =>
        {
            await Application.InvokeAsync(async () =>
            {
                await ShowPermissionPrompt(evt);
            });
        }));

        // Error → show notification
        _subscriptions.Add(_eventBus.Subscribe<ErrorOccurredEvent>(evt =>
        {
            Application.Invoke(() =>
            {
                ShowErrorNotification(evt.Message);
            });
        }));
    }

    protected override void Dispose(bool disposing)
    {
        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        base.Dispose(disposing);
    }
}
```

### Metrics/Observability Integration

```csharp
public class MetricsEventHandler : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IMetricsCollector _metrics;
    private readonly List<IDisposable> _subscriptions = new();

    public MetricsEventHandler(IEventBus eventBus, IMetricsCollector metrics)
    {
        _eventBus = eventBus;
        _metrics = metrics;

        // Track tool execution times
        _subscriptions.Add(_eventBus.Subscribe<ToolExecutionCompletedEvent>(evt =>
        {
            _metrics.RecordHistogram("tool_execution_duration_ms",
                evt.Duration.TotalMilliseconds,
                new Dictionary<string, string>
                {
                    ["tool"] = evt.ToolName,
                    ["success"] = evt.Success.ToString()
                });
        }));

        // Track message token usage
        _subscriptions.Add(_eventBus.Subscribe<MessageStreamCompletedEvent>(evt =>
        {
            if (evt.Tokens != null)
            {
                _metrics.RecordGauge("message_tokens_input", evt.Tokens.Input);
                _metrics.RecordGauge("message_tokens_output", evt.Tokens.Output);
            }
        }));

        // Track errors
        _subscriptions.Add(_eventBus.Subscribe<ErrorOccurredEvent>(evt =>
        {
            _metrics.IncrementCounter("errors_total",
                new Dictionary<string, string>
                {
                    ["component"] = evt.Component,
                    ["type"] = evt.ExceptionType ?? "unknown"
                });
        }));
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
    }
}
```

---

## Event Persistence (Audit Log)

```csharp
namespace OpenFork.Core.Events;

public interface IEventStore
{
    Task StoreAsync(IEvent @event, CancellationToken ct = default);
    Task<IReadOnlyList<IEvent>> GetEventsAsync(
        Guid? sessionId = null,
        DateTimeOffset? since = null,
        int? limit = null,
        CancellationToken ct = default);
}

public class SqliteEventStore : IEventStore, IDisposable
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IEventBus _eventBus;
    private readonly IDisposable _subscription;
    private readonly HashSet<Type> _persistedEventTypes;

    public SqliteEventStore(
        SqliteConnectionFactory connectionFactory,
        IEventBus eventBus)
    {
        _connectionFactory = connectionFactory;
        _eventBus = eventBus;

        // Define which events to persist
        _persistedEventTypes = new HashSet<Type>
        {
            typeof(MessageCreatedEvent),
            typeof(ToolExecutionCompletedEvent),
            typeof(PermissionGrantedEvent),
            typeof(PermissionDeniedEvent),
            typeof(ErrorOccurredEvent)
        };

        // Subscribe to persist events
        _subscription = _eventBus.Subscribe<IEvent>(async evt =>
        {
            if (_persistedEventTypes.Contains(evt.GetType()))
            {
                await StoreAsync(evt);
            }
        });
    }

    public async Task StoreAsync(IEvent @event, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var sql = """
            INSERT INTO EventLog (
                EventId, EventType, Source, Timestamp, SessionId, DataJson
            ) VALUES (
                @EventId, @EventType, @Source, @Timestamp, @SessionId, @DataJson
            )
            """;

        // Extract SessionId if available
        Guid? sessionId = @event.GetType().GetProperty("SessionId")?.GetValue(@event) as Guid?;

        await connection.ExecuteAsync(sql, new
        {
            @event.EventId,
            EventType = @event.GetType().Name,
            @event.Source,
            @event.Timestamp,
            SessionId = sessionId,
            DataJson = JsonSerializer.Serialize(@event, @event.GetType())
        });
    }

    public void Dispose() => _subscription.Dispose();
}
```

---

## Database Schema

```sql
-- Event log table
CREATE TABLE IF NOT EXISTS EventLog (
    EventId TEXT PRIMARY KEY,
    EventType TEXT NOT NULL,
    Source TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    SessionId TEXT,
    DataJson TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_eventlog_type ON EventLog(EventType);
CREATE INDEX IF NOT EXISTS idx_eventlog_session ON EventLog(SessionId);
CREATE INDEX IF NOT EXISTS idx_eventlog_time ON EventLog(Timestamp);
```

---

## Testing

```csharp
public class EventBusTests
{
    [Fact]
    public async Task PublishAsync_NotifiesSubscribers()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var received = new TaskCompletionSource<MessageCreatedEvent>();

        bus.Subscribe<MessageCreatedEvent>(evt => received.SetResult(evt));

        await bus.PublishAsync(new MessageCreatedEvent
        {
            MessageId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            Role = "user"
        });

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("user", result.Role);
    }

    [Fact]
    public async Task Subscribe_WithFilter_OnlyReceivesMatching()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var targetSession = Guid.NewGuid();
        var received = new List<MessageCreatedEvent>();

        bus.Subscribe<MessageCreatedEvent>(
            filter: evt => evt.SessionId == targetSession,
            handler: evt => received.Add(evt));

        await bus.PublishAsync(new MessageCreatedEvent { SessionId = Guid.NewGuid() });
        await bus.PublishAsync(new MessageCreatedEvent { SessionId = targetSession });
        await bus.PublishAsync(new MessageCreatedEvent { SessionId = Guid.NewGuid() });

        await Task.Delay(50);  // Allow batch processing

        Assert.Single(received);
        Assert.Equal(targetSession, received[0].SessionId);
    }

    [Fact]
    public void Unsubscribe_StopsReceiving()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var count = 0;

        var subscription = bus.Subscribe<MessageCreatedEvent>(_ => count++);
        bus.PublishAsync(new MessageCreatedEvent());

        subscription.Dispose();

        bus.PublishAsync(new MessageCreatedEvent());

        Assert.Equal(1, count);
    }
}
```

---

## Performance Considerations

1. **Event Batching**: 16ms batch interval balances responsiveness with efficiency
2. **Async Handlers**: Non-blocking handler execution
3. **Type Caching**: Handler lookup cached per type
4. **Memory Pressure**: Unbounded queue with monitoring
5. **Subscription Cleanup**: Proper disposal prevents leaks

---

## Migration Path

1. Add IEventBus to DI as singleton
2. Inject into ChatService, ToolRegistry, etc.
3. Add event publishing at key points
4. Subscribe TUI to relevant events
5. Add metrics collection
6. Optional: Enable event persistence
