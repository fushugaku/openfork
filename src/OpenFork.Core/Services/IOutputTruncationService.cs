namespace OpenFork.Core.Services;

/// <summary>
/// Layer 1: Tool output truncation service.
/// Truncates outputs immediately when they exceed limits.
/// </summary>
public interface IOutputTruncationService
{
    /// <summary>
    /// Truncate tool output to configured limits.
    /// </summary>
    /// <param name="output">Raw tool output.</param>
    /// <param name="toolName">Name of the tool for per-tool limits.</param>
    /// <param name="requestedSpillPath">Optional explicit spill path.</param>
    /// <returns>Truncation result with truncated output and metadata.</returns>
    TruncationResult Truncate(string output, string toolName, string? requestedSpillPath = null);

    /// <summary>
    /// Retrieve full output from a spill file.
    /// </summary>
    Task<string> RetrieveSpilledAsync(string spillPath, CancellationToken ct = default);

    /// <summary>
    /// Clean up old spill files.
    /// </summary>
    Task CleanupOldSpillFilesAsync(TimeSpan maxAge, CancellationToken ct = default);
}

/// <summary>
/// Result of truncating tool output.
/// </summary>
public record TruncationResult
{
    /// <summary>Truncated (or original if not truncated) output.</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Whether truncation was applied.</summary>
    public bool WasTruncated { get; init; }

    /// <summary>Original line count.</summary>
    public int OriginalLines { get; init; }

    /// <summary>Original byte count.</summary>
    public int OriginalBytes { get; init; }

    /// <summary>Truncated line count.</summary>
    public int TruncatedLines { get; init; }

    /// <summary>Truncated byte count.</summary>
    public int TruncatedBytes { get; init; }

    /// <summary>Path to full output if spilled to disk.</summary>
    public string? SpillPath { get; init; }

    /// <summary>Human-readable truncation message appended to output.</summary>
    public string? TruncationMessage { get; init; }
}
