using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenFork.Core.Tools;

public class GlobTool : ITool
{
    private const int Limit = 100;

    private static readonly HashSet<string> IgnoreDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__", ".git", "dist", "build", "target", "vendor",
        "bin", "obj", ".idea", ".vscode", ".zig-cache", "zig-out", ".coverage",
        "coverage", "tmp", "temp", ".cache", "cache", "logs", ".venv", "venv", "env"
    };

    public string Name => "glob";

    public string Description => PromptLoader.Load("glob",
        "Fast file pattern matching tool. Supports glob patterns like **/*.cs or src/**/*.ts");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
                type = "string",
                description = "The glob pattern to match files against"
            },
            path = new
            {
                type = "string",
                description = "The directory to search in. Defaults to working directory if not specified."
            }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<GlobArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.Pattern))
                return new ToolResult(false, "Missing required parameter: pattern");

            var searchPath = args.Path;
            if (string.IsNullOrEmpty(searchPath))
                searchPath = context.WorkingDirectory;
            else if (!Path.IsPathRooted(searchPath))
                searchPath = Path.Combine(context.WorkingDirectory, searchPath);

            if (!Directory.Exists(searchPath))
                return new ToolResult(false, $"Directory not found: {searchPath}");

            var regex = GlobHelper.GlobToRegex(args.Pattern);
            var files = new List<(string path, DateTime mtime)>();
            var truncated = false;

            await Task.Run(() =>
            {
                foreach (var file in EnumerateFilesRecursive(searchPath))
                {
                    if (files.Count >= Limit)
                    {
                        truncated = true;
                        break;
                    }

                    var relativePath = Path.GetRelativePath(searchPath, file);
                    if (regex.IsMatch(relativePath) || regex.IsMatch(Path.GetFileName(file)))
                    {
                        var mtime = File.GetLastWriteTimeUtc(file);
                        files.Add((file, mtime));
                    }
                }
            });

            files.Sort((a, b) => b.mtime.CompareTo(a.mtime));

            if (files.Count == 0)
                return new ToolResult(true, "No files found");

            var output = new List<string>();
            output.AddRange(files.Select(f => f.path));
            
            if (truncated)
            {
                output.Add("");
                output.Add("(Results are truncated. Consider using a more specific path or pattern.)");
            }

            return new ToolResult(true, string.Join("\n", output));
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error searching files: {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string directory)
    {
        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                var dirName = Path.GetFileName(subdir);
                if (!IgnoreDirs.Contains(dirName) && !dirName.StartsWith('.'))
                    stack.Push(subdir);
            }
        }
    }

    private record GlobArgs(
        [property: JsonPropertyName("pattern")] string? Pattern,
        [property: JsonPropertyName("path")] string? Path
    );
}
