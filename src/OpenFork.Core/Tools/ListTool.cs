using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class ListTool : ITool
{
    private const int Limit = 100;

    private static readonly HashSet<string> DefaultIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__", ".git", "dist", "build", "target", "vendor",
        "bin", "obj", ".idea", ".vscode", ".zig-cache", "zig-out", ".coverage",
        "coverage", "tmp", "temp", ".cache", "cache", "logs", ".venv", "venv", "env"
    };

    public string Name => "list";

    public string Description => PromptLoader.Load("list",
        "Lists files and directories in a given path with tree view output.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "The path to the directory to list. Defaults to working directory."
            },
            ignore = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of glob patterns to ignore"
            }
        },
        required = Array.Empty<string>()
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<ListArgs>(arguments, JsonHelper.Options);

            var searchPath = args?.Path;
            if (string.IsNullOrEmpty(searchPath))
                searchPath = context.WorkingDirectory;
            else if (!Path.IsPathRooted(searchPath))
                searchPath = Path.Combine(context.WorkingDirectory, searchPath);

            if (!Directory.Exists(searchPath))
                return new ToolResult(false, $"Directory not found: {searchPath}");

            var ignorePatterns = new HashSet<string>(DefaultIgnore, StringComparer.OrdinalIgnoreCase);
            if (args?.Ignore != null)
            {
                foreach (var pattern in args.Ignore)
                    ignorePatterns.Add(pattern);
            }

            var files = new List<string>();
            var truncated = false;

            await Task.Run(() =>
            {
                foreach (var file in EnumerateFilesRecursive(searchPath, ignorePatterns))
                {
                    if (files.Count >= Limit)
                    {
                        truncated = true;
                        break;
                    }
                    files.Add(Path.GetRelativePath(searchPath, file));
                }
            });

            var output = BuildTreeOutput(searchPath, files, truncated);
            return new ToolResult(true, output);
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error listing directory: {ex.Message}");
        }
    }

    private static string BuildTreeOutput(string rootPath, List<string> files, bool truncated)
    {
        var dirs = new HashSet<string>();
        var filesByDir = new Dictionary<string, List<string>>();

        foreach (var file in files)
        {
            var dir = Path.GetDirectoryName(file) ?? ".";
            dir = dir == "" ? "." : dir;
            
            var parts = dir == "." ? Array.Empty<string>() : dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i <= parts.Length; i++)
            {
                var dirPath = i == 0 ? "." : string.Join("/", parts.Take(i));
                dirs.Add(dirPath);
            }

            if (!filesByDir.ContainsKey(dir))
                filesByDir[dir] = new List<string>();
            filesByDir[dir].Add(Path.GetFileName(file));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{rootPath}/");
        RenderDir(sb, ".", 0, dirs, filesByDir);

        if (truncated)
        {
            sb.AppendLine();
            sb.AppendLine($"(Results truncated at {Limit} files. Use a more specific path.)");
        }

        return sb.ToString();
    }

    private static void RenderDir(StringBuilder sb, string dirPath, int depth, 
        HashSet<string> dirs, Dictionary<string, List<string>> filesByDir)
    {
        var indent = new string(' ', depth * 2);

        if (depth > 0)
        {
            var dirName = Path.GetFileName(dirPath);
            sb.AppendLine($"{indent}{dirName}/");
        }

        var childIndent = new string(' ', (depth + 1) * 2);

        var children = dirs
            .Where(d => GetParentDir(d) == dirPath && d != dirPath)
            .OrderBy(d => d)
            .ToList();

        foreach (var child in children)
        {
            RenderDir(sb, child, depth + 1, dirs, filesByDir);
        }

        if (filesByDir.TryGetValue(dirPath, out var dirFiles))
        {
            foreach (var file in dirFiles.OrderBy(f => f))
            {
                sb.AppendLine($"{childIndent}{file}");
            }
        }
    }

    private static string GetParentDir(string path)
    {
        if (path == ".") return "";
        var parent = Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(parent) ? "." : parent.Replace('\\', '/');
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string directory, HashSet<string> ignorePatterns)
    {
        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();

            string[] files;
            try { files = Directory.GetFiles(currentDir); }
            catch { continue; }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.StartsWith('.'))
                    yield return file;
            }

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(currentDir); }
            catch { continue; }

            foreach (var subdir in subdirs)
            {
                var dirName = Path.GetFileName(subdir);
                if (!ignorePatterns.Contains(dirName) && !dirName.StartsWith('.'))
                    stack.Push(subdir);
            }
        }
    }

    private record ListArgs(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("ignore")] string[]? Ignore
    );
}
