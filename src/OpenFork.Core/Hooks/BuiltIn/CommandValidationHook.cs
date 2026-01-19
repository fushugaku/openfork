namespace OpenFork.Core.Hooks.BuiltIn;

/// <summary>
/// Built-in hook that validates commands for dangerous patterns.
/// </summary>
public class CommandValidationHook : IHook
{
    public string Id => "builtin-command-validation";
    public string Name => "Command Validator";
    public HookTrigger Trigger => HookTrigger.PreCommand;
    public int Priority => 10;
    public bool Enabled => true;

    private static readonly HashSet<string> DangerousPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm -rf /",
        "rm -rf /*",
        ":(){ :|:& };:",  // Fork bomb
        "dd if=/dev/zero of=/dev/sda",
        "dd if=/dev/random of=/dev/sda",
        "mkfs.ext4 /dev/sda",
        "mkfs.ext3 /dev/sda",
        "mkfs.xfs /dev/sda",
        "> /dev/sda",
        "chmod -R 777 /",
        "chown -R root /",
        "mv / /dev/null",
    };

    private static readonly string[] DangerousSubstrings =
    {
        "|rm -rf",
        "&& rm -rf",
        ";rm -rf",
        "$(rm -rf",
        "`rm -rf",
    };

    public Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default)
    {
        var command = context.Command ?? "";

        // Check for exact dangerous command patterns
        foreach (var pattern in DangerousPatterns)
        {
            if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(HookResult.Cancel(
                    $"Dangerous command pattern detected: {pattern}"));
            }
        }

        // Check for dangerous substrings
        foreach (var substring in DangerousSubstrings)
        {
            if (command.Contains(substring, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(HookResult.Cancel(
                    $"Dangerous command pattern detected in pipeline"));
            }
        }

        // Flag sudo commands for awareness
        if (command.TrimStart().StartsWith("sudo ", StringComparison.OrdinalIgnoreCase))
        {
            context.Data["requires_elevation"] = true;
        }

        // Flag commands that could affect system files
        if (IsSystemPath(command))
        {
            context.Data["affects_system_files"] = true;
        }

        return Task.FromResult(HookResult.Ok());
    }

    private static bool IsSystemPath(string command)
    {
        var systemPaths = new[] { "/etc/", "/usr/", "/var/", "/bin/", "/sbin/", "/lib/" };
        return systemPaths.Any(path => command.Contains(path));
    }
}
