using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class BashTool : ITool
{
    private const int DefaultTimeoutMs = 120_000;
    private const int MaxOutputLines = 2000;
    private const int MaxOutputBytes = 50 * 1024;

    public string Name => "bash";

    public string Description => PromptLoader.Load("bash",
        "Execute a shell command in the terminal and return the output. Use for running builds, tests, git commands, package managers, etc.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            command = new
            {
                type = "string",
                description = "The shell command to execute"
            },
            timeout = new
            {
                type = "integer",
                description = "Optional timeout in milliseconds (default: 120000)"
            },
            workdir = new
            {
                type = "string",
                description = "The working directory to run the command in. Defaults to current directory. Use this instead of 'cd' commands."
            },
            description = new
            {
                type = "string",
                description = "Clear, concise description of what this command does in 5-10 words"
            }
        },
        required = new[] { "command" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<BashArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.Command))
                return new ToolResult(false, "Missing required parameter: command");

            var timeout = args.Timeout ?? DefaultTimeoutMs;
            if (timeout < 0)
                return new ToolResult(false, $"Invalid timeout value: {timeout}. Timeout must be a positive number.");

            var workdir = args.Workdir;
            if (!string.IsNullOrEmpty(workdir) && !Path.IsPathRooted(workdir))
                workdir = Path.Combine(context.WorkingDirectory, workdir);
            workdir ??= context.WorkingDirectory;

            if (!Directory.Exists(workdir))
                return new ToolResult(false, $"Working directory not found: {workdir}");

            var isWindows = OperatingSystem.IsWindows();
            var shell = isWindows ? "cmd.exe" : GetUnixShell();
            var shellArgs = isWindows 
                ? $"/c {args.Command}" 
                : $"-c \"{args.Command.Replace("\"", "\\\"")}\"";

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var output = new List<string>();
            var totalBytes = 0;
            var truncated = false;

            void AppendOutput(string? line, bool isError)
            {
                if (line == null || truncated) return;
                
                var prefix = isError ? "[stderr] " : "";
                var fullLine = prefix + line;
                var lineBytes = System.Text.Encoding.UTF8.GetByteCount(fullLine);

                if (output.Count >= MaxOutputLines || totalBytes + lineBytes > MaxOutputBytes)
                {
                    truncated = true;
                    return;
                }

                output.Add(fullLine);
                totalBytes += lineBytes;
            }

            process.OutputDataReceived += (_, e) => AppendOutput(e.Data, false);
            process.ErrorDataReceived += (_, e) => AppendOutput(e.Data, true);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit(timeout));

            if (!completed)
            {
                try { process.Kill(true); } catch { }
                var partialOutput = string.Join("\n", output);
                return new ToolResult(false, $"Command timed out after {timeout}ms. Partial output:\n{partialOutput}");
            }

            var result = string.Join("\n", output);
            var exitCode = process.ExitCode;

            if (truncated)
            {
                result += $"\n\n(Output truncated at {MaxOutputLines} lines or {MaxOutputBytes} bytes)";
            }

            if (exitCode == 0)
                return new ToolResult(true, string.IsNullOrEmpty(result) ? "(no output)" : result);
            
            return new ToolResult(false, $"Exit code {exitCode}\n{result}");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error executing command: {ex.Message}");
        }
    }

    private static string GetUnixShell()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
            return shell;
        
        if (File.Exists("/bin/zsh")) return "/bin/zsh";
        if (File.Exists("/bin/bash")) return "/bin/bash";
        return "/bin/sh";
    }

    private record BashArgs(
        [property: JsonPropertyName("command")] string? Command,
        [property: JsonPropertyName("timeout")] int? Timeout,
        [property: JsonPropertyName("workdir")] string? Workdir,
        [property: JsonPropertyName("description")] string? Description
    );
}
