using System.Text;
using Microsoft.Extensions.Logging;
using OpenFork.Core.Config;
using OpenFork.Core.Constants;

namespace OpenFork.Core.Services;

/// <summary>
/// Layer 1: Truncates tool outputs to respect line, byte, and character limits.
/// Spills full content to disk when truncation is applied.
/// </summary>
public class OutputTruncationService : IOutputTruncationService
{
    private readonly ILogger<OutputTruncationService> _logger;
    private readonly string _spillDirectory;

    public OutputTruncationService(
        ILogger<OutputTruncationService> logger,
        AppSettings settings)
    {
        _logger = logger;

        // Spill directory next to database
        var dataDir = Path.GetDirectoryName(settings.DatabasePath) ?? "data";
        _spillDirectory = Path.Combine(dataDir, "spill");
        Directory.CreateDirectory(_spillDirectory);
    }

    /// <inheritdoc />
    public TruncationResult Truncate(string output, string toolName, string? requestedSpillPath = null)
    {
        if (string.IsNullOrEmpty(output))
        {
            return new TruncationResult
            {
                Output = output,
                WasTruncated = false,
                OriginalLines = 0,
                OriginalBytes = 0,
                TruncatedLines = 0,
                TruncatedBytes = 0
            };
        }

        var originalBytes = Encoding.UTF8.GetByteCount(output);
        var lines = output.Split('\n');
        var originalLines = lines.Length;

        // Get tool-specific character limit
        var charLimit = TokenConstants.GetToolLimit(toolName);

        // Check if truncation needed
        bool needsTruncation = originalLines > TokenConstants.MaxOutputLines ||
                               originalBytes > TokenConstants.MaxOutputBytes ||
                               output.Length > charLimit;

        if (!needsTruncation)
        {
            // Still truncate individual long lines
            var processedOutput = TruncateAllLineLength(output);
            return new TruncationResult
            {
                Output = processedOutput,
                WasTruncated = false,
                OriginalLines = originalLines,
                OriginalBytes = originalBytes,
                TruncatedLines = originalLines,
                TruncatedBytes = Encoding.UTF8.GetByteCount(processedOutput)
            };
        }

        // Spill full output to disk
        string? actualSpillPath = null;
        if (requestedSpillPath != null || originalBytes > TokenConstants.MaxOutputBytes)
        {
            actualSpillPath = requestedSpillPath ?? Path.Combine(
                _spillDirectory,
                $"{DateTimeOffset.UtcNow:yyyyMMdd}_{Guid.NewGuid():N}.txt");

            // Create parent directory if using explicit path
            var parentDir = Path.GetDirectoryName(actualSpillPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            File.WriteAllText(actualSpillPath, output);
            _logger.LogDebug("Spilled {Bytes} bytes to {Path}", originalBytes, actualSpillPath);
        }

        // Truncate to limits
        var truncatedLines = new List<string>();
        var currentBytes = 0;
        var lineCount = 0;

        foreach (var line in lines)
        {
            if (lineCount >= TokenConstants.MaxOutputLines)
                break;

            var truncatedLine = TruncateSingleLine(line);
            var lineBytes = Encoding.UTF8.GetByteCount(truncatedLine) + 1; // +1 for newline

            if (currentBytes + lineBytes > TokenConstants.MaxOutputBytes)
                break;

            truncatedLines.Add(truncatedLine);
            currentBytes += lineBytes;
            lineCount++;
        }

        // Apply character limit
        var truncatedOutput = string.Join('\n', truncatedLines);
        if (truncatedOutput.Length > charLimit)
        {
            truncatedOutput = truncatedOutput[..charLimit];
        }

        // Build truncation message
        var truncationMessage = BuildTruncationMessage(
            originalLines, originalBytes,
            truncatedLines.Count, currentBytes,
            actualSpillPath);

        return new TruncationResult
        {
            Output = truncatedOutput + "\n\n" + truncationMessage,
            WasTruncated = true,
            OriginalLines = originalLines,
            OriginalBytes = originalBytes,
            TruncatedLines = truncatedLines.Count,
            TruncatedBytes = currentBytes,
            SpillPath = actualSpillPath,
            TruncationMessage = truncationMessage
        };
    }

    /// <inheritdoc />
    public async Task<string> RetrieveSpilledAsync(string spillPath, CancellationToken ct = default)
    {
        if (!File.Exists(spillPath))
            throw new FileNotFoundException($"Spill file not found: {spillPath}");

        return await File.ReadAllTextAsync(spillPath, ct);
    }

    /// <inheritdoc />
    public Task CleanupOldSpillFilesAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(_spillDirectory))
                return;

            var cutoff = DateTimeOffset.UtcNow - maxAge;
            var files = Directory.GetFiles(_spillDirectory, "*.txt");
            var deleted = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc < cutoff.UtcDateTime)
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete spill file: {Path}", file);
                    }
                }
            }

            if (deleted > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old spill files", deleted);
            }
        }, ct);
    }

    private static string TruncateSingleLine(string line)
    {
        if (line.Length <= TokenConstants.MaxLineLength)
            return line;

        return line[..TokenConstants.MaxLineLength] + "... (line truncated)";
    }

    private static string TruncateAllLineLength(string output)
    {
        var lines = output.Split('\n');
        var truncated = lines.Select(TruncateSingleLine);
        return string.Join('\n', truncated);
    }

    private static string BuildTruncationMessage(
        int origLines, int origBytes,
        int truncLines, int truncBytes,
        string? spillPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"[Output truncated: {origLines:N0} → {truncLines:N0} lines, {FormatBytes(origBytes)} → {FormatBytes(truncBytes)}]");

        if (spillPath != null)
        {
            sb.AppendLine($"[Full output saved to: {spillPath}]");
            sb.AppendLine("[Use 'read' tool with the path above to see full content]");
        }

        return sb.ToString();
    }

    private static string FormatBytes(int bytes) => bytes switch
    {
        < 1024 => $"{bytes}B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1}KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1}MB"
    };
}
