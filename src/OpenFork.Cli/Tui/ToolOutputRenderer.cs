using System.Text;
using System.Text.RegularExpressions;

namespace OpenFork.Cli.Tui;

/// <summary>
/// Renders tool execution output with formatting and syntax indicators.
/// </summary>
public static class ToolOutputRenderer
{
    private const int BoxWidth = 50;

    /// <summary>
    /// Renders tool output to formatted text with status indicators.
    /// </summary>
    public static string RenderToText(string toolName, string output, bool success)
    {
        if (!success)
        {
            return FormatToolOutput(Icons.Cross, toolName, output, OutputType.Error);
        }

        return toolName.ToLowerInvariant() switch
        {
            "read" => RenderReadFile(output),
            "edit" => FormatToolOutput(Icons.Write, "edit", output, OutputType.Diff),
            "multiedit" => FormatToolOutput(Icons.Write, "multiedit", output, OutputType.Diff),
            "write" => FormatToolOutput(Icons.Write, "write", output, OutputType.Success),
            "bash" => RenderBashOutput(output),
            "glob" => RenderGlobOutput(output),
            "grep" => RenderGrepOutput(output),
            "list" => FormatToolOutput(Icons.Folder, "list", output, OutputType.Files),
            "webfetch" => RenderWebFetch(output),
            "websearch" => FormatToolOutput(Icons.Web, "websearch", output, OutputType.Info),
            "codesearch" => RenderCodeSearch(output),
            "diagnostics" => RenderDiagnostics(output),
            "lsp" => FormatToolOutput(Icons.Tool, "lsp", output, OutputType.Info),
            "question" => FormatToolOutput(Icons.Info, "question", output, OutputType.Info),
            "todowrite" => FormatToolOutput(Icons.Check, "todowrite", output, OutputType.Success),
            "todoread" => FormatToolOutput(Icons.Pending, "todoread", output, OutputType.Info),
            "search_project" => FormatToolOutput(Icons.Search, "search_project", output, OutputType.Info),
            "task" => RenderTaskOutput(output, success),
            _ => FormatToolOutput(Icons.Tool, toolName, output, OutputType.Info)
        };
    }

    /// <summary>
    /// Renders grouped file reads with compact formatting.
    /// </summary>
    public static string RenderGroupedReads(List<string> filePaths)
    {
        var sb = new StringBuilder();
        var header = $" {Icons.Read} read ({filePaths.Count} files) ";
        sb.AppendLine(CreateBoxTop(header));
        foreach (var path in filePaths.Take(10))
        {
            sb.AppendLine(CreateBoxLine($"{Icons.File} {Path.GetFileName(path)}"));
        }
        if (filePaths.Count > 10)
            sb.AppendLine(CreateBoxLine($"   ... +{filePaths.Count - 10} more files"));
        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string CreateBoxTop(string title)
    {
        var padding = BoxWidth - title.Length - 2;
        var rightPad = Math.Max(1, padding);
        return $"{Icons.BoxTopLeft}{Icons.BoxHorizontal}{title}{new string(Icons.BoxHorizontal[0], rightPad)}{Icons.BoxTopRight}";
    }

    private static string CreateBoxLine(string content)
    {
        var trimmed = content.Length > BoxWidth - 4 ? content[..(BoxWidth - 7)] + "..." : content;
        return $"{Icons.BoxVertical} {trimmed.PadRight(BoxWidth - 4)} {Icons.BoxVertical}";
    }

    private static string CreateBoxBottom()
    {
        return $"{Icons.BoxBottomLeft}{new string(Icons.BoxHorizontal[0], BoxWidth - 2)}{Icons.BoxBottomRight}";
    }

    private enum OutputType
    {
        Info,
        Success,
        Error,
        Warning,
        Diff,
        Code,
        Files
    }

    private static string FormatToolOutput(string icon, string toolName, string output, OutputType type)
    {
        var sb = new StringBuilder();
        var statusIcon = type switch
        {
            OutputType.Success => Icons.Check,
            OutputType.Error => Icons.Cross,
            OutputType.Warning => Icons.Warning,
            _ => ""
        };

        var header = $" {icon} {toolName} {statusIcon}".Trim();
        sb.AppendLine(CreateBoxTop(header));

        // Format the output based on type
        var formattedOutput = type switch
        {
            OutputType.Diff => FormatDiffOutput(output),
            OutputType.Code => FormatCodeOutput(output),
            OutputType.Files => FormatFilesOutput(output),
            OutputType.Error => FormatErrorOutput(output),
            _ => FormatStandardOutput(output)
        };

        var lines = formattedOutput.Split('\n').Take(Layout.MaxToolOutputLines).ToArray();
        foreach (var line in lines)
        {
            sb.AppendLine(CreateBoxLine(line));
        }

        var lineCount = formattedOutput.Split('\n').Length;
        if (lineCount > Layout.MaxToolOutputLines)
            sb.AppendLine(CreateBoxLine($"... ({lineCount - Layout.MaxToolOutputLines} more lines)"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderReadFile(string output)
    {
        var lines = output.Split('\n');
        var filePath = "File Content";

        // Extract file path from XML-style header
        if (lines.Length > 0)
        {
            var pathMatch = Regex.Match(lines[0], @"<file path=""(.+?)"">");
            if (pathMatch.Success)
            {
                filePath = pathMatch.Groups[1].Value;
            }
        }

        var sb = new StringBuilder();
        var fileName = Path.GetFileName(filePath);
        sb.AppendLine(CreateBoxTop($" {Icons.Read} {fileName} "));
        sb.AppendLine(CreateBoxLine($"📍 {filePath}"));
        sb.AppendLine(CreateBoxLine(new string('─', BoxWidth - 6)));

        // Detect language and format code
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var contentLines = lines.Skip(1).Take(Layout.MaxCodeBlockLines).ToArray();

        foreach (var line in contentLines)
        {
            var formattedLine = FormatCodeLine(line, extension);
            sb.AppendLine(CreateBoxLine(formattedLine));
        }

        if (lines.Length > Layout.MaxCodeBlockLines + 1)
            sb.AppendLine(CreateBoxLine($"... ({lines.Length - Layout.MaxCodeBlockLines - 1} more lines)"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderBashOutput(string output)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CreateBoxTop($" {Icons.Bash} bash "));

        var lines = output.Split('\n');
        foreach (var line in lines.Take(100))
        {
            // Highlight error patterns
            var formattedLine = line;
            if (IsErrorLine(line))
                formattedLine = $"❌ {line}";
            else if (IsWarningLine(line))
                formattedLine = $"⚠️ {line}";

            sb.AppendLine(CreateBoxLine(formattedLine));
        }

        if (lines.Length > 100)
            sb.AppendLine(CreateBoxLine($"... ({lines.Length - 100} more lines)"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderGlobOutput(string output)
    {
        var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        sb.AppendLine(CreateBoxTop($" {Icons.Search} glob ({files.Length} matches) "));

        foreach (var file in files.Take(20))
        {
            var icon = GetFileIcon(file);
            sb.AppendLine(CreateBoxLine($"{icon} {Path.GetFileName(file)}"));
        }

        if (files.Length > 20)
            sb.AppendLine(CreateBoxLine($"   ... +{files.Length - 20} more files"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderGrepOutput(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        sb.AppendLine(CreateBoxTop($" {Icons.Search} grep ({lines.Length} results) "));

        foreach (var line in lines.Take(50))
        {
            // Format grep output: file:line:content
            var match = Regex.Match(line, @"^(.+?):(\d+):(.*)$");
            if (match.Success)
            {
                var file = Path.GetFileName(match.Groups[1].Value);
                var lineNum = match.Groups[2].Value;
                var content = match.Groups[3].Value.Trim();
                if (content.Length > 35) content = content[..35] + "...";
                sb.AppendLine(CreateBoxLine($"📍 {file}:{lineNum} {content}"));
            }
            else
            {
                sb.AppendLine(CreateBoxLine(line));
            }
        }

        if (lines.Length > 50)
            sb.AppendLine(CreateBoxLine($"   ... +{lines.Length - 50} more matches"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderWebFetch(string output)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CreateBoxTop($" {Icons.Web} webfetch "));

        // Truncate content
        var maxLength = 2000;
        var displayOutput = output.Length > maxLength
            ? output[..maxLength]
            : output;

        foreach (var line in displayOutput.Split('\n').Take(50))
        {
            sb.AppendLine(CreateBoxLine(line));
        }

        if (output.Length > maxLength)
            sb.AppendLine(CreateBoxLine($"... (truncated {output.Length - maxLength} chars)"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderCodeSearch(string output)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CreateBoxTop($" {Icons.Code} codesearch "));

        var maxLength = 3000;
        var displayOutput = output.Length > maxLength
            ? output[..maxLength]
            : output;

        foreach (var line in displayOutput.Split('\n').Take(Layout.MaxCodeBlockLines))
        {
            sb.AppendLine(CreateBoxLine(line));
        }

        if (output.Length > maxLength)
            sb.AppendLine(CreateBoxLine("... (truncated)"));

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    private static string RenderDiagnostics(string output)
    {
        var sb = new StringBuilder();
        var hasErrors = output.Contains("error", StringComparison.OrdinalIgnoreCase);
        var icon = hasErrors ? Icons.Cross : Icons.Check;

        sb.AppendLine(CreateBoxTop($" {icon} diagnostics "));

        foreach (var line in output.Split('\n').Take(50))
        {
            var formattedLine = line;
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                formattedLine = $"❌ {line}";
            else if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
                formattedLine = $"⚠️ {line}";

            sb.AppendLine(CreateBoxLine(formattedLine));
        }

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    /// <summary>
    /// Renders task tool (subagent) output with distinctive formatting.
    /// </summary>
    private static string RenderTaskOutput(string output, bool success)
    {
        var sb = new StringBuilder();

        // Check if it's a background launch or completed execution
        var isBackground = output.Contains("launched in background", StringComparison.OrdinalIgnoreCase);
        var isCompleted = output.Contains("## Subagent Result", StringComparison.OrdinalIgnoreCase);

        string headerIcon;
        string headerText;

        if (!success)
        {
            headerIcon = Icons.Cross;
            headerText = "subagent failed";
        }
        else if (isBackground)
        {
            headerIcon = "🚀";
            headerText = "subagent launched";
        }
        else if (isCompleted)
        {
            headerIcon = "🤖✓";
            headerText = "subagent completed";
        }
        else
        {
            headerIcon = "🤖";
            headerText = "subagent";
        }

        sb.AppendLine(CreateBoxTop($" {headerIcon} {headerText} "));

        // Parse subagent type and description from output
        var lines = output.Split('\n');
        var agentType = ExtractValue(lines, "Type:");
        var description = ExtractValue(lines, "Description:") ?? ExtractValue(lines, "Task:");
        var agentId = ExtractValue(lines, "ID:");

        if (!string.IsNullOrEmpty(agentType))
        {
            var agentIcon = GetSubagentIcon(agentType);
            sb.AppendLine(CreateBoxLine($"{agentIcon} Agent: {agentType}"));
        }

        if (!string.IsNullOrEmpty(description))
        {
            sb.AppendLine(CreateBoxLine($"📋 Task: {description}"));
        }

        if (isBackground && !string.IsNullOrEmpty(agentId))
        {
            sb.AppendLine(CreateBoxLine($"🔗 ID: {TruncateString(agentId, 35)}"));
            sb.AppendLine(CreateBoxLine("⏳ Running in background..."));
        }
        else if (isCompleted)
        {
            // Extract and show result preview
            var resultStart = output.IndexOf("**Result:**", StringComparison.OrdinalIgnoreCase);
            if (resultStart >= 0)
            {
                var resultContent = output[(resultStart + 11)..].Trim();
                var resultLines = resultContent.Split('\n').Take(5).ToArray();
                sb.AppendLine(CreateBoxLine("─── Result ───"));
                foreach (var line in resultLines)
                {
                    sb.AppendLine(CreateBoxLine(line));
                }
                if (resultContent.Split('\n').Length > 5)
                {
                    sb.AppendLine(CreateBoxLine("...(truncated)"));
                }
            }
        }
        else if (!success)
        {
            // Show error message
            sb.AppendLine(CreateBoxLine($"❌ {output}"));
        }
        else
        {
            // Generic output
            foreach (var line in lines.Take(8))
            {
                sb.AppendLine(CreateBoxLine(line));
            }
            if (lines.Length > 8)
            {
                sb.AppendLine(CreateBoxLine("...(more)"));
            }
        }

        sb.AppendLine(CreateBoxBottom());
        return sb.ToString();
    }

    /// <summary>
    /// Gets an icon for a specific subagent type.
    /// </summary>
    private static string GetSubagentIcon(string agentType)
    {
        return agentType.ToLowerInvariant() switch
        {
            "explore" or "explorer" => "🔭",
            "researcher" or "research" => "📚",
            "planner" or "planner-sub" => "📝",
            "general" => "🤖",
            "coder" => "💻",
            "tester" => "🧪",
            "reviewer" => "👀",
            _ => "🤖"
        };
    }

    /// <summary>
    /// Extracts a value from lines matching "Key: Value" pattern.
    /// </summary>
    private static string? ExtractValue(string[] lines, string key)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', ' ');
            if (trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[key.Length..].Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// Truncates a string to specified length with ellipsis.
    /// </summary>
    private static string TruncateString(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length > maxLength ? text[..maxLength] + "..." : text;
    }

    // Formatting helpers

    private static string FormatDiffOutput(string output)
    {
        var sb = new StringBuilder();
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith('+') && !line.StartsWith("+++"))
                sb.AppendLine($"[+] {line}");
            else if (line.StartsWith('-') && !line.StartsWith("---"))
                sb.AppendLine($"[-] {line}");
            else if (line.StartsWith("@@"))
                sb.AppendLine($"[@] {line}");
            else
                sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatCodeOutput(string output)
    {
        // Detect code blocks and format
        var sb = new StringBuilder();
        var inCodeBlock = false;
        var codeLanguage = "";

        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    codeLanguage = line.Length > 3 ? line[3..].Trim() : "";
                    sb.AppendLine($"─── {codeLanguage} ───");
                    inCodeBlock = true;
                }
                else
                {
                    sb.AppendLine("───────────");
                    inCodeBlock = false;
                }
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatFilesOutput(string output)
    {
        var sb = new StringBuilder();
        foreach (var line in output.Split('\n'))
        {
            var icon = GetFileIcon(line);
            sb.AppendLine($"{icon} {line}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatErrorOutput(string output)
    {
        var sb = new StringBuilder();
        foreach (var line in output.Split('\n'))
        {
            sb.AppendLine($"  {line}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string FormatStandardOutput(string output)
    {
        return output;
    }

    private static string FormatCodeLine(string line, string extension)
    {
        // Basic line number extraction if present
        var match = Regex.Match(line, @"^\s*(\d+)[:\|→]\s*(.*)$");
        if (match.Success)
        {
            var lineNum = match.Groups[1].Value.PadLeft(4);
            var content = match.Groups[2].Value;
            return $"{lineNum}│ {content}";
        }
        return line;
    }

    private static string GetFileIcon(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" or ".csx" => "🟣",          // C#
            ".js" or ".jsx" => "🟡",           // JavaScript
            ".ts" or ".tsx" => "🔵",           // TypeScript
            ".py" => "🐍",                      // Python
            ".rs" => "🦀",                      // Rust
            ".go" => "🔷",                      // Go
            ".java" or ".kt" => "☕",           // Java/Kotlin
            ".json" => "📋",                    // JSON
            ".xml" or ".html" or ".htm" => "🌐", // Markup
            ".md" or ".txt" => "📝",            // Text/Markdown
            ".css" or ".scss" or ".sass" => "🎨", // Styles
            ".yaml" or ".yml" => "⚙️",          // Config
            ".sql" => "🗃️",                     // SQL
            ".sh" or ".bash" or ".zsh" => "💻", // Shell
            ".dockerfile" or "" when path.Contains("Dockerfile") => "🐳", // Docker
            ".gitignore" or ".gitattributes" => "📦", // Git
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" => "🖼️", // Images
            _ when Directory.Exists(path) => "📁",
            _ => "📄"
        };
    }

    private static bool IsErrorLine(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.Contains("error") ||
               lower.Contains("failed") ||
               lower.Contains("exception") ||
               lower.Contains("fatal") ||
               line.StartsWith("E ");
    }

    private static bool IsWarningLine(string line)
    {
        var lower = line.ToLowerInvariant();
        return lower.Contains("warning") ||
               lower.Contains("warn") ||
               lower.Contains("deprecated") ||
               line.StartsWith("W ");
    }

    /// <summary>
    /// Detects if content contains a diff.
    /// </summary>
    public static bool IsDiffContent(string content)
    {
        return content.Contains("diff --git") ||
               content.Contains("@@") ||
               Regex.IsMatch(content, @"^[+-](?![+-])", RegexOptions.Multiline);
    }

    /// <summary>
    /// Detects if content is a code block.
    /// </summary>
    public static bool IsCodeBlock(string content)
    {
        return content.StartsWith("```") || content.Contains("\n```");
    }

    /// <summary>
    /// Gets a compact summary for tool execution.
    /// </summary>
    public static string GetCompactSummary(string toolName, bool success, string? additionalInfo = null)
    {
        var icon = success ? Icons.Check : Icons.Cross;
        var info = !string.IsNullOrEmpty(additionalInfo) ? $" ({additionalInfo})" : "";
        return $"{Icons.Tool} {toolName} {icon}{info}";
    }
}
