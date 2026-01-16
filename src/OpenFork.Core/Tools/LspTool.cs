using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFork.Core.Lsp;

namespace OpenFork.Core.Tools;

public class LspTool : ITool
{
    private readonly LspService _lspService;

    public LspTool(LspService lspService)
    {
        _lspService = lspService;
    }

    public string Name => "lsp";

    public string Description => PromptLoader.Load("lsp", @"Interact with Language Server Protocol (LSP) servers to get code intelligence features.

Supported operations:
- goToDefinition: Find where a symbol is defined
- findReferences: Find all references to a symbol
- hover: Get hover information (documentation, type info) for a symbol
- documentSymbol: Get all symbols (functions, classes, variables) in a document
- workspaceSymbol: Search for symbols across the entire workspace
- goToImplementation: Find implementations of an interface or abstract method

All operations require:
- filePath: The file to operate on
- line: The line number (1-based, as shown in editors)
- character: The character offset (1-based, as shown in editors)

Note: LSP servers must be configured for the file type. If no server is available, an error will be returned.");

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            operation = new
            {
                type = "string",
                @enum = new[] { "goToDefinition", "findReferences", "hover", "documentSymbol", "workspaceSymbol", "goToImplementation" },
                description = "The LSP operation to perform"
            },
            filePath = new
            {
                type = "string",
                description = "The path to the file"
            },
            line = new
            {
                type = "integer",
                description = "The line number (1-based)"
            },
            character = new
            {
                type = "integer",
                description = "The character offset (1-based)"
            },
            query = new
            {
                type = "string",
                description = "Search query for workspaceSymbol operation"
            }
        },
        required = new[] { "operation", "filePath" }
    };

    public async Task<ToolResult> ExecuteAsync(string arguments, ToolContext context)
    {
        try
        {
            var args = JsonSerializer.Deserialize<LspArgs>(arguments, JsonHelper.Options);
            
            if (string.IsNullOrEmpty(args?.Operation))
                return new ToolResult(false, "operation is required");

            if (string.IsNullOrEmpty(args.FilePath))
                return new ToolResult(false, "filePath is required");

            var filePath = Path.IsPathRooted(args.FilePath)
                ? args.FilePath
                : Path.Combine(context.WorkingDirectory, args.FilePath);

            if (!_lspService.HasServerForFile(filePath))
                return new ToolResult(false, $"No LSP server available for this file type: {Path.GetExtension(filePath)}");

            if (!File.Exists(filePath))
                return new ToolResult(false, $"File not found: {filePath}");

            var line = args.Line ?? 1;
            var character = args.Character ?? 1;
            var workspaceRoot = context.WorkingDirectory;

            string? result = args.Operation switch
            {
                "goToDefinition" => await _lspService.DefinitionAsync(filePath, line, character, workspaceRoot),
                "findReferences" => await _lspService.ReferencesAsync(filePath, line, character, workspaceRoot),
                "hover" => await _lspService.HoverAsync(filePath, line, character, workspaceRoot),
                "documentSymbol" => await _lspService.DocumentSymbolAsync(filePath, workspaceRoot),
                "workspaceSymbol" => await _lspService.WorkspaceSymbolAsync(args.Query ?? "", workspaceRoot),
                "goToImplementation" => await _lspService.ImplementationAsync(filePath, line, character, workspaceRoot),
                _ => null
            };

            if (result == null)
                return new ToolResult(true, $"No results found for {args.Operation}");

            return new ToolResult(true, FormatResult(args.Operation, result, workspaceRoot));
        }
        catch (TimeoutException)
        {
            return new ToolResult(false, "LSP request timed out. The language server may be slow or unresponsive.");
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"LSP error: {ex.Message}");
        }
    }

    private static string FormatResult(string operation, string json, string workspaceRoot)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (operation == "hover")
            {
                return json;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var item in root.EnumerateArray())
                {
                    items.Add(FormatLocation(item, workspaceRoot));
                }
                return items.Count > 0 
                    ? string.Join("\n", items) 
                    : "No results found";
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                return FormatLocation(root, workspaceRoot);
            }

            return json;
        }
        catch
        {
            return json;
        }
    }

    private static string FormatLocation(JsonElement element, string workspaceRoot)
    {
        if (element.TryGetProperty("uri", out var uri))
        {
            var filePath = new Uri(uri.GetString() ?? "").LocalPath;
            var relativePath = Path.GetRelativePath(workspaceRoot, filePath);
            
            if (element.TryGetProperty("range", out var range))
            {
                var start = range.GetProperty("start");
                var line = start.GetProperty("line").GetInt32() + 1;
                var col = start.GetProperty("character").GetInt32() + 1;
                return $"{relativePath}:{line}:{col}";
            }
            
            return relativePath;
        }

        if (element.TryGetProperty("location", out var location))
        {
            return FormatLocation(location, workspaceRoot);
        }

        if (element.TryGetProperty("name", out var name))
        {
            var symbolName = name.GetString();
            if (element.TryGetProperty("kind", out var kind))
            {
                var kindName = GetSymbolKindName(kind.GetInt32());
                
                if (element.TryGetProperty("location", out var loc))
                {
                    return $"{kindName} {symbolName} - {FormatLocation(loc, workspaceRoot)}";
                }
                
                if (element.TryGetProperty("range", out var symbolRange))
                {
                    var start = symbolRange.GetProperty("start");
                    var line = start.GetProperty("line").GetInt32() + 1;
                    return $"{kindName} {symbolName} (line {line})";
                }
                
                return $"{kindName} {symbolName}";
            }
            return symbolName ?? element.GetRawText();
        }

        return element.GetRawText();
    }

    private static string GetSymbolKindName(int kind) => kind switch
    {
        1 => "File",
        2 => "Module",
        3 => "Namespace",
        4 => "Package",
        5 => "Class",
        6 => "Method",
        7 => "Property",
        8 => "Field",
        9 => "Constructor",
        10 => "Enum",
        11 => "Interface",
        12 => "Function",
        13 => "Variable",
        14 => "Constant",
        15 => "String",
        16 => "Number",
        17 => "Boolean",
        18 => "Array",
        19 => "Object",
        20 => "Key",
        21 => "Null",
        22 => "EnumMember",
        23 => "Struct",
        24 => "Event",
        25 => "Operator",
        26 => "TypeParameter",
        _ => "Symbol"
    };

    private record LspArgs(
        [property: JsonPropertyName("operation")] string? Operation,
        [property: JsonPropertyName("filePath")] string? FilePath,
        [property: JsonPropertyName("line")] int? Line,
        [property: JsonPropertyName("character")] int? Character,
        [property: JsonPropertyName("query")] string? Query
    );
}
