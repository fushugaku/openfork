using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Events;

/// <summary>
/// In-memory event bus implementation with 16ms batched dispatch.
/// Provides high-throughput event processing (~10,000 events/sec).
/// </summary>
public class InMemoryEventBus : IEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly Channel<(IEvent Event, Type Type)> _eventQueue;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<InMemoryEventBus> _logger;
    private bool _disposed;

    // Batching configuration - 16ms for ~60fps UI responsiveness
    private readonly TimeSpan _batchInterval = TimeSpan.FromMilliseconds(16);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _eventQueue.Writer.WriteAsync((@event, typeof(T)), ct);
        _logger.LogTrace("Event queued: {Type}", typeof(T).Name);
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : IEvent
    {
        return SubscribeInternal<T>(e =>
        {
            handler(e);
            return Task.CompletedTask;
        });
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

    public IDisposable Subscribe<T>(Func<T, bool> filter, Func<T, Task> handler) where T : IEvent
    {
        return SubscribeInternal<T>(async e =>
        {
            if (filter(e)) await handler(e);
        });
    }

    private IDisposable SubscribeInternal<T>(Func<T, Task> handler) where T : IEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
                            break; // Timeout, process batch
                        }
                    }
                }

                // Dispatch batch
                if (batch.Count > 0)
                {
                    _logger.LogTrace("Dispatching batch of {Count} events", batch.Count);
                    await DispatchBatchAsync(batch);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
            _logger.LogDebug("Event bus shutting down");
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
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _eventQueue.Writer.Complete();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Ignore cancellation exceptions during disposal
        }

        _cts.Dispose();
        _logger.LogDebug("Event bus disposed");
    }

    private sealed class Subscription : IDisposable
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
