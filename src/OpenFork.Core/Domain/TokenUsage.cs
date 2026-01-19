namespace OpenFork.Core.Domain;

/// <summary>
/// Token usage for a message or response.
/// </summary>
public record TokenUsage
{
    /// <summary>Input tokens used.</summary>
    public int Input { get; init; }

    /// <summary>Output tokens generated.</summary>
    public int Output { get; init; }

    /// <summary>Reasoning tokens used (if model supports).</summary>
    public int Reasoning { get; init; }

    /// <summary>Total tokens used.</summary>
    public int Total => Input + Output + Reasoning;

    /// <summary>Cache usage if applicable.</summary>
    public CacheUsage? Cache { get; init; }
}

/// <summary>
/// Cache token usage.
/// </summary>
public record CacheUsage
{
    /// <summary>Tokens read from cache.</summary>
    public int Read { get; init; }

    /// <summary>Tokens written to cache.</summary>
    public int Write { get; init; }
}

/// <summary>
/// An error that occurred during message processing.
/// </summary>
public record MessageError
{
    /// <summary>Error code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Error message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>When the error occurred.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
