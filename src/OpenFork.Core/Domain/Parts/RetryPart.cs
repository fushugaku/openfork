namespace OpenFork.Core.Domain.Parts;

/// <summary>
/// Retry marker for failed operations.
/// Records retry attempts and their context.
/// </summary>
public class RetryPart : MessagePart
{
    public override string Type => "retry";

    /// <summary>Which attempt this is (1-based).</summary>
    public int AttemptNumber { get; set; }

    /// <summary>Reason for the retry.</summary>
    public string? Reason { get; set; }

    /// <summary>Original error that triggered the retry.</summary>
    public string? OriginalError { get; set; }

    /// <summary>Delay before the retry.</summary>
    public TimeSpan? DelayBefore { get; set; }
}
