namespace OpenFork.Core.Constants;

/// <summary>
/// Constants for token management across the 3-layer system.
/// </summary>
public static class TokenConstants
{
    // ═══════════════════════════════════════════════════════════════
    // ESTIMATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Average characters per token for estimation.</summary>
    public const double CharsPerToken = 4.0;

    // ═══════════════════════════════════════════════════════════════
    // CONTEXT LIMITS (configurable per model)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Default context window size.</summary>
    public const int DefaultContextWindow = 128_000;

    /// <summary>Default max output tokens.</summary>
    public const int DefaultMaxOutputTokens = 16_384;

    // ═══════════════════════════════════════════════════════════════
    // LAYER 1: TRUNCATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Maximum lines per tool output.</summary>
    public const int MaxOutputLines = 2_000;

    /// <summary>Maximum bytes per tool output (50KB).</summary>
    public const int MaxOutputBytes = 50 * 1024;

    /// <summary>Maximum characters per line.</summary>
    public const int MaxLineLength = 2_000;

    // ═══════════════════════════════════════════════════════════════
    // LAYER 2: PRUNING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Don't prune if under this token count.</summary>
    public const int PruneProtectTokens = 40_000;

    /// <summary>Minimum reduction target when pruning.</summary>
    public const int PruneMinimumTokens = 20_000;

    /// <summary>Keep first N chars of pruned output.</summary>
    public const int PruneOutputRetainChars = 2_000;

    // ═══════════════════════════════════════════════════════════════
    // LAYER 3: COMPACTION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Trigger compaction at this percentage of context.</summary>
    public const double CompactionThreshold = 0.90;

    /// <summary>Target percentage after compaction.</summary>
    public const int CompactionTargetPercent = 50;

    /// <summary>Max tokens for compaction summary.</summary>
    public const int CompactionSummaryMaxTokens = 2_000;

    // ═══════════════════════════════════════════════════════════════
    // PER-TOOL OUTPUT LIMITS (in chars)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Per-tool character limits for output truncation.</summary>
    public static readonly Dictionary<string, int> ToolOutputLimits = new()
    {
        ["read"] = 100_000,        // 100KB for file reads
        ["bash"] = 50_000,         // 50KB for command output
        ["grep"] = 30_000,         // 30KB for search results
        ["glob"] = 20_000,         // 20KB for file lists
        ["webfetch"] = 50_000,     // 50KB for web content
        ["websearch"] = 20_000,    // 20KB for search results
        ["list"] = 10_000,         // 10KB for directory listings
        ["codesearch"] = 30_000,   // 30KB for code search
        ["default"] = 50_000       // Default limit
    };

    /// <summary>Get the character limit for a specific tool.</summary>
    public static int GetToolLimit(string toolName)
    {
        var normalizedName = toolName.ToLowerInvariant();
        return ToolOutputLimits.GetValueOrDefault(normalizedName, ToolOutputLimits["default"]);
    }
}
