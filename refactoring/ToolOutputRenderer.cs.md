# Refactoring: ToolOutputRenderer.cs

## Overview
Convert tool output rendering from Spectre.Console IRenderable to formatted strings for TextView.

## Current Return Type
```csharp
public static IRenderable Render(string toolName, string output, bool success)
```

## Target Return Type
```csharp
public static string RenderToText(string toolName, string output, bool success)
```

## Approach
Since Terminal.Gui TextView doesn't support rich rendering like Spectre.Console, we'll:
1. Return formatted plain text strings
2. Use Unicode box drawing characters for structure
3. Use color codes if TextView supports them (otherwise plain)

## Example Transformation

### Before (Spectre.Console)
```csharp
return new Panel(new Markup($"[green]{Markup.Escape(output)}[/]"))
    .Header($"[cyan]✏️ File Edited[/]")
    .Border(BoxBorder.Rounded)
    .BorderColor(Theme.Success);
```

### After (Plain Text)
```csharp
return $@"
╭─ ✏️ File Edited ──────╮
│ {output}              │
╰───────────────────────╯
".Trim();
```

## Simplified Renderers
- `RenderReadFile()` - File path + line count
- `RenderEditFile()` - Success message
- `RenderMultiEdit()` - File list with checkmarks
- `RenderWriteFile()` - Success message
- `RenderBashOutput()` - Command output (truncated)
- `RenderGlobOutput()` - File count + sample files
- `RenderGrepOutput()` - Match count + sample matches
- `RenderListOutput()` - Directory structure
- `RenderCodeSearch()` - Truncated content
- `RenderDiagnostics()` - Error/warning list
- `RenderTodoWrite/Read()` - Todo status
- `RenderSearchProject()` - Result count

## Alternative: View-based Rendering
If we want richer UI, return `View` instead:

```csharp
public static View RenderToView(string toolName, string output, bool success)
{
    var frame = new FrameView($"{GetToolIcon(toolName)} {toolName}")
    {
        Width = Dim.Fill(),
        Height = Dim.Fill()
    };
    
    var textView = new TextView()
    {
        Text = output,
        ReadOnly = true,
        Width = Dim.Fill(),
        Height = Dim.Fill()
    };
    
    frame.Add(textView);
    return frame;
}
```

## Migration Decision
**Recommended:** Start with plain text approach (simpler), optionally enhance to View-based later if needed.

## Update Call Sites
In `ConsoleApp.Chat.cs`:
```csharp
// Before
var rendered = ToolOutputRenderer.Render(toolExecution.ToolName, ...);
toolOutputs.Add(rendered);

// After
var text = ToolOutputRenderer.RenderToText(toolExecution.ToolName, ...);
AppendToHistory($"\n{text}\n");
```
