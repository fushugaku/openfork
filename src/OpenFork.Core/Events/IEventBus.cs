namespace OpenFork.Core.Events;

/// <summary>
/// Central event bus for pub/sub communication.
/// Provides decoupled communication between components.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    /// <typeparam name="T">The event type.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : IEvent;

    /// <summary>
    /// Subscribe to events of a specific type.
    /// </summary>
    /// <typeparam name="T">The event type to subscribe to.</typeparam>
    /// <param name="handler">Handler to invoke when event is received.</param>
    /// <returns>Disposable subscription - dispose to unsubscribe.</returns>
    IDisposable Subscribe<T>(Action<T> handler) where T : IEvent;

    /// <summary>
    /// Subscribe to events of a specific type asynchronously.
    /// </summary>
    /// <typeparam name="T">The event type to subscribe to.</typeparam>
    /// <param name="handler">Async handler to invoke when event is received.</param>
    /// <returns>Disposable subscription - dispose to unsubscribe.</returns>
    IDisposable Subscribe<T>(Func<T, Task> handler) where T : IEvent;

    /// <summary>
    /// Subscribe with a filter predicate.
    /// </summary>
    /// <typeparam name="T">The event type to subscribe to.</typeparam>
    /// <param name="filter">Filter predicate - only matching events are delivered.</param>
    /// <param name="handler">Handler to invoke when event matches filter.</param>
    /// <returns>Disposable subscription - dispose to unsubscribe.</returns>
    IDisposable Subscribe<T>(Func<T, bool> filter, Action<T> handler) where T : IEvent;

    /// <summary>
    /// Subscribe with a filter predicate asynchronously.
    /// </summary>
    /// <typeparam name="T">The event type to subscribe to.</typeparam>
    /// <param name="filter">Filter predicate - only matching events are delivered.</param>
    /// <param name="handler">Async handler to invoke when event matches filter.</param>
    /// <returns>Disposable subscription - dispose to unsubscribe.</returns>
    IDisposable Subscribe<T>(Func<T, bool> filter, Func<T, Task> handler) where T : IEvent;
}
