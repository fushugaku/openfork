using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Hooks;

/// <summary>
/// Hook that sends data to a webhook URL.
/// </summary>
public class WebhookHook : IHook
{
    private readonly HookConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookHook> _logger;

    public string Id => _config.Id;
    public string Name => _config.Name;
    public HookTrigger Trigger => _config.Trigger;
    public int Priority => _config.Priority;
    public bool Enabled => _config.Enabled;

    public WebhookHook(
        HookConfig config,
        HttpClient httpClient,
        ILogger<WebhookHook> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.WebhookUrl))
        {
            return HookResult.Fail("No webhook URL configured");
        }

        var payload = new
        {
            trigger = Trigger.ToString(),
            timestamp = DateTimeOffset.UtcNow,
            sessionId = context.SessionId,
            messageId = context.MessageId,
            data = new
            {
                toolName = context.ToolName,
                filePath = context.FilePath,
                command = context.Command,
                exitCode = context.ExitCode,
                error = context.ErrorMessage,
                duration = context.Duration?.TotalMilliseconds
            }
        };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_config.Timeout);

            var response = await _httpClient.PostAsJsonAsync(
                _config.WebhookUrl,
                payload,
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook {Name} returned {Status}",
                    Name, response.StatusCode);

                if (_config.ContinueOnError)
                    return HookResult.Ok();

                return HookResult.Fail($"Webhook returned {response.StatusCode}");
            }

            return HookResult.Ok();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Webhook hook {Name} timed out", Name);
            return _config.ContinueOnError ? HookResult.Ok() : HookResult.Fail("Hook timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook hook {Name} failed", Name);
            return _config.ContinueOnError ? HookResult.Ok() : HookResult.Fail(ex.Message);
        }
    }
}
