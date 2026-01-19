using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace OpenFork.Cli.Tui;

/// <summary>
/// Layout constants for consistent spacing and dimensions across the UI.
/// </summary>
public static class Layout
{
    // Main structure
    public const int MenuBarHeight = 1;
    public const int StatusBarHeight = 1;
    public const int ContextPanelHeight = 3;

    // Chat view splits
    public const int ChatHistoryWidthPercent = 70;
    public const int StatusPanelWidthPercent = 30;
    public const int InputAreaHeight = 3;

    // Padding
    public const int FramePaddingX = 1;
    public const int FramePaddingY = 0;
    public const int ButtonSpacing = 2;

    // List views
    public const int ListItemPaddingLeft = 2;

    // Tool output
    public const int MaxToolOutputLines = 50;
    public const int MaxCodeBlockLines = 30;
    public const int MaxDiffLines = 40;
}

/// <summary>
/// Dark Professional color theme with blue/green accents.
/// </summary>
public static class Theme
{
    // Background colors
    public static readonly Color BaseBg = Color.Black;
    public static readonly Color PanelBg = Color.Black;
    public static readonly Color ActiveBg = Color.Black;  // Black everywhere

    // Foreground colors
    public static readonly Color PrimaryFg = Color.White;
    public static readonly Color SecondaryFg = Color.Gray;
    public static readonly Color MutedFg = Color.DarkGray;

    // Accent colors
    public static readonly Color AccentPrimary = Color.BrightCyan;
    public static readonly Color AccentSecondary = Color.BrightBlue;
    public static readonly Color Success = Color.BrightGreen;
    public static readonly Color Error = Color.BrightRed;
    public static readonly Color Warning = Color.BrightYellow;
    public static readonly Color Info = Color.BrightMagenta;

    // Syntax highlighting colors
    public static readonly Color SyntaxKeyword = Color.BrightMagenta;
    public static readonly Color SyntaxString = Color.BrightGreen;
    public static readonly Color SyntaxNumber = Color.BrightCyan;
    public static readonly Color SyntaxComment = Color.Gray;
    public static readonly Color SyntaxFunction = Color.BrightYellow;
    public static readonly Color SyntaxType = Color.BrightBlue;

    // Attributes (foreground/background pairs)
    public static readonly Attribute Normal = new(PrimaryFg, BaseBg);
    public static readonly Attribute Muted = new(MutedFg, BaseBg);
    public static readonly Attribute Secondary = new(SecondaryFg, BaseBg);
    public static readonly Attribute Focused = new(AccentPrimary, BaseBg);  // Accent text on black bg
    public static readonly Attribute Selected = new(BaseBg, AccentPrimary);
    public static readonly Attribute Accent = new(AccentPrimary, BaseBg);
    public static readonly Attribute AccentFocused = new(BaseBg, AccentPrimary);
    public static readonly Attribute SuccessAttr = new(Success, BaseBg);
    public static readonly Attribute ErrorAttr = new(Error, BaseBg);
    public static readonly Attribute WarningAttr = new(Warning, BaseBg);
    public static readonly Attribute InfoAttr = new(Info, BaseBg);

    // Frame/border attributes
    public static readonly Attribute FrameBorder = new(AccentSecondary, BaseBg);
    public static readonly Attribute FrameTitle = new(AccentPrimary, BaseBg);

    /// <summary>
    /// Pre-defined ColorSchemes for different component types.
    /// </summary>
    public static class Schemes
    {
        /// <summary>Base scheme for most components</summary>
        public static readonly ColorScheme Base = new()
        {
            Normal = Theme.Normal,
            Focus = Theme.Focused,
            HotNormal = Theme.Accent,
            HotFocus = Theme.AccentFocused,
            Disabled = Theme.Normal  // Use white even when disabled (for read-only TextViews)
        };

        /// <summary>Scheme for chat content - always white text for readability</summary>
        public static readonly ColorScheme Chat = new()
        {
            Normal = Theme.Normal,    // White on black
            Focus = Theme.Normal,     // White on black (keep readable when focused)
            HotNormal = Theme.Normal,
            HotFocus = Theme.Normal,
            Disabled = Theme.Normal   // White even when read-only
        };

        /// <summary>Scheme for panels and frames</summary>
        public static readonly ColorScheme Panel = new()
        {
            Normal = new Attribute(SecondaryFg, BaseBg),
            Focus = Theme.Focused,
            HotNormal = new Attribute(AccentSecondary, BaseBg),
            HotFocus = new Attribute(BaseBg, AccentSecondary),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for menu bar</summary>
        public static readonly ColorScheme Menu = new()
        {
            Normal = new Attribute(PrimaryFg, BaseBg),
            Focus = new Attribute(BaseBg, AccentPrimary),
            HotNormal = new Attribute(AccentPrimary, BaseBg),
            HotFocus = new Attribute(BaseBg, AccentPrimary),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for status bar</summary>
        public static readonly ColorScheme StatusBar = new()
        {
            Normal = new Attribute(SecondaryFg, BaseBg),
            Focus = new Attribute(PrimaryFg, BaseBg),
            HotNormal = new Attribute(AccentPrimary, BaseBg),
            HotFocus = new Attribute(AccentPrimary, BaseBg),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for list views with selection</summary>
        public static readonly ColorScheme List = new()
        {
            Normal = Theme.Normal,
            Focus = new Attribute(BaseBg, AccentPrimary),
            HotNormal = Theme.Accent,
            HotFocus = new Attribute(BaseBg, AccentPrimary),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for buttons</summary>
        public static readonly ColorScheme Button = new()
        {
            Normal = new Attribute(PrimaryFg, BaseBg),
            Focus = new Attribute(BaseBg, AccentPrimary),
            HotNormal = new Attribute(AccentPrimary, BaseBg),
            HotFocus = new Attribute(BaseBg, AccentPrimary),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for text input fields</summary>
        public static readonly ColorScheme Input = new()
        {
            Normal = new Attribute(PrimaryFg, BaseBg),
            Focus = new Attribute(AccentPrimary, BaseBg),  // Accent text on black bg when focused
            HotNormal = Theme.Accent,
            HotFocus = Theme.AccentFocused,
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for success messages</summary>
        public static readonly ColorScheme SuccessScheme = new()
        {
            Normal = Theme.SuccessAttr,
            Focus = new Attribute(BaseBg, Success),
            HotNormal = Theme.SuccessAttr,
            HotFocus = new Attribute(BaseBg, Success),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for error messages</summary>
        public static readonly ColorScheme ErrorScheme = new()
        {
            Normal = Theme.ErrorAttr,
            Focus = new Attribute(BaseBg, Error),
            HotNormal = Theme.ErrorAttr,
            HotFocus = new Attribute(BaseBg, Error),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for warning messages</summary>
        public static readonly ColorScheme WarningScheme = new()
        {
            Normal = Theme.WarningAttr,
            Focus = new Attribute(BaseBg, Warning),
            HotNormal = Theme.WarningAttr,
            HotFocus = new Attribute(BaseBg, Warning),
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for dialog boxes</summary>
        public static readonly ColorScheme Dialog = new()
        {
            Normal = Theme.Normal,
            Focus = Theme.Focused,
            HotNormal = Theme.Accent,
            HotFocus = Theme.AccentFocused,
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for muted/secondary text</summary>
        public static readonly ColorScheme Muted = new()
        {
            Normal = Theme.Muted,
            Focus = Theme.Secondary,
            HotNormal = Theme.Muted,
            HotFocus = Theme.Secondary,
            Disabled = Theme.Muted
        };

        /// <summary>Scheme for code blocks</summary>
        public static readonly ColorScheme Code = new()
        {
            Normal = Theme.Normal,
            Focus = Theme.Focused,
            HotNormal = Theme.Normal,
            HotFocus = Theme.Focused,
            Disabled = Theme.Muted
        };
    }
}

/// <summary>
/// Unicode icons for consistent visual indicators.
/// </summary>
public static class Icons
{
    // Entity icons
    public const string Folder = "📁";
    public const string File = "📄";
    public const string Project = "📦";
    public const string Session = "💬";
    public const string Agent = "🤖";
    public const string Pipeline = "⚡";

    // Action icons
    public const string Add = "➕";
    public const string Edit = "✏️";
    public const string Delete = "🗑️";
    public const string Back = "⬅️";
    public const string Forward = "➡️";

    // Status icons
    public const string Check = "✅";
    public const string Cross = "❌";
    public const string Pending = "⏳";
    public const string InProgress = "🔄";
    public const string Warning = "⚠️";
    public const string Info = "ℹ️";

    // Role icons
    public const string User = "👤";
    public const string Assistant = "🤖";
    public const string System = "⚙️";

    // Tool icons
    public const string Tool = "🔧";
    public const string Read = "📖";
    public const string Write = "✏️";
    public const string Search = "🔍";
    public const string Bash = "💻";
    public const string Web = "🌐";
    public const string Code = "📝";
    public const string Build = "🔨";

    // Spinner frames for activity indication
    public static readonly string[] SpinnerFrames = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    // Progress bar characters
    public const char ProgressFilled = '█';
    public const char ProgressEmpty = '░';
    public const char ProgressPartial = '▒';

    // Box drawing characters
    public const string BoxTopLeft = "╭";
    public const string BoxTopRight = "╮";
    public const string BoxBottomLeft = "╰";
    public const string BoxBottomRight = "╯";
    public const string BoxHorizontal = "─";
    public const string BoxVertical = "│";
}

/// <summary>
/// Helper methods for creating styled dialogs.
/// </summary>
public static class DialogHelpers
{
    public static string? PromptText(string title, string prompt, string defaultValue = "", bool required = false)
    {
        var dialog = new Dialog(title)
        {
            ColorScheme = Theme.Schemes.Dialog
        };

        var label = new Label(prompt)
        {
            X = 1,
            Y = 1,
            ColorScheme = Theme.Schemes.Base
        };

        var textField = new TextField(defaultValue)
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 1,
            ColorScheme = Theme.Schemes.Input
        };

        var ok = new Button("_OK") { IsDefault = true, ColorScheme = Theme.Schemes.Button };
        var cancel = new Button("_Cancel") { ColorScheme = Theme.Schemes.Button };

        string? result = null;

        ok.Clicked += () =>
        {
            var text = textField.Text.ToString();
            if (required && string.IsNullOrWhiteSpace(text))
            {
                MessageBox.ErrorQuery("Error", "This field is required", "OK");
                return;
            }
            result = text;
            Application.RequestStop();
        };

        cancel.Clicked += () =>
        {
            result = null;
            Application.RequestStop();
        };

        dialog.Add(label, textField);
        dialog.AddButton(ok);
        dialog.AddButton(cancel);

        Application.Run(dialog);

        return result;
    }

    public static T? PromptSelection<T>(string title, IEnumerable<T> choices, Func<T, string>? formatter = null) where T : class
    {
        formatter ??= (t => t?.ToString() ?? "");

        var dialog = new Dialog(title)
        {
            ColorScheme = Theme.Schemes.Dialog
        };

        var choicesList = choices.ToList();
        var listView = new ListView()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill() - 2,
            AllowsMarking = false,
            ColorScheme = Theme.Schemes.List
        };

        listView.SetSource(choicesList.Select(formatter).ToList());

        T? selected = null;
        var ok = new Button("_OK") { IsDefault = true, ColorScheme = Theme.Schemes.Button };
        var cancel = new Button("_Cancel") { ColorScheme = Theme.Schemes.Button };

        ok.Clicked += () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < choicesList.Count)
            {
                selected = choicesList[listView.SelectedItem];
            }
            Application.RequestStop();
        };

        cancel.Clicked += () => Application.RequestStop();

        listView.OpenSelectedItem += (e) =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < choicesList.Count)
            {
                selected = choicesList[listView.SelectedItem];
                Application.RequestStop();
            }
        };

        dialog.Add(listView);
        dialog.AddButton(ok);
        dialog.AddButton(cancel);

        Application.Run(dialog);

        return selected;
    }

    public static bool Confirm(string title, string message)
    {
        var result = MessageBox.Query(title, message, "Yes", "No");
        return result == 0;
    }

    /// <summary>
    /// Shows an interactive dialog for entering pipeline tool parameters.
    /// Returns a dictionary of parameter values, or null if cancelled.
    /// </summary>
    public static Dictionary<string, string>? PromptPipelineToolParameters(
        string toolName,
        string toolDescription,
        List<string> requiredParams,
        Dictionary<string, string> paramDescriptions)
    {
        var dialog = new Dialog($"⚡ /{toolName}")
        {
            ColorScheme = Theme.Schemes.Dialog,
            Width = Dim.Percent(80),
            Height = Dim.Percent(80)
        };

        // Description
        var descLabel = new Label($"📋 {toolDescription}")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill() - 2,
            ColorScheme = Theme.Schemes.Muted
        };
        dialog.Add(descLabel);

        // Separator
        var sepLabel = new Label(new string('─', 60))
        {
            X = 1,
            Y = 1,
            ColorScheme = Theme.Schemes.Muted
        };
        dialog.Add(sepLabel);

        // Create scrollable view for parameters if there are many
        var scrollView = new ScrollView()
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 4,
            ContentSize = new Size(200, paramDescriptions.Count * 4 + 2),
            ShowVerticalScrollIndicator = true,
            ColorScheme = Theme.Schemes.Base
        };

        var paramFields = new Dictionary<string, TextField>();
        int y = 0;

        foreach (var (paramName, description) in paramDescriptions.OrderByDescending(p => requiredParams.Contains(p.Key)))
        {
            var isRequired = requiredParams.Contains(paramName);
            var requiredMarker = isRequired ? " *" : "";
            var icon = isRequired ? "🔴" : "⚪";

            // Parameter name label
            var paramLabel = new Label($"{icon} {paramName}{requiredMarker}:")
            {
                X = 0,
                Y = y,
                ColorScheme = isRequired ? Theme.Schemes.WarningScheme : Theme.Schemes.Base
            };
            scrollView.Add(paramLabel);

            // Description label
            var paramDescLabel = new Label($"   {description}")
            {
                X = 0,
                Y = y + 1,
                Width = Dim.Fill(),
                ColorScheme = Theme.Schemes.Muted
            };
            scrollView.Add(paramDescLabel);

            // Input field
            var paramField = new TextField("")
            {
                X = 0,
                Y = y + 2,
                Width = Dim.Fill() - 2,
                ColorScheme = Theme.Schemes.Input
            };
            scrollView.Add(paramField);
            paramFields[paramName] = paramField;

            y += 4;
        }

        dialog.Add(scrollView);

        // Hint at bottom
        var hintLabel = new Label("🔴 = Required | Press Tab to move between fields")
        {
            X = 1,
            Y = Pos.AnchorEnd(3),
            ColorScheme = Theme.Schemes.Muted
        };
        dialog.Add(hintLabel);

        // Buttons
        var executeButton = new Button("▶️ _Execute") { IsDefault = true, ColorScheme = Theme.Schemes.Button };
        var cancelButton = new Button("_Cancel") { ColorScheme = Theme.Schemes.Button };

        Dictionary<string, string>? result = null;

        executeButton.Clicked += () =>
        {
            // Validate required parameters
            var missingRequired = requiredParams
                .Where(p => paramFields.ContainsKey(p) &&
                            string.IsNullOrWhiteSpace(paramFields[p].Text?.ToString()))
                .ToList();

            if (missingRequired.Count > 0)
            {
                MessageBox.ErrorQuery("Missing Required Parameters",
                    $"Please fill in the required parameters:\n• {string.Join("\n• ", missingRequired)}",
                    "OK");
                return;
            }

            // Collect values
            result = new Dictionary<string, string>();
            foreach (var (paramName, field) in paramFields)
            {
                var value = field.Text?.ToString() ?? "";
                if (!string.IsNullOrEmpty(value))
                {
                    result[paramName] = value;
                }
            }

            Application.RequestStop();
        };

        cancelButton.Clicked += () =>
        {
            result = null;
            Application.RequestStop();
        };

        dialog.AddButton(executeButton);
        dialog.AddButton(cancelButton);

        // Focus first field
        if (paramFields.Count > 0)
        {
            paramFields.First().Value.SetFocus();
        }

        Application.Run(dialog);

        return result;
    }

    /// <summary>
    /// Shows an autocomplete selection dialog for pipeline tools.
    /// Returns the selected tool name, or null if cancelled.
    /// </summary>
    public static string? PromptToolAutocomplete(IEnumerable<(string Name, string Description)> tools, string filter = "")
    {
        var toolsList = tools.ToList();

        if (toolsList.Count == 0)
        {
            MessageBox.Query("No Tools", "No pipeline tools are available.", "OK");
            return null;
        }

        if (toolsList.Count == 1)
        {
            // Only one match, return it directly
            return toolsList[0].Name;
        }

        var dialog = new Dialog("⚡ Select Pipeline Tool")
        {
            ColorScheme = Theme.Schemes.Dialog,
            Width = Dim.Percent(60),
            Height = Dim.Percent(60)
        };

        var listView = new ListView()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 2,
            Height = Dim.Fill() - 3,
            ColorScheme = Theme.Schemes.List
        };

        var displayItems = toolsList.Select(t => $"/{t.Name} - {t.Description}").ToList();
        listView.SetSource(displayItems);

        string? result = null;

        listView.OpenSelectedItem += (e) =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < toolsList.Count)
            {
                result = toolsList[listView.SelectedItem].Name;
                Application.RequestStop();
            }
        };

        var selectButton = new Button("_Select") { IsDefault = true, ColorScheme = Theme.Schemes.Button };
        var cancelButton = new Button("_Cancel") { ColorScheme = Theme.Schemes.Button };

        selectButton.Clicked += () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < toolsList.Count)
            {
                result = toolsList[listView.SelectedItem].Name;
            }
            Application.RequestStop();
        };

        cancelButton.Clicked += () =>
        {
            result = null;
            Application.RequestStop();
        };

        dialog.Add(listView);
        dialog.AddButton(selectButton);
        dialog.AddButton(cancelButton);

        Application.Run(dialog);

        return result;
    }
}

/// <summary>
/// Helper methods for creating styled frames and showing messages.
/// </summary>
public static class FrameHelpers
{
    public static FrameView CreateFrame(string? title, View content, ColorScheme? scheme = null)
    {
        var frame = new FrameView(title ?? "")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = scheme ?? Theme.Schemes.Panel
        };

        frame.Add(content);
        return frame;
    }

    public static FrameView CreateStyledFrame(string title)
    {
        return new FrameView(title)
        {
            ColorScheme = Theme.Schemes.Panel
        };
    }

    public static void ShowError(string message)
    {
        MessageBox.ErrorQuery("Error", message, "OK");
    }

    public static void ShowSuccess(string message)
    {
        MessageBox.Query("Success", message, "OK");
    }

    public static void ShowInfo(string message)
    {
        MessageBox.Query("Info", message, "OK");
    }

    public static void ShowWarning(string message)
    {
        MessageBox.Query("Warning", message, "OK");
    }
}

/// <summary>
/// Helper methods for creating styled tables.
/// </summary>
public static class TableHelpers
{
    public static TableView CreateTable()
    {
        var table = new TableView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            FullRowSelect = true,
            ColorScheme = Theme.Schemes.List
        };

        table.Style.ShowHorizontalHeaderUnderline = true;
        table.Style.ShowVerticalCellLines = false;
        table.Style.ShowHorizontalHeaderOverline = false;
        table.Style.AlwaysShowHeaders = true;

        return table;
    }

    public static void SetupColumns(TableView table, params string[] columnNames)
    {
        var dt = new System.Data.DataTable();
        foreach (var name in columnNames)
        {
            dt.Columns.Add(name);
        }
        table.Table = dt;
    }

    public static void AddRow(TableView table, params object[] values)
    {
        if (table.Table is System.Data.DataTable dt)
        {
            dt.Rows.Add(values);
        }
    }
}

/// <summary>
/// Helper methods for progress dialogs.
/// </summary>
public static class ProgressHelpers
{
    public static async Task<T> RunAsync<T>(string message, Func<Task<T>> action)
    {
        T result = default!;
        Exception? exception = null;

        var dialog = new Dialog(message)
        {
            Width = 60,
            Height = 5,
            ColorScheme = Theme.Schemes.Dialog
        };

        var progressBar = new ProgressBar()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1,
            ColorScheme = Theme.Schemes.Base
        };
        progressBar.Fraction = 0.5f;

        dialog.Add(progressBar);

        _ = Task.Run(async () =>
        {
            try
            {
                result = await action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Application.MainLoop.Invoke(() => Application.RequestStop());
            }
        });

        Application.Run(dialog);

        if (exception != null)
            throw exception;

        return result;
    }

    public static async Task RunAsync(string message, Func<Task> action)
    {
        await RunAsync<object?>(message, async () =>
        {
            await action();
            return null;
        });
    }

    /// <summary>
    /// Creates a styled progress bar.
    /// </summary>
    public static ProgressBar CreateProgressBar(int width = 40)
    {
        return new ProgressBar()
        {
            Width = width,
            Height = 1,
            ColorScheme = Theme.Schemes.Base
        };
    }

    /// <summary>
    /// Renders a text-based progress bar.
    /// </summary>
    public static string RenderProgressText(float fraction, int width = 20)
    {
        var filled = (int)(fraction * width);
        var empty = width - filled;
        return new string(Icons.ProgressFilled, filled) + new string(Icons.ProgressEmpty, empty);
    }
}

/// <summary>
/// Helper methods for multiline text input.
/// </summary>
public static class TextViewHelpers
{
    public static string PromptMultiline(string title, string prompt, string defaultValue = "")
    {
        var dialog = new Dialog(title)
        {
            Width = Dim.Percent(80),
            Height = Dim.Percent(80),
            ColorScheme = Theme.Schemes.Dialog
        };

        var label = new Label(prompt)
        {
            X = 1,
            Y = 1,
            ColorScheme = Theme.Schemes.Base
        };

        var textView = new TextView()
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill() - 2,
            Text = defaultValue,
            ColorScheme = Theme.Schemes.Input
        };

        var ok = new Button("_OK") { IsDefault = true, ColorScheme = Theme.Schemes.Button };
        var cancel = new Button("_Cancel") { ColorScheme = Theme.Schemes.Button };

        string result = defaultValue;

        ok.Clicked += () =>
        {
            result = textView.Text.ToString() ?? "";
            Application.RequestStop();
        };

        cancel.Clicked += () => Application.RequestStop();

        dialog.Add(label, textView);
        dialog.AddButton(ok);
        dialog.AddButton(cancel);

        Application.Run(dialog);

        return result;
    }
}

/// <summary>
/// Helper methods for applying themes to views.
/// </summary>
public static class ThemeHelper
{
    /// <summary>
    /// Applies a color scheme to a view and optionally all its subviews.
    /// </summary>
    public static void ApplyScheme(View view, ColorScheme scheme, bool recursive = false)
    {
        view.ColorScheme = scheme;

        if (recursive)
        {
            foreach (var subview in view.Subviews)
            {
                ApplyScheme(subview, scheme, true);
            }
        }
    }

    /// <summary>
    /// Creates a styled label with the specified color.
    /// </summary>
    public static Label CreateLabel(string text, Attribute color)
    {
        return new Label(text)
        {
            ColorScheme = new ColorScheme { Normal = color }
        };
    }

    /// <summary>
    /// Creates a styled label with theme color.
    /// </summary>
    public static Label CreateAccentLabel(string text) => CreateLabel(text, Theme.Accent);
    public static Label CreateSuccessLabel(string text) => CreateLabel(text, Theme.SuccessAttr);
    public static Label CreateErrorLabel(string text) => CreateLabel(text, Theme.ErrorAttr);
    public static Label CreateWarningLabel(string text) => CreateLabel(text, Theme.WarningAttr);
    public static Label CreateMutedLabel(string text) => CreateLabel(text, Theme.Muted);

    /// <summary>
    /// Formats a status indicator with appropriate icon and color.
    /// </summary>
    public static (string icon, Attribute color) GetStatusIndicator(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "completed" or "success" or "done" => (Icons.Check, Theme.SuccessAttr),
            "error" or "failed" => (Icons.Cross, Theme.ErrorAttr),
            "warning" => (Icons.Warning, Theme.WarningAttr),
            "in_progress" or "running" => (Icons.InProgress, Theme.WarningAttr),
            _ => (Icons.Pending, Theme.Muted)
        };
    }
}
