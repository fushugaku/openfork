using System.Collections.Concurrent;

namespace OpenFork.Core.Services;

/// <summary>
/// In-memory user prompt service that waits for responses via events.
/// The TUI should subscribe to PromptRequested and call ProvideResponse.
/// </summary>
public class InMemoryUserPromptService : IUserPromptService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PromptResponse>> _pendingPrompts = new();

    /// <summary>
    /// Event raised when a prompt is waiting for user input.
    /// The TUI should handle this and call ProvideResponse.
    /// </summary>
    public event EventHandler<UserPromptRequest>? PromptRequested;

    public async Task<PromptResponse> PromptAsync(
        UserPromptRequest request,
        CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<PromptResponse>();

        // Register cancellation
        using var registration = ct.Register(() =>
        {
            tcs.TrySetResult(new PromptResponse
            {
                Key = request.DefaultOption ?? "n",
                Cancelled = true
            });
        });

        // Add to pending prompts
        _pendingPrompts[request.RequestId] = tcs;

        try
        {
            // Raise the event for the TUI to handle
            PromptRequested?.Invoke(this, request);

            // Wait for response or timeout
            if (request.Timeout.HasValue)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(request.Timeout.Value);

                try
                {
                    return await tcs.Task.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return new PromptResponse
                    {
                        Key = request.DefaultOption ?? "n",
                        TimedOut = true
                    };
                }
            }
            else
            {
                return await tcs.Task.WaitAsync(ct);
            }
        }
        finally
        {
            _pendingPrompts.TryRemove(request.RequestId, out _);
        }
    }

    /// <summary>
    /// Provide a response to a pending prompt.
    /// Call this from the TUI when the user makes a selection.
    /// </summary>
    public void ProvideResponse(string requestId, PromptResponse response)
    {
        if (_pendingPrompts.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetResult(response);
        }
    }

    /// <summary>
    /// Cancel a pending prompt.
    /// </summary>
    public void CancelPrompt(string requestId)
    {
        if (_pendingPrompts.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetResult(new PromptResponse { Cancelled = true });
        }
    }

    /// <summary>
    /// Check if there are any pending prompts.
    /// </summary>
    public bool HasPendingPrompts => !_pendingPrompts.IsEmpty;

    /// <summary>
    /// Get count of pending prompts.
    /// </summary>
    public int PendingPromptCount => _pendingPrompts.Count;
}
