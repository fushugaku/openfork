# Refactoring: PromptStyles.cs

## Overview
Convert Spectre.Console theming and UI helpers to Terminal.Gui equivalents.

## Current Structure
- `Theme` - Color definitions using `Spectre.Console.Color`
- `Icons` - Unicode icons (unchanged)
- `Prompts` - Spectre.Console prompt wrappers
- `Panels` - Panel creation helpers
- `Tables` - Table creation helpers
- `StatusSpinner` - Async loading spinner
- `MultilineInput` - Multi-line text input

## Target Structure

### 1. Theme (Color Mapping)

**Current:**
```csharp
public static class Theme
{
    public static readonly Color Primary = Color.Teal;
    public static readonly Color Secondary = Color.SteelBlue;
    // ...
}
```

**Target:**
```csharp
using Terminal.Gui;

public static class Theme
{
    public static readonly Attribute Primary = new(Color.BrightCyan, Color.Black);
    public static readonly Attribute Secondary = new(Color.BrightBlue, Color.Black);
    public static readonly Attribute Accent = new(Color.BrightYellow, Color.Black);
    public static readonly Attribute Success = new(Color.BrightGreen, Color.Black);
    public static readonly Attribute Error = new(Color.BrightRed, Color.Black);
    public static readonly Attribute Warning = new(Color.BrightYellow, Color.Black);
    public static readonly Attribute Muted = new(Color.Gray, Color.Black);
    public static readonly Attribute Surface = new(Color.DarkGray, Color.Black);
    
    // Single colors for when Attribute is not needed
    public static readonly Color PrimaryColor = Color.BrightCyan;
    public static readonly Color SecondaryColor = Color.BrightBlue;
    public static readonly Color AccentColor = Color.BrightYellow;
    public static readonly Color SuccessColor = Color.BrightGreen;
    public static readonly Color ErrorColor = Color.BrightRed;
    public static readonly Color WarningColor = Color.BrightYellow;
    public static readonly Color MutedColor = Color.Gray;
}
```

### 2. Icons (Unchanged)
Keep as-is - Terminal.Gui supports Unicode.

### 3. Prompts → DialogHelpers

**Current:**
```csharp
public static class Prompts
{
    public static TextPrompt<string> RequiredText(string prompt) { }
    public static SelectionPrompt<T> Selection<T>(string title) { }
    public static ConfirmationPrompt Confirm(string prompt) { }
}
```

**Target:**
```csharp
public static class DialogHelpers
{
    public static string? PromptText(string title, string prompt, string defaultValue = "", bool required = false)
    {
        var dialog = new Dialog(title);
        var label = new Label(prompt) { X = 1, Y = 1 };
        var textField = new TextField(defaultValue) 
        { 
            X = 1, 
            Y = 2, 
            Width = Dim.Fill() - 1 
        };
        
        var ok = new Button("OK") { IsDefault = true };
        var cancel = new Button("Cancel");
        
        ok.Clicked += () =>
        {
            if (required && string.IsNullOrWhiteSpace(textField.Text.ToString()))
            {
                MessageBox.ErrorQuery("Error", "This field is required", "OK");
                return;
            }
            Application.RequestStop();
        };
        
        cancel.Clicked += () =>
        {
            textField.Text = string.Empty;
            Application.RequestStop();
        };
        
        dialog.Add(label, textField);
        dialog.AddButton(ok);
        dialog.AddButton(cancel);
        
        Application.Run(dialog);
        
        return textField.Text.ToString();
    }
    
    public static T? PromptSelection<T>(string title, IEnumerable<T> choices, Func<T, string>? formatter = null) where T : class
    {
        formatter ??= (t => t?.ToString() ?? "");
        
        var dialog = new Dialog(title);
        var listView = new ListView(choices.ToList())
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill() - 2,
            AllowsMarking = false
        };
        
        listView.SetSource(choices.Select(formatter).ToList());
        
        T? selected = null;
        var ok = new Button("OK") { IsDefault = true };
        var cancel = new Button("Cancel");
        
        ok.Clicked += () =>
        {
            selected = choices.ElementAtOrDefault(listView.SelectedItem);
            Application.RequestStop();
        };
        
        cancel.Clicked += () => Application.RequestStop();
        
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
}
```

### 4. Panels → FrameHelpers

**Current:**
```csharp
public static class Panels
{
    public static Panel Create(string content, string? header = null) { }
    public static Panel Error(string message) { }
    public static Panel Success(string message) { }
}
```

**Target:**
```csharp
public static class FrameHelpers
{
    public static FrameView CreateFrame(string? title, View content, Attribute? colorScheme = null)
    {
        var frame = new FrameView(title ?? "")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        
        if (colorScheme.HasValue)
        {
            frame.ColorScheme = new ColorScheme
            {
                Normal = colorScheme.Value
            };
        }
        
        frame.Add(content);
        return frame;
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
}
```

### 5. Tables → TableHelpers

**Current:**
```csharp
public static class Tables
{
    public static Table Create(params string[] columns) { }
}
```

**Target:**
```csharp
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
            FullRowSelect = true
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
```

### 6. StatusSpinner → ProgressHelpers

**Current:**
```csharp
public static class StatusSpinner
{
    public static async Task<T> RunAsync<T>(string message, Func<Task<T>> action) { }
}
```

**Target:**
```csharp
public static class ProgressHelpers
{
    public static async Task<T> RunAsync<T>(string message, Func<Task<T>> action)
    {
        T result = default!;
        var completed = false;
        var dialog = new Dialog(message)
        {
            Width = 60,
            Height = 5
        };
        
        var progressBar = new ProgressBar()
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1
        };
        progressBar.Fraction = 0.5f; // Indeterminate
        
        dialog.Add(progressBar);
        
        _ = Task.Run(async () =>
        {
            try
            {
                result = await action();
            }
            finally
            {
                completed = true;
                Application.MainLoop.Invoke(() => Application.RequestStop());
            }
        });
        
        Application.Run(dialog);
        
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
}
```

### 7. MultilineInput → TextViewHelpers

**Current:**
```csharp
public static class MultilineInput
{
    public static string Read(string prompt) { }
}
```

**Target:**
```csharp
public static class TextViewHelpers
{
    public static string PromptMultiline(string title, string prompt, string defaultValue = "")
    {
        var dialog = new Dialog(title);
        dialog.Width = Dim.Percent(80);
        dialog.Height = Dim.Percent(80);
        
        var label = new Label(prompt) { X = 1, Y = 1 };
        
        var textView = new TextView()
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 1,
            Height = Dim.Fill() - 2,
            Text = defaultValue
        };
        
        var ok = new Button("OK") { IsDefault = true };
        var cancel = new Button("Cancel");
        
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
```

## Complete Refactored File

```csharp
using Terminal.Gui;
using Attribute = Terminal.Gui.Attribute;

namespace OpenFork.Cli.Tui;

public static class Theme
{
    public static readonly Attribute Primary = new(Color.BrightCyan, Color.Black);
    public static readonly Attribute Secondary = new(Color.BrightBlue, Color.Black);
    public static readonly Attribute Accent = new(Color.BrightYellow, Color.Black);
    public static readonly Attribute Success = new(Color.BrightGreen, Color.Black);
    public static readonly Attribute Error = new(Color.BrightRed, Color.Black);
    public static readonly Attribute Warning = new(Color.BrightYellow, Color.Black);
    public static readonly Attribute Muted = new(Color.Gray, Color.Black);
    
    public static readonly Color PrimaryColor = Color.BrightCyan;
    public static readonly Color SecondaryColor = Color.BrightBlue;
    public static readonly Color AccentColor = Color.BrightYellow;
    public static readonly Color SuccessColor = Color.BrightGreen;
    public static readonly Color ErrorColor = Color.BrightRed;
    public static readonly Color WarningColor = Color.BrightYellow;
    public static readonly Color MutedColor = Color.Gray;
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

// DialogHelpers, FrameHelpers, TableHelpers, ProgressHelpers, TextViewHelpers classes here...
```

## Migration Steps

1. Replace `using Spectre.Console` with `using Terminal.Gui`
2. Update Theme to use Terminal.Gui `Attribute` and `Color`
3. Replace Prompts class with DialogHelpers
4. Replace Panels class with FrameHelpers
5. Replace Tables class with TableHelpers
6. Replace StatusSpinner with ProgressHelpers
7. Replace MultilineInput with TextViewHelpers
8. Update all call sites throughout codebase
