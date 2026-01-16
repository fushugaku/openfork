using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFork.Core.Lsp;

namespace OpenFork.Core.Tools;

public class DiagnosticsTool : ITool
{
    private readonly LspService? _lspService;

    public DiagnosticsTool(LspService? lspService = null)
    {
        _lspService = lspService;
    }

    public string Name => "diagnostics";

    public string Description => PromptLoader.Load("diagnostics",
        "Get diagnostics (errors, warnings) from language servers for specified files.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            files = new
            {
                type = "array",
                items = new { type = "string" },
                description = "List of file paths to get diagnostics for. If empty, gets diagnostics for all open/recently edited files."
            }
        },
        required = Array.Empty<string>()
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<DiagnosticsArgs>(arguments, JsonHelper.Options);
            var files = args?.Files ?? Array.Empty<string>();

            var resolvedFiles = files.Select(f => 
                Path.IsPathRooted(f) ? f : Path.Combine(context.WorkingDirectory, f)
            ).ToArray();

            List<Diagnostic> diagnostics;

            if (_lspService != null)
            {
                var lspDiags = await _lspService.GetDiagnosticsAsync(resolvedFiles, context.WorkingDirectory);
                diagnostics = lspDiags.Select(d => new Diagnostic
                {
                    FilePath = d.FilePath,
                    Line = d.Line,
                    Column = d.Column,
                    Message = d.Message,
                    Severity = (DiagnosticSeverity)(int)d.Severity,
                    Code = d.Code
                }).ToList();
            }
            else if (context.GetDiagnosticsAsync != null)
            {
                diagnostics = await context.GetDiagnosticsAsync(resolvedFiles);
            }
            else
            {
                return new ToolResult(false, "Diagnostics service not available in this context");
            }

            if (diagnostics.Count == 0)
                return new ToolResult(true, "No diagnostics found. Code looks good!");

            var sb = new StringBuilder();
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            var warnings = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();

            sb.AppendLine($"Found {errors.Count} error(s) and {warnings.Count} warning(s):");
            sb.AppendLine();

            var byFile = diagnostics.GroupBy(d => d.FilePath);
            foreach (var group in byFile)
            {
                var relativePath = Path.GetRelativePath(context.WorkingDirectory, group.Key);
                sb.AppendLine($"📄 {relativePath}:");

                foreach (var diag in group.OrderBy(d => d.Line))
                {
                    var icon = diag.Severity switch
                    {
                        DiagnosticSeverity.Error => "❌",
                        DiagnosticSeverity.Warning => "⚠️",
                        DiagnosticSeverity.Information => "ℹ️",
                        _ => "💡"
                    };

                    var code = !string.IsNullOrEmpty(diag.Code) ? $" [{diag.Code}]" : "";
                    sb.AppendLine($"  {icon} Line {diag.Line}:{diag.Column}{code}: {diag.Message}");
                }
                sb.AppendLine();
            }

            return new ToolResult(errors.Count > 0 ? false : true, sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Error getting diagnostics: {ex.Message}");
        }
    }

    private record DiagnosticsArgs(
        [property: JsonPropertyName("files")] string[]? Files
    );
}
