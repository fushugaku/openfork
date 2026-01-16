using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class WriteFileTool : ITool
{
    public string Name => "write";

    public string Description => PromptLoader.Load("write",
        "Writes a file to the local filesystem. Creates new files or overwrites existing ones.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            filePath = new
            {
                type = "string",
                description = "The path to the file to write (can be absolute or relative)"
            },
            content = new
            {
                type = "string",
                description = "The content to write to the file"
            }
        },
        required = new[] { "filePath", "content" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<WriteArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.FilePath))
                return new ToolResult(false, "Missing required parameter: filePath");

            var filePath = Path.IsPathRooted(args.FilePath)
                ? args.FilePath
                : Path.Combine(context.WorkingDirectory, args.FilePath);

            var exists = File.Exists(filePath);
            
            if (exists && !context.HasReadFile(filePath))
                return new ToolResult(false, "You must read the file before overwriting it. Use the read tool first.");

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var oldContent = exists ? await File.ReadAllTextAsync(filePath) : null;
            var newContent = args.Content ?? "";

            await File.WriteAllTextAsync(filePath, newContent);

            var oldLines = oldContent?.Split('\n').Length ?? 0;
            var newLines = newContent.Split('\n').Length;

            context.FileChangeTracker?.TrackChange(new FileChange
            {
                FilePath = filePath,
                IsNew = !exists,
                LinesAdded = exists ? Math.Max(0, newLines - oldLines) : newLines,
                LinesDeleted = exists ? Math.Max(0, oldLines - newLines) : 0,
                NewLineCount = newLines
            });

            context.MarkFileRead(filePath);

            var info = new FileInfo(filePath);
            return new ToolResult(true, $"Wrote {info.Length} bytes to {filePath}");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error writing file: {ex.Message}");
        }
    }

    private record WriteArgs(
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("content")] string? Content
    );
}
