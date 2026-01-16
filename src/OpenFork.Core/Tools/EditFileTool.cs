using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class EditFileTool : ITool
{
    public string Name => "edit";

    public string Description => PromptLoader.Load("edit",
        "Performs exact string replacements in files. Use oldString and newString to make changes.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            filePath = new
            {
                type = "string",
                description = "The absolute path to the file to modify"
            },
            oldString = new
            {
                type = "string",
                description = "The text to replace"
            },
            newString = new
            {
                type = "string",
                description = "The text to replace it with (must be different from oldString)"
            },
            replaceAll = new
            {
                type = "boolean",
                description = "Replace all occurrences of oldString (default false)"
            }
        },
        required = new[] { "filePath", "oldString", "newString" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<EditArgs>(arguments, JsonHelper.Options);
            
            if (string.IsNullOrEmpty(args?.FilePath))
                return new ToolResult(false, "filePath is required");

            if (args.OldString == args.NewString)
                return new ToolResult(false, "oldString and newString must be different");

            var filePath = Path.IsPathRooted(args.FilePath)
                ? args.FilePath
                : Path.Combine(context.WorkingDirectory, args.FilePath);

            if (!context.HasReadFile(filePath))
                return new ToolResult(false, "You must read the file before editing it. Use the read tool first.");

            if (string.IsNullOrEmpty(args.OldString))
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                await File.WriteAllTextAsync(filePath, args.NewString ?? "");
                
                context.FileChangeTracker?.TrackChange(new FileChange
                {
                    FilePath = filePath,
                    IsNew = true,
                    LinesAdded = (args.NewString ?? "").Split('\n').Length,
                    LinesDeleted = 0,
                    NewLineCount = (args.NewString ?? "").Split('\n').Length
                });

                return new ToolResult(true, $"Created new file: {filePath}");
            }

            if (!File.Exists(filePath))
                return new ToolResult(false, $"File not found: {filePath}");

            var content = await File.ReadAllTextAsync(filePath);
            var oldContent = content;

            var newContent = Replace(content, args.OldString, args.NewString ?? "", args.ReplaceAll ?? false);
            
            if (newContent == content)
                return new ToolResult(false, "oldString not found in content");

            await File.WriteAllTextAsync(filePath, newContent);

            var oldLines = oldContent.Split('\n').Length;
            var newLines = newContent.Split('\n').Length;
            
            context.FileChangeTracker?.TrackChange(new FileChange
            {
                FilePath = filePath,
                IsNew = false,
                LinesAdded = Math.Max(0, newLines - oldLines),
                LinesDeleted = Math.Max(0, oldLines - newLines),
                NewLineCount = newLines
            });

            context.MarkFileRead(filePath);

            return new ToolResult(true, "Edit applied successfully.");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error editing file: {ex.Message}");
        }
    }

    private static string Replace(string content, string oldString, string newString, bool replaceAll)
    {
        if (replaceAll)
            return content.Replace(oldString, newString);

        var result = TrySimpleReplace(content, oldString, newString);
        if (result != null) return result;

        result = TryLineTrimmedReplace(content, oldString, newString);
        if (result != null) return result;

        result = TryBlockAnchorReplace(content, oldString, newString);
        if (result != null) return result;

        return content;
    }

    private static string? TrySimpleReplace(string content, string find, string replacement)
    {
        var index = content.IndexOf(find, StringComparison.Ordinal);
        if (index < 0) return null;

        var lastIndex = content.LastIndexOf(find, StringComparison.Ordinal);
        if (index != lastIndex)
            return null;

        return content[..index] + replacement + content[(index + find.Length)..];
    }

    private static string? TryLineTrimmedReplace(string content, string find, string replacement)
    {
        var originalLines = content.Split('\n');
        var searchLines = find.Split('\n');

        if (searchLines.Length > 0 && searchLines[^1] == "")
            searchLines = searchLines[..^1];

        for (int i = 0; i <= originalLines.Length - searchLines.Length; i++)
        {
            var matches = true;
            for (int j = 0; j < searchLines.Length; j++)
            {
                if (originalLines[i + j].Trim() != searchLines[j].Trim())
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                var matchStartIndex = 0;
                for (int k = 0; k < i; k++)
                    matchStartIndex += originalLines[k].Length + 1;

                var matchEndIndex = matchStartIndex;
                for (int k = 0; k < searchLines.Length; k++)
                {
                    matchEndIndex += originalLines[i + k].Length;
                    if (k < searchLines.Length - 1)
                        matchEndIndex += 1;
                }

                var foundMatch = content[matchStartIndex..matchEndIndex];
                
                var secondOccurrence = content.IndexOf(foundMatch, matchEndIndex, StringComparison.Ordinal);
                if (secondOccurrence >= 0)
                    return null;

                return content[..matchStartIndex] + replacement + content[matchEndIndex..];
            }
        }

        return null;
    }

    private static string? TryBlockAnchorReplace(string content, string find, string replacement)
    {
        var originalLines = content.Split('\n');
        var searchLines = find.Split('\n');

        if (searchLines.Length < 3) return null;
        if (searchLines[^1] == "") searchLines = searchLines[..^1];

        var firstLineSearch = searchLines[0].Trim();
        var lastLineSearch = searchLines[^1].Trim();

        var candidates = new List<(int startLine, int endLine)>();
        
        for (int i = 0; i < originalLines.Length; i++)
        {
            if (originalLines[i].Trim() != firstLineSearch) continue;

            for (int j = i + 2; j < originalLines.Length; j++)
            {
                if (originalLines[j].Trim() == lastLineSearch)
                {
                    candidates.Add((i, j));
                    break;
                }
            }
        }

        if (candidates.Count != 1) return null;

        var (startLine, endLine) = candidates[0];
        
        var matchStartIndex = 0;
        for (int k = 0; k < startLine; k++)
            matchStartIndex += originalLines[k].Length + 1;

        var matchEndIndex = matchStartIndex;
        for (int k = startLine; k <= endLine; k++)
        {
            matchEndIndex += originalLines[k].Length;
            if (k < endLine) matchEndIndex += 1;
        }

        return content[..matchStartIndex] + replacement + content[matchEndIndex..];
    }

    private record EditArgs(
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("oldString")] string? OldString,
        [property: JsonPropertyName("newString")] string? NewString,
        [property: JsonPropertyName("replaceAll")] bool? ReplaceAll
    );
}
