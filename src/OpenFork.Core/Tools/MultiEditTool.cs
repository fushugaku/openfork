using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

/// <summary>
/// Perform multiple edits on a single file in one atomic operation.
/// All edits are applied in sequence; if any fails, none are applied.
/// </summary>
public class MultiEditTool : ITool
{
    public string Name => "multiedit";

    public string Description => PromptLoader.Load("multiedit",
        "Make multiple edits to a single file in one operation. All edits are atomic - either all succeed or none are applied.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            filePath = new
            {
                type = "string",
                description = "The path to the file to modify"
            },
            edits = new
            {
                type = "array",
                description = "Array of edit operations to perform sequentially on the file",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        oldString = new
                        {
                            type = "string",
                            description = "The text to replace"
                        },
                        newString = new
                        {
                            type = "string",
                            description = "The text to replace it with"
                        },
                        replaceAll = new
                        {
                            type = "boolean",
                            description = "Replace all occurrences (default false)"
                        }
                    },
                    required = new[] { "oldString", "newString" }
                }
            }
        },
        required = new[] { "filePath", "edits" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<MultiEditArgs>(arguments, JsonHelper.Options);
            
            if (string.IsNullOrEmpty(args?.FilePath))
                return new ToolResult(false, "filePath is required");

            if (args.Edits == null || args.Edits.Count == 0)
                return new ToolResult(false, "At least one edit is required");

            var filePath = Path.IsPathRooted(args.FilePath)
                ? args.FilePath
                : Path.Combine(context.WorkingDirectory, args.FilePath);

            if (!context.HasReadFile(filePath))
                return new ToolResult(false, "You must read the file before editing it. Use the read tool first.");

            if (!File.Exists(filePath))
                return new ToolResult(false, $"File not found: {filePath}");

            var originalContent = await File.ReadAllTextAsync(filePath);
            var content = originalContent;
            var appliedEdits = new List<string>();

            foreach (var edit in args.Edits)
            {
                if (string.IsNullOrEmpty(edit.OldString))
                {
                    return new ToolResult(false, "Each edit must have a non-empty oldString");
                }

                if (edit.OldString == edit.NewString)
                {
                    return new ToolResult(false, $"oldString and newString must be different for edit: '{Truncate(edit.OldString, 50)}'");
                }

                var newContent = ApplyEdit(content, edit.OldString, edit.NewString ?? "", edit.ReplaceAll ?? false);
                
                if (newContent == content)
                {
                    return new ToolResult(false, $"oldString not found in file: '{Truncate(edit.OldString, 100)}'");
                }

                content = newContent;
                appliedEdits.Add($"'{Truncate(edit.OldString, 30)}' → '{Truncate(edit.NewString ?? "", 30)}'");
            }

            await File.WriteAllTextAsync(filePath, content);

            var oldLines = originalContent.Split('\n').Length;
            var newLines = content.Split('\n').Length;

            context.FileChangeTracker?.TrackChange(new FileChange
            {
                FilePath = filePath,
                IsNew = false,
                LinesAdded = Math.Max(0, newLines - oldLines),
                LinesDeleted = Math.Max(0, oldLines - newLines),
                NewLineCount = newLines
            });

            context.MarkFileRead(filePath);

            return new ToolResult(true, $"Applied {appliedEdits.Count} edits successfully:\n" + 
                string.Join("\n", appliedEdits.Select((e, i) => $"  {i + 1}. {e}")));
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error applying edits: {ex.Message}");
        }
    }

    private static string ApplyEdit(string content, string oldString, string newString, bool replaceAll)
    {
        if (replaceAll)
            return content.Replace(oldString, newString);

        var index = content.IndexOf(oldString, StringComparison.Ordinal);
        if (index < 0)
        {
            index = TryFindWithTrimmedLines(content, oldString);
            if (index < 0) return content;
            
            var match = ExtractMatchFromTrimmed(content, oldString, index);
            if (match != null)
                return content[..index] + newString + content[(index + match.Length)..];
            return content;
        }

        var lastIndex = content.LastIndexOf(oldString, StringComparison.Ordinal);
        if (index != lastIndex)
        {
            return content;
        }

        return content[..index] + newString + content[(index + oldString.Length)..];
    }

    private static int TryFindWithTrimmedLines(string content, string find)
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
                return matchStartIndex;
            }
        }

        return -1;
    }

    private static string? ExtractMatchFromTrimmed(string content, string find, int startIndex)
    {
        var originalLines = content.Split('\n');
        var searchLines = find.Split('\n');
        
        if (searchLines.Length > 0 && searchLines[^1] == "")
            searchLines = searchLines[..^1];

        var lineIndex = 0;
        var charCount = 0;
        while (charCount < startIndex && lineIndex < originalLines.Length)
        {
            charCount += originalLines[lineIndex].Length + 1;
            lineIndex++;
        }

        if (lineIndex + searchLines.Length > originalLines.Length)
            return null;

        var matchEndIndex = startIndex;
        for (int k = 0; k < searchLines.Length; k++)
        {
            matchEndIndex += originalLines[lineIndex + k].Length;
            if (k < searchLines.Length - 1)
                matchEndIndex += 1;
        }

        return content[startIndex..matchEndIndex];
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= maxLen ? s : s[..maxLen] + "...";
    }

    private record MultiEditArgs(
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("edits")] List<EditOperation>? Edits
    );

    private record EditOperation(
        [property: JsonPropertyName("oldString")] string? OldString,
        [property: JsonPropertyName("newString")] string? NewString,
        [property: JsonPropertyName("replaceAll")] bool? ReplaceAll
    );
}
