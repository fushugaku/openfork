namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Base class for all message part types.
/// Message parts provide fine-grained structure for conversation content.
/// </summary>
public abstract class MessagePart
{
    /// <summary>Unique identifier for this part.</summary>
    public Guid Id { get; set; }

    /// <summary>The session this part belongs to.</summary>
    public long SessionId { get; set; }

    /// <summary>The message this part belongs to.</summary>
    public long MessageId { get; set; }

    /// <summary>Order within the message (0-based).</summary>
    public int OrderIndex { get; set; }

    /// <summary>Type discriminator for polymorphic storage.</summary>
    public abstract string Type { get; }

    /// <summary>When this part was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this part was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
