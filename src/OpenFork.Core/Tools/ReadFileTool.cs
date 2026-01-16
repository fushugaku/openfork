using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenFork.Core.Tools;

public class ReadFileTool : ITool
{
    private const int DefaultReadLimit = 2000;
    private const int MaxLineLength = 2000;
    private const int MaxBytes = 50 * 1024;

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".exe", ".dll", ".so", ".class", ".jar", ".war",
        ".7z", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
        ".bin", ".dat", ".obj", ".o", ".a", ".lib", ".wasm", ".pyc", ".pyo",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".svg",
        ".mp3", ".mp4", ".avi", ".mov", ".mkv", ".wav", ".flac",
        ".pdf", ".ttf", ".otf", ".woff", ".woff2", ".eot"
    };

    public string Name => "read";

    public string Description => PromptLoader.Load("read", 
        "Reads a file from the local filesystem with line numbers. Supports offset and limit for pagination.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            filePath = new
            {
                type = "string",
                description = "The path to the file to read"
            },
            offset = new
            {
                type = "integer",
                description = "The line number to start reading from (0-based)"
            },
            limit = new
            {
                type = "integer",
                description = "The number of lines to read (defaults to 2000)"
            }
        },
        required = new[] { "filePath" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<ReadArgs>(arguments, JsonHelper.Options);
            if (string.IsNullOrWhiteSpace(args?.FilePath))
                return new ToolResult(false, "Missing required parameter: filePath");

            var filePath = Path.IsPathRooted(args.FilePath)
                ? args.FilePath
                : Path.Combine(context.WorkingDirectory, args.FilePath);

            if (!File.Exists(filePath))
                return new ToolResult(false, $"File not found: {filePath}");

            var ext = Path.GetExtension(filePath);
            if (BinaryExtensions.Contains(ext))
                return new ToolResult(false, $"Cannot read binary file: {filePath}");

            if (await IsBinaryFileAsync(filePath))
                return new ToolResult(false, $"Cannot read binary file: {filePath}");

            context.MarkFileRead(filePath);

            var limit = args.Limit ?? DefaultReadLimit;
            var offset = args.Offset ?? 0;
            var lines = await File.ReadAllLinesAsync(filePath);

            var outputLines = new List<string>();
            var bytes = 0;
            var truncatedByBytes = false;

            for (int i = offset; i < Math.Min(lines.Length, offset + limit); i++)
            {
                var line = lines[i].Length > MaxLineLength 
                    ? lines[i][..MaxLineLength] + "..." 
                    : lines[i];
                
                var lineSize = System.Text.Encoding.UTF8.GetByteCount(line) + (outputLines.Count > 0 ? 1 : 0);
                if (bytes + lineSize > MaxBytes)
                {
                    truncatedByBytes = true;
                    break;
                }

                outputLines.Add($"{(i + 1).ToString().PadLeft(5)}| {line}");
                bytes += lineSize;
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine($"<file path=\"{filePath}\">");
            output.AppendLine(string.Join("\n", outputLines));

            var totalLines = lines.Length;
            var lastReadLine = offset + outputLines.Count;
            var hasMoreLines = totalLines > lastReadLine;

            if (truncatedByBytes)
            {
                output.AppendLine($"\n(Output truncated at {MaxBytes} bytes. Use 'offset' parameter to read beyond line {lastReadLine})");
            }
            else if (hasMoreLines)
            {
                output.AppendLine($"\n(File has more lines. Use 'offset' parameter to read beyond line {lastReadLine})");
            }
            else
            {
                output.AppendLine($"\n(End of file - total {totalLines} lines)");
            }
            output.AppendLine("</file>");

            return new ToolResult(true, output.ToString());
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error reading file: {ex.Message}");
        }
    }

    private static async Task<bool> IsBinaryFileAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0) return false;

        var bufferSize = (int)Math.Min(4096, fileInfo.Length);
        var buffer = new byte[bufferSize];

        await using var stream = File.OpenRead(filePath);
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize));
        if (bytesRead == 0) return false;

        var nonPrintableCount = 0;
        for (int i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0) return true;
            if (buffer[i] < 9 || (buffer[i] > 13 && buffer[i] < 32))
                nonPrintableCount++;
        }

        return (double)nonPrintableCount / bytesRead > 0.3;
    }

    private record ReadArgs(
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("offset")] int? Offset,
        [property: JsonPropertyName("limit")] int? Limit
    );
}
