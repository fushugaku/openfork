namespace OpenFork.Core.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }
    Task<ToolResult> ExecuteAsync(string arguments, ToolContext context);
}

public record ToolResult(bool Success, string Output)
{
    public bool RequiresUserInput { get; init; }
    public QuestionRequest? QuestionRequest { get; init; }
}

public class QuestionRequest
{
    public List<Question> Questions { get; set; } = new();
}

public class Question
{
    public string Text { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public bool AllowMultiple { get; set; }
    public bool AllowCustom { get; set; } = true;
}

public class QuestionAnswer
{
    public string QuestionText { get; set; } = "";
    public List<string> Answers { get; set; } = new();
}

public class ToolContext
{
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public FileChangeTracker? FileChangeTracker { get; set; }
    public TodoTracker? TodoTracker { get; set; }

    /// <summary>Current session ID for session-aware tools.</summary>
    public long? SessionId { get; set; }

    /// <summary>Current message ID for message-aware tools.</summary>
    public long? MessageId { get; set; }

    /// <summary>Current agent configuration for agent-aware tools.</summary>
    public AgentConfig? AgentConfig { get; set; }

    public Func<QuestionRequest, Task<List<QuestionAnswer>>>? AskUserAsync { get; set; }

    public Func<string[], Task<List<Diagnostic>>>? GetDiagnosticsAsync { get; set; }

    private readonly HashSet<string> _readFiles = new(StringComparer.OrdinalIgnoreCase);

    public void MarkFileRead(string filePath)
    {
        var normalized = Path.GetFullPath(filePath);
        _readFiles.Add(normalized);
    }

    public bool HasReadFile(string filePath)
    {
        var normalized = Path.GetFullPath(filePath);
        return _readFiles.Contains(normalized);
    }
}

/// <summary>
/// Minimal agent configuration for tool context.
/// </summary>
public class AgentConfig
{
    /// <summary>Agent ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Agent slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Whether this agent can spawn subagents.</summary>
    public bool CanSpawnSubagents { get; set; }
}

public class Diagnostic
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Message { get; set; } = "";
    public DiagnosticSeverity Severity { get; set; }
    public string? Code { get; set; }
}

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}
