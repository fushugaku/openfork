using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace OpenFork.Cli.Tui;

public static class ToolOutputRenderer
{
    public static IRenderable Render(string toolName, string output, bool success)
    {
        if (!success)
        {
            return new Panel(new Markup($"[{Theme.Error.ToMarkup()}]{Markup.Escape(output)}[/]"))
                .Header($"[{Theme.Error.ToMarkup()}]⚠️ {toolName} - Error[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Theme.Error);
        }

        return toolName switch
        {
            "read" => RenderReadFile(output),
            "edit" => RenderEditFile(output),
            "multiedit" => RenderMultiEdit(output),
            "write" => RenderWriteFile(output),
            "bash" => RenderBashOutput(output),
            "glob" => RenderGlobOutput(output),
            "grep" => RenderGrepOutput(output),
            "list" => RenderListOutput(output),
            "webfetch" => RenderWebFetch(output),
            "websearch" => RenderWebSearch(output),
            "codesearch" => RenderCodeSearch(output),
            "diagnostics" => RenderDiagnostics(output),
            "lsp" => RenderLspOutput(output),
            "question" => RenderQuestion(output),
            "todowrite" => RenderTodoWrite(output),
            "todoread" => RenderTodoRead(output),
            "search_project" => RenderSearchProject(output),
            _ => RenderDefault(toolName, output)
        };
    }

    public static IRenderable RenderGroupedReads(List<string> filePaths)
    {
        var fileList = string.Join(", ", filePaths.Select(Path.GetFileName));
        
        return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]{Markup.Escape(fileList)}[/]"))
            .Header($"[{Theme.Primary.ToMarkup()}]📄 read[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderReadFile(string output)
    {
        var lines = output.Split('\n');
        
        // Extract file path from <file path="..."> tag
        var filePath = "File Content";
        if (lines.Length > 0)
        {
            var pathMatch = Regex.Match(lines[0], @"<file path=""(.+?)"">");
            if (pathMatch.Success)
            {
                filePath = pathMatch.Groups[1].Value;
            }
        }
        
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Secondary)
            .AddColumn(new TableColumn("[dim]Line[/]").RightAligned())
            .AddColumn(new TableColumn("Content"));

        var maxLines = 50;
        var displayLines = lines.Take(maxLines).ToList();
        
        foreach (var line in displayLines)
        {
            var match = Regex.Match(line, @"^\s*(\d+)\|(.*)$");
            if (match.Success)
            {
                var lineNum = match.Groups[1].Value.Trim();
                var content = match.Groups[2].Value;
                table.AddRow($"[dim]{lineNum}[/]", Markup.Escape(content));
            }
            else
            {
                table.AddRow("", Markup.Escape(line));
            }
        }

        if (lines.Length > maxLines)
        {
            table.AddRow("[dim]...[/]", $"[dim]({lines.Length - maxLines} more lines)[/]");
        }

        return new Panel(table)
            .Header($"[{Theme.Primary.ToMarkup()}]📄 {Markup.Escape(filePath)}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderEditFile(string output)
    {
        return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]{Markup.Escape(output)}[/]"))
            .Header($"[{Theme.Success.ToMarkup()}]✏️ File Edited[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Success);
    }

    private static IRenderable RenderMultiEdit(string output)
    {
        var lines = output.Split('\n');
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("");

        foreach (var line in lines)
        {
            if (line.Contains("→"))
            {
                table.AddRow($"[{Theme.Accent.ToMarkup()}]✓[/] {Markup.Escape(line)}");
            }
            else
            {
                table.AddRow(Markup.Escape(line));
            }
        }

        return new Panel(table)
            .Header($"[{Theme.Success.ToMarkup()}]✏️ Multiple Edits Applied[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Success);
    }

    private static IRenderable RenderWriteFile(string output)
    {
        return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]{Markup.Escape(output)}[/]"))
            .Header($"[{Theme.Success.ToMarkup()}]💾 File Written[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Success);
    }

    private static IRenderable RenderBashOutput(string output)
    {
        var maxLines = 100;
        var lines = output.Split('\n');
        var displayLines = lines.Take(maxLines).ToList();
        
        var content = string.Join('\n', displayLines.Select(Markup.Escape));
        if (lines.Length > maxLines)
        {
            content += $"\n[dim]... ({lines.Length - maxLines} more lines)[/]";
        }

        return new Panel(new Markup(content))
            .Header($"[{Theme.Accent.ToMarkup()}]⚡ Command Output[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Accent);
    }

    private static IRenderable RenderGlobOutput(string output)
    {
        var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var tree = new Tree($"[{Theme.Primary.ToMarkup()}]📂 Matched Files ({files.Length})[/]");

        var grouped = files.GroupBy(f => Path.GetDirectoryName(f) ?? "");
        foreach (var group in grouped.Take(20))
        {
            var dirNode = tree.AddNode($"[{Theme.Secondary.ToMarkup()}]{Markup.Escape(group.Key ?? ".")}[/]");
            foreach (var file in group.Take(50))
            {
                dirNode.AddNode($"[{Theme.Muted.ToMarkup()}]{Markup.Escape(Path.GetFileName(file))}[/]");
            }
        }

        return new Panel(tree)
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderGrepOutput(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Secondary)
            .AddColumn(new TableColumn("File:Line"))
            .AddColumn(new TableColumn("Match"));

        foreach (var line in lines.Take(50))
        {
            var match = Regex.Match(line, @"^(.+?):(\d+):(.+)$");
            if (match.Success)
            {
                var file = Path.GetFileName(match.Groups[1].Value);
                var lineNum = match.Groups[2].Value;
                var content = match.Groups[3].Value;
                table.AddRow(
                    $"[{Theme.Accent.ToMarkup()}]{Markup.Escape(file)}:{lineNum}[/]",
                    Markup.Escape(content.Trim())
                );
            }
            else
            {
                table.AddRow("", Markup.Escape(line));
            }
        }

        if (lines.Length > 50)
        {
            table.AddRow("[dim]...[/]", $"[dim]({lines.Length - 50} more matches)[/]");
        }

        return new Panel(table)
            .Header($"[{Theme.Primary.ToMarkup()}]🔍 Search Results ({lines.Length})[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderListOutput(string output)
    {
        var tree = new Tree($"[{Theme.Primary.ToMarkup()}]📁 Directory Structure[/]");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        var currentPath = new Stack<TreeNode>();
        currentPath.Push(tree.AddNode(""));

        foreach (var line in lines.Take(200))
        {
            var indent = line.TakeWhile(c => c == ' ').Count();
            var content = line.Trim();
            
            if (string.IsNullOrWhiteSpace(content)) continue;

            var isDir = content.EndsWith('/') || !content.Contains('.');
            var icon = isDir ? "📁" : "📄";
            var color = isDir ? Theme.Secondary.ToMarkup() : Theme.Muted.ToMarkup();

            var node = currentPath.Peek().AddNode($"[{color}]{icon} {Markup.Escape(content)}[/]");
            
            if (isDir && indent < 100)
            {
                while (currentPath.Count > indent / 2 + 1)
                    currentPath.Pop();
                currentPath.Push(node);
            }
        }

        return new Panel(tree)
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderWebFetch(string output)
    {
        var maxLength = 2000;
        var displayOutput = output.Length > maxLength 
            ? output[..maxLength] + $"\n\n[dim]... (truncated {output.Length - maxLength} characters)[/]"
            : output;

        return new Panel(new Markup(Markup.Escape(displayOutput)))
            .Header($"[{Theme.Primary.ToMarkup()}]🌐 Web Content[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderWebSearch(string output)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Secondary)
            .AddColumn(new TableColumn("#").Width(3))
            .AddColumn(new TableColumn("Result"));

        var items = output.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var index = 1;

        foreach (var item in items.Take(10))
        {
            var lines = item.Split('\n', 3);
            var title = lines.Length > 0 ? lines[0].Replace("**", "").Trim() : "";
            var url = lines.Length > 1 ? lines[1] : "";
            var snippet = lines.Length > 2 ? lines[2] : "";

            var content = $"[{Theme.Accent.ToMarkup()}]{Markup.Escape(title)}[/]\n" +
                         $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(url)}[/]\n" +
                         $"{Markup.Escape(snippet.Length > 150 ? snippet[..150] + "..." : snippet)}";

            table.AddRow($"[dim]{index}[/]", content);
            index++;
        }

        return new Panel(table)
            .Header($"[{Theme.Primary.ToMarkup()}]🔍 Web Search Results[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderCodeSearch(string output)
    {
        return new Panel(new Markup(Markup.Escape(output.Length > 3000 ? output[..3000] + "\n\n[dim]...(truncated)[/]" : output)))
            .Header($"[{Theme.Primary.ToMarkup()}]💻 Code Documentation[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderDiagnostics(string output)
    {
        if (output.Contains("No diagnostics found"))
        {
            return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]✓ {Markup.Escape(output)}[/]"))
                .Header($"[{Theme.Success.ToMarkup()}]🔍 Diagnostics[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Theme.Success);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Warning)
            .AddColumn(new TableColumn("File:Line"))
            .AddColumn(new TableColumn("Issue"));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("❌"))
            {
                var parts = line.Split(": ", 2);
                if (parts.Length == 2)
                {
                    table.AddRow(
                        $"[{Theme.Error.ToMarkup()}]{Markup.Escape(parts[0])}[/]",
                        Markup.Escape(parts[1])
                    );
                }
            }
            else if (line.Contains("⚠️"))
            {
                var parts = line.Split(": ", 2);
                if (parts.Length == 2)
                {
                    table.AddRow(
                        $"[{Theme.Warning.ToMarkup()}]{Markup.Escape(parts[0])}[/]",
                        Markup.Escape(parts[1])
                    );
                }
            }
            else if (!line.StartsWith("Found") && !line.StartsWith("📄"))
            {
                table.AddRow("", Markup.Escape(line));
            }
        }

        return new Panel(table)
            .Header($"[{Theme.Warning.ToMarkup()}]🔍 Diagnostics[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Warning);
    }

    private static IRenderable RenderLspOutput(string output)
    {
        return new Panel(new Markup(Markup.Escape(output)))
            .Header($"[{Theme.Primary.ToMarkup()}]🔧 LSP Results[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderQuestion(string output)
    {
        return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]{Markup.Escape(output)}[/]"))
            .Header($"[{Theme.Accent.ToMarkup()}]❓ User Response[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Accent);
    }

    private static IRenderable RenderTodoWrite(string output)
    {
        return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]{Markup.Escape(output)}[/]"))
            .Header($"[{Theme.Success.ToMarkup()}]✓ Tasks Updated[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Success);
    }

    private static IRenderable RenderTodoRead(string output)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Secondary)
            .AddColumn(new TableColumn("Status").Width(12))
            .AddColumn(new TableColumn("Task"));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("[completed]"))
            {
                var task = line.Replace("[completed]", "").Trim();
                table.AddRow($"[{Theme.Success.ToMarkup()}]✓ Done[/]", Markup.Escape(task));
            }
            else if (line.Contains("[in_progress]"))
            {
                var task = line.Replace("[in_progress]", "").Trim();
                table.AddRow($"[{Theme.Warning.ToMarkup()}]→ Active[/]", Markup.Escape(task));
            }
            else if (line.Contains("[pending]"))
            {
                var task = line.Replace("[pending]", "").Trim();
                table.AddRow($"[{Theme.Muted.ToMarkup()}]○ Pending[/]", Markup.Escape(task));
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                table.AddRow("", Markup.Escape(line));
            }
        }

        return new Panel(table)
            .Header($"[{Theme.Primary.ToMarkup()}]📋 Tasks[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderSearchProject(string output)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Secondary)
            .AddColumn(new TableColumn("File"))
            .AddColumn(new TableColumn("Relevance"))
            .AddColumn(new TableColumn("Preview"));

        var entries = output.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries.Take(15))
        {
            var lines = entry.Split('\n');
            if (lines.Length >= 2)
            {
                var file = lines[0].Replace("File:", "").Trim();
                var score = lines.FirstOrDefault(l => l.Contains("Score:"))?.Replace("Score:", "").Trim() ?? "";
                var preview = lines.Length > 2 ? string.Join(" ", lines.Skip(2)).Trim() : "";
                
                table.AddRow(
                    $"[{Theme.Accent.ToMarkup()}]{Markup.Escape(Path.GetFileName(file))}[/]\n[dim]{Markup.Escape(Path.GetDirectoryName(file) ?? "")}[/]",
                    $"[{Theme.Success.ToMarkup()}]{Markup.Escape(score)}[/]",
                    Markup.Escape(preview.Length > 100 ? preview[..100] + "..." : preview)
                );
            }
        }

        return new Panel(table)
            .Header($"[{Theme.Primary.ToMarkup()}]🔍 Semantic Search Results[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Primary);
    }

    private static IRenderable RenderDefault(string toolName, string output)
    {
        return new Panel(new Markup(Markup.Escape(output)))
            .Header($"[{Theme.Primary.ToMarkup()}]🔧 {toolName}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Secondary);
    }
}
