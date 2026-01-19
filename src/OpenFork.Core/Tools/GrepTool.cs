using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenFork.Core.Tools;

public class GrepTool : ITool
{
    private const int Limit = 100;
    private const int MaxLineLength = 2000;

    private static readonly HashSet<string> IgnoreDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__", ".git", "dist", "build", "target", "vendor",
        "bin", "obj", ".idea", ".vscode", ".zig-cache", "zig-out", ".coverage",
        "coverage", "tmp", "temp", ".cache", "cache", "logs", ".venv", "venv", "env"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".exe", ".dll", ".so", ".class", ".jar", ".war",
        ".7z", ".bin", ".dat", ".obj", ".o", ".a", ".lib", ".wasm", ".pyc", ".pyo",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp",
        ".mp3", ".mp4", ".avi", ".mov", ".mkv", ".wav", ".flac",
        ".pdf", ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    public string Name => "grep";

    public string Description => PromptLoader.Load("grep",
        "Fast content search tool. Searches file contents using regular expressions.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
                type = "string",
                description = "The regex pattern to search for in file contents"
            },
            path = new
            {
                type = "string",
                description = "The directory to search in. Defaults to working directory."
            },
            include = new
            {
                type = "string",
                description = "File pattern to include in the search (e.g. \"*.cs\", \"*.{ts,tsx}\")"
            }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<GrepArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.Pattern))
                return new ToolResult(false, "Missing required parameter: pattern");

            var searchPath = args.Path;
            if (string.IsNullOrEmpty(searchPath))
                searchPath = context.WorkingDirectory;
            else if (!Path.IsPathRooted(searchPath))
                searchPath = Path.Combine(context.WorkingDirectory, searchPath);

            if (!Directory.Exists(searchPath))
                return new ToolResult(false, $"Directory not found: {searchPath}");

            Regex searchRegex;
            try
            {
                searchRegex = new Regex(args.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                return new ToolResult(false, $"Invalid regex pattern: {ex.Message}");
            }

            Regex? includeRegex = null;
            if (!string.IsNullOrEmpty(args.Include))
                includeRegex = GlobHelper.SimpleGlobToRegex(args.Include);

            var matches = new List<(string path, DateTime mtime, int lineNum, string lineText)>();
            var truncated = false;

            await Task.Run(() =>
            {
                foreach (var file in EnumerateFilesRecursive(searchPath))
                {
                    if (matches.Count >= Limit)
                    {
                        truncated = true;
                        break;
                    }

                    var ext = Path.GetExtension(file);
                    if (BinaryExtensions.Contains(ext))
                        continue;

                    if (includeRegex != null)
                    {
                        var fileName = Path.GetFileName(file);
                        if (!includeRegex.IsMatch(fileName))
                            continue;
                    }

                    try
                    {
                        var mtime = File.GetLastWriteTimeUtc(file);
                        var lines = File.ReadLines(file);
                        var lineNum = 0;

                        foreach (var line in lines)
                        {
                            lineNum++;
                            if (matches.Count >= Limit)
                            {
                                truncated = true;
                                break;
                            }

                            if (searchRegex.IsMatch(line))
                            {
                                matches.Add((file, mtime, lineNum, line));
                            }
                        }
                    }
                    catch (IOException)
                    {
                        // Skip files that can't be read (locked, deleted, etc.)
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip files without read permission
                    }
                }
            });

            if (matches.Count == 0)
                return new ToolResult(true, "No files found");

            matches.Sort((a, b) => b.mtime.CompareTo(a.mtime));

            var output = new List<string> { $"Found {matches.Count} matches" };
            var currentFile = "";

            foreach (var match in matches)
            {
                if (currentFile != match.path)
                {
                    if (currentFile != "") output.Add("");
                    currentFile = match.path;
                    output.Add($"{match.path}:");
                }

                var truncatedLine = match.lineText.Length > MaxLineLength
                    ? match.lineText[..MaxLineLength] + "..."
                    : match.lineText;
                output.Add($"  Line {match.lineNum}: {truncatedLine}");
            }

            if (truncated)
            {
                output.Add("");
                output.Add("(Results are truncated. Consider using a more specific path or pattern.)");
            }

            return new ToolResult(true, string.Join("\n", output));
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error searching: {ex.Message}");
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
            try { files = Directory.GetFiles(currentDir); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var file in files)
                yield return file;

            string[] subdirs;
            try { subdirs = Directory.GetDirectories(currentDir); }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var subdir in subdirs)
            {
                var dirName = Path.GetFileName(subdir);
                if (!IgnoreDirs.Contains(dirName) && !dirName.StartsWith('.'))
                    stack.Push(subdir);
            }
        }
    }

    private record GrepArgs(
        [property: JsonPropertyName("pattern")] string? Pattern,
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("include")] string? Include
    );
}
