namespace OpenFork.Core.Services;

/// <summary>
/// Handles retry logic with exponential backoff for provider errors.
/// Based on OpenCode's retry implementation.
/// </summary>
public static class RetryHelper
{
    public const int InitialDelayMs = 2000;
    public const double BackoffFactor = 2.0;
    public const int MaxDelayMs = 30000;
    public const int MaxRetries = 5;

    /// <summary>
    /// Determines if an exception is retryable.
    /// </summary>
    public static bool IsRetryable(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        // Network/connection errors
        if (message.Contains("connection") ||
            message.Contains("timeout") ||
            message.Contains("econnreset") ||
            message.Contains("network"))
            return true;

        // Rate limiting
        if (message.Contains("rate") ||
            message.Contains("too many requests") ||
            message.Contains("429") ||
            message.Contains("throttl"))
            return true;

        // Server errors
        if (message.Contains("500") ||
            message.Contains("502") ||
            message.Contains("503") ||
            message.Contains("504") ||
            message.Contains("server error") ||
            message.Contains("overloaded") ||
            message.Contains("unavailable"))
            return true;

        // Provider-specific retryable errors
        if (message.Contains("exhausted") ||
            message.Contains("capacity") ||
            message.Contains("ended prematurely"))
            return true;

        return false;
    }

    /// <summary>
    /// Calculates delay for the given retry attempt using exponential backoff.
    /// </summary>
    public static int GetDelayMs(int attempt)
    {
        var delay = (int)(InitialDelayMs * Math.Pow(BackoffFactor, attempt - 1));
        return Math.Min(delay, MaxDelayMs);
    }

    /// <summary>
    /// Gets a user-friendly message describing why we're retrying.
    /// </summary>
    public static string GetRetryReason(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("rate") || message.Contains("429") || message.Contains("too many"))
            return "Rate limited";
        if (message.Contains("overloaded") || message.Contains("capacity"))
            return "Provider overloaded";
        if (message.Contains("timeout"))
            return "Request timeout";
        if (message.Contains("connection") || message.Contains("network"))
            return "Connection error";
        if (message.Contains("ended prematurely"))
            return "Response interrupted";
        if (message.Contains("500") || message.Contains("502") ||
            message.Contains("503") || message.Contains("504"))
            return "Server error";

        return "Temporary error";
    }

    /// <summary>
    /// Detects if response was cut off prematurely (incomplete).
    /// </summary>
    public static bool IsResponseIncomplete(string? finishReason, string? content)
    {
        // Check finish reason
        if (finishReason != null)
        {
            var reason = finishReason.ToLowerInvariant();
            if (reason == "length" || reason == "max_tokens" || reason == "max_output_tokens")
                return true;
        }

        // Check for signs of truncation in content
        if (!string.IsNullOrEmpty(content))
        {
            // Incomplete code blocks
            var codeBlockStarts = content.Split("```").Length - 1;
            if (codeBlockStarts % 2 != 0)
                return true;

            // Incomplete JSON (rough heuristic, may have false positives)
            if ((content.Contains("{") && !content.TrimEnd().EndsWith("}")) ||
                (content.Contains("[") && !content.TrimEnd().EndsWith("]")))
            {
                return true;
            }
        }

        return false;
    }
}
