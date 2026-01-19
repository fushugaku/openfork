namespace OpenFork.Core.Events;

/// <summary>
/// Base record for events with common properties.
/// </summary>
public abstract record EventBase : IEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = string.Empty;
}
