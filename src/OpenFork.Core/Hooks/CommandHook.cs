using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OpenFork.Core.Hooks;

/// <summary>
/// Hook that executes a shell command.
/// </summary>
public class CommandHook : IHook
{
    private readonly HookConfig _config;
    private readonly ILogger<CommandHook> _logger;

    public string Id => _config.Id;
    public string Name => _config.Name;
    public HookTrigger Trigger => _config.Trigger;
    public int Priority => _config.Priority;
    public bool Enabled => _config.Enabled;

    public CommandHook(HookConfig config, ILogger<CommandHook> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.Command))
        {
            return HookResult.Fail("No command configured");
        }

        // Build environment with context data
        var env = new Dictionary<string, string>
        {
            ["HOOK_TRIGGER"] = Trigger.ToString(),
            ["HOOK_SESSION_ID"] = context.SessionId?.ToString() ?? "",
            ["HOOK_TOOL_NAME"] = context.ToolName ?? "",
            ["HOOK_FILE_PATH"] = context.FilePath ?? "",
            ["HOOK_COMMAND"] = context.Command ?? "",
            ["HOOK_ERROR"] = context.ErrorMessage ?? ""
        };

        // Pass context as JSON via stdin
        var contextJson = JsonSerializer.Serialize(new
        {
            trigger = Trigger.ToString(),
            sessionId = context.SessionId,
            messageId = context.MessageId,
            toolName = context.ToolName,
            filePath = context.FilePath,
            command = context.Command,
            exitCode = context.ExitCode,
            errorMessage = context.ErrorMessage,
            duration = context.Duration?.TotalMilliseconds
        });

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_config.Timeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = GetShellPath(),
                Arguments = GetShellArgs(_config.Command),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var (key, value) in env)
            {
                startInfo.EnvironmentVariables[key] = value;
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            await process.StandardInput.WriteLineAsync(contextJson);
            process.StandardInput.Close();

            await process.WaitForExitAsync(cts.Token);

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Command hook {Name} exited with code {Code}: {Error}",
                    Name, process.ExitCode, error);

                if (_config.ContinueOnError)
                {
                    return HookResult.Ok();
                }

                return HookResult.Fail(error);
            }

            // Parse output for special directives
            if (output.Contains("HOOK_CANCEL:"))
            {
                var reason = output.Split("HOOK_CANCEL:").LastOrDefault()?.Trim();
                return HookResult.Cancel(reason ?? "Cancelled by hook");
            }

            return HookResult.Ok();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Command hook {Name} timed out", Name);
            return _config.ContinueOnError ? HookResult.Ok() : HookResult.Fail("Hook timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command hook {Name} failed", Name);
            return _config.ContinueOnError ? HookResult.Ok() : HookResult.Fail(ex.Message);
        }
    }

    private static string GetShellPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return "cmd.exe";
        }
        return "/bin/bash";
    }

    private static string GetShellArgs(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"/c {command}";
        }
        return $"-c \"{command.Replace("\"", "\\\"")}\"";
    }
}
