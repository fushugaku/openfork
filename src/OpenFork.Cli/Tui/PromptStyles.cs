using Spectre.Console;
using Spectre.Console.Rendering;

namespace OpenFork.Cli.Tui;

public static class Theme
{
    public static readonly Color Primary = Color.Teal;
    public static readonly Color Secondary = Color.SteelBlue;
    public static readonly Color Accent = Color.Orange1;
    public static readonly Color Success = Color.Green3_1;
    public static readonly Color Error = Color.Red3_1;
    public static readonly Color Warning = Color.Yellow3_1;
    public static readonly Color Muted = Color.Grey;
    public static readonly Color Surface = Color.Grey23;

    public static readonly Style PrimaryStyle = new(Primary);
    public static readonly Style SecondaryStyle = new(Secondary);
    public static readonly Style AccentStyle = new(Accent);
    public static readonly Style SuccessStyle = new(Success);
    public static readonly Style ErrorStyle = new(Error);
    public static readonly Style WarningStyle = new(Warning);
    public static readonly Style MutedStyle = new(Muted);
}

public static class Icons
{
    public const string Folder = "📁";
    public const string File = "📄";
    public const string Project = "📦";
    public const string Session = "💬";
    public const string Agent = "🤖";
    public const string Pipeline = "⚡";
    public const string Add = "➕";
    public const string Back = "←";
    public const string Check = "✓";
    public const string Chat = "💭";
    public const string Settings = "⚙️";
    public const string User = "👤";
    public const string System = "🔧";
    public const string Assistant = "🤖";
}

public static class Prompts
{
    public static TextPrompt<string> RequiredText(string prompt)
    {
        return new TextPrompt<string>($"[{Theme.Primary.ToMarkup()}]{prompt}[/]")
            .PromptStyle(Theme.AccentStyle)
            .ValidationErrorMessage($"[{Theme.Error.ToMarkup()}]This field is required[/]")
            .Validate(input =>
            {
                if (string.IsNullOrWhiteSpace(input))
                    return ValidationResult.Error();
                return ValidationResult.Success();
            });
    }

    public static TextPrompt<string> OptionalText(string prompt, string defaultValue = "")
    {
        return new TextPrompt<string>($"[{Theme.Primary.ToMarkup()}]{prompt}[/]")
            .PromptStyle(Theme.AccentStyle)
            .DefaultValue(defaultValue)
            .AllowEmpty();
    }

    public static TextPrompt<string> MultilineText(string prompt)
    {
        return new TextPrompt<string>($"[{Theme.Primary.ToMarkup()}]{prompt}[/] [grey](empty line to finish)[/]")
            .PromptStyle(Theme.AccentStyle)
            .AllowEmpty();
    }

    public static TextPrompt<int> Number(string prompt, int defaultValue)
    {
        return new TextPrompt<int>($"[{Theme.Primary.ToMarkup()}]{prompt}[/]")
            .PromptStyle(Theme.AccentStyle)
            .DefaultValue(defaultValue);
    }

    public static SelectionPrompt<T> Selection<T>(string title) where T : notnull
    {
        return new SelectionPrompt<T>()
            .Title($"[{Theme.Primary.ToMarkup()}]{title}[/]")
            .PageSize(12)
            .WrapAround(true)
            .HighlightStyle(Theme.AccentStyle)
            .MoreChoicesText($"[{Theme.Muted.ToMarkup()}]↑↓ navigate | Ctrl+C to exit[/]");
    }

    public static MultiSelectionPrompt<T> MultiSelection<T>(string title) where T : notnull
    {
        return new MultiSelectionPrompt<T>()
            .Title($"[{Theme.Primary.ToMarkup()}]{title}[/]")
            .PageSize(12)
            .WrapAround(true)
            .HighlightStyle(Theme.AccentStyle)
            .InstructionsText($"[{Theme.Muted.ToMarkup()}]Space to toggle, Enter to confirm, Ctrl+C to exit[/]")
            .MoreChoicesText($"[{Theme.Muted.ToMarkup()}]↑↓ navigate[/]");
    }

    public static ConfirmationPrompt Confirm(string prompt)
    {
        return new ConfirmationPrompt($"[{Theme.Primary.ToMarkup()}]{prompt}[/]")
            .Yes('y')
            .No('n');
    }
}

public static class Panels
{
    public static Panel Create<T>(T content, string? header = null) where T : IRenderable
    {
        var panel = new Panel(content)
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Secondary);

        if (header != null)
            panel.Header($"[{Theme.Primary.ToMarkup()}]{header}[/]");

        return panel;
    }

    public static Panel Create(string content, string? header = null)
    {
        var panel = new Panel(new Markup(Markup.Escape(content)))
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Secondary);

        if (header != null)
            panel.Header($"[{Theme.Primary.ToMarkup()}]{header}[/]");

        return panel;
    }

    public static Panel Error(string message)
    {
        return new Panel(new Markup($"[{Theme.Error.ToMarkup()}]{Markup.Escape(message)}[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Error)
            .Header($"[{Theme.Error.ToMarkup()}]Error[/]");
    }

    public static Panel Success(string message)
    {
        return new Panel(new Markup($"[{Theme.Success.ToMarkup()}]{Markup.Escape(message)}[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Success)
            .Header($"[{Theme.Success.ToMarkup()}]Success[/]");
    }

    public static Panel Info(string message)
    {
        return new Panel(new Markup($"[{Theme.Muted.ToMarkup()}]{Markup.Escape(message)}[/]"))
            .Border(BoxBorder.Rounded)
            .BorderColor(Theme.Muted);
    }
}

public static class Tables
{
    public static Table Create(params string[] columns)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Theme.Secondary);

        foreach (var col in columns)
        {
            table.AddColumn($"[{Theme.Primary.ToMarkup()}]{col}[/]");
        }

        return table;
    }
}

public static class StatusSpinner
{
    public static async Task<T> RunAsync<T>(string message, Func<Task<T>> action)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Theme.AccentStyle)
            .StartAsync($"[{Theme.Muted.ToMarkup()}]{message}[/]", async _ => await action());
    }

    public static async Task RunAsync(string message, Func<Task> action)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Theme.AccentStyle)
            .StartAsync($"[{Theme.Muted.ToMarkup()}]{message}[/]", async _ => await action());
    }
}

public static class MultilineInput
{
    public static string Read(string prompt)
    {
        AnsiConsole.MarkupLine($"[{Theme.Primary.ToMarkup()}]{prompt}[/]");
        AnsiConsole.MarkupLine($"[{Theme.Muted.ToMarkup()}]Enter text (empty line to finish):[/]");

        var lines = new List<string>();
        while (true)
        {
            var line = Console.ReadLine();
            if (string.IsNullOrEmpty(line))
                break;
            lines.Add(line);
        }

        return string.Join("\n", lines);
    }

    public static string ReadWithDefault(string prompt, string defaultValue)
    {
        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            AnsiConsole.MarkupLine($"[{Theme.Muted.ToMarkup()}]Current value:[/]");
            AnsiConsole.Write(Panels.Info(defaultValue));
        }

        var editChoice = AnsiConsole.Prompt(
            Prompts.Selection<string>("Edit system prompt?")
                .AddChoices("Keep current", "Replace"));

        if (editChoice == "Keep current")
            return defaultValue;

        return Read(prompt);
    }
}
