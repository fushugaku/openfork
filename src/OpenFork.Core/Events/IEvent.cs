namespace OpenFork.Core.Events;

/// <summary>
/// Base interface for all events in the system.
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
