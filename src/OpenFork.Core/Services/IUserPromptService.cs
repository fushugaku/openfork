namespace OpenFork.Core.Services;

/// <summary>
/// Service for prompting users for input during agent execution.
/// </summary>
public interface IUserPromptService
{
    /// <summary>
    /// Prompt the user with options and wait for a response.
    /// </summary>
    Task<PromptResponse> PromptAsync(
        UserPromptRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Event raised when a prompt is waiting for user input.
    /// </summary>
    event EventHandler<UserPromptRequest>? PromptRequested;

    /// <summary>
    /// Provide a response to a pending prompt.
    /// </summary>
    void ProvideResponse(string requestId, PromptResponse response);
}

/// <summary>
/// Request for user prompt.
/// </summary>
public record UserPromptRequest
{
    /// <summary>
    /// Unique identifier for this prompt request.
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Title for the prompt dialog.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Message to display to the user.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Available options for the user to choose from.
    /// </summary>
    public IReadOnlyList<PromptOption> Options { get; init; } = Array.Empty<PromptOption>();

    /// <summary>
    /// Default option key if user times out or cancels.
    /// </summary>
    public string? DefaultOption { get; init; }

    /// <summary>
    /// Optional timeout for the prompt.
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// An option in a user prompt.
/// </summary>
public record PromptOption
{
    /// <summary>
    /// Key to identify this option (e.g., "y", "n", "a").
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Display label for the option.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Optional description for the option.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Response from a user prompt.
/// </summary>
public record PromptResponse
{
    /// <summary>
    /// The key of the selected option.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Whether the prompt timed out.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// Whether the user cancelled.
    /// </summary>
    public bool Cancelled { get; init; }
}
