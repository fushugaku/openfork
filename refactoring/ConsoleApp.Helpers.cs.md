# Refactoring: ConsoleApp.Helpers.cs

## Overview
Convert helper methods from Spectre.Console to Terminal.Gui equivalents.

## Current Methods
- `LoadLastProjectAsync()` - Load saved project state
- `StartBackgroundIndexing()` - Background indexing
- `RefreshIndexStatusAsync()` - Update index status
- `GetIndexStatusText()` - Format index status
- `SelectProviderKey()` - Provider selection prompt
- `SelectModel()` - Model selection prompt  
- `SelectDirectory()` - Directory browser
- `RenderHeader()` - FigletText header
- `RenderContext()` - Context table
- `Pause()` - Press Enter to continue
- `MenuChoice` record - Menu item data

## Target Implementation

```csharp
using OpenFork.Core.Domain;
using OpenFork.Search.Models;
using OpenFork.Search.Services;
using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private IndexStatus? _cachedIndexStatus;
    private volatile bool _isIndexing;
    private CancellationTokenSource? _indexingCts;

    // ======================
    // State Management
    // ======================

    private async Task LoadLastProjectAsync()
    {
        if (_activeProject != null) return;

        var id = await _appState.GetLastProjectIdAsync();
        if (id.HasValue)
        {
            var project = await _projects.GetAsync(id.Value);
            if (project != null)
            {
                _activeProject = project;
                await RefreshIndexStatusAsync();
                StartBackgroundIndexing(project);
            }
        }
    }

    private void StartBackgroundIndexing(Project project)
    {
        if (!_settings.Search.EnableSemanticSearch)
            return;

        if (!Directory.Exists(project.RootPath))
            return;

        _indexingCts?.Cancel();
        _indexingCts = new CancellationTokenSource();
        var token = _indexingCts.Token;

        _isIndexing = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await _indexService.IndexProjectAsync(project.Id, project.RootPath, null, token);
                if (!token.IsCancellationRequested && _activeProject?.Id == project.Id)
                {
                    _cachedIndexStatus = await _indexService.GetIndexStatusAsync(project.Id, token);
                    
                    // Update UI on main thread
                    Application.MainLoop.Invoke(UpdateContextDisplay);
                }
            }
            catch
            {
                // Silently fail
            }
            finally
            {
                _isIndexing = false;
            }
        }, token);
    }

    private async Task RefreshIndexStatusAsync()
    {
        if (_activeProject == null || !_settings.Search.EnableSemanticSearch)
        {
            _cachedIndexStatus = null;
            return;
        }

        try
        {
            _cachedIndexStatus = await _indexService.GetIndexStatusAsync(_activeProject.Id);
        }
        catch
        {
            _cachedIndexStatus = null;
        }
    }

    private string GetIndexStatusText()
    {
        if (!_settings.Search.EnableSemanticSearch)
            return "disabled";

        if (_isIndexing)
            return "indexing...";

        if (_cachedIndexStatus == null)
            return "—";

        if (!_cachedIndexStatus.IsAvailable)
            return "unavailable";

        if (!_cachedIndexStatus.Exists)
            return "not indexed";

        return $"{_cachedIndexStatus.TotalFiles} files, {_cachedIndexStatus.TotalChunks} chunks";
    }

    // ======================
    // Provider/Model Selection
    // ======================

    private string SelectProviderKey()
    {
        var keys = _settings.OpenAiCompatible.Keys.ToList();
        if (keys.Count == 0) 
            return _settings.DefaultProviderKey ?? "";

        if (keys.Count == 1)
            return keys[0];

        var selected = DialogHelpers.PromptSelection(
            "Select Provider",
            keys,
            k => k);

        return selected ?? _settings.DefaultProviderKey ?? "";
    }

    private string SelectModel(string providerKey)
    {
        if (_settings.OpenAiCompatible.TryGetValue(providerKey, out var provider) 
            && provider.AvailableModels.Count > 0)
        {
            var models = provider.AvailableModels.Select(m => m.Name).ToList();
            
            if (models.Count == 1)
                return models[0];

            var selected = DialogHelpers.PromptSelection(
                "Select Model",
                models,
                m => m);

            return selected ?? _settings.DefaultModel ?? "";
        }

        return _settings.DefaultModel ?? "";
    }

    private string? SelectDirectory(string startDirectory)
    {
        return FileDialogHelpers.SelectFolder(startDirectory, "Select Project Root");
    }

    // ======================
    // UI Update Helpers
    // ======================

    private void UpdateStatusBar(string message)
    {
        if (_statusBar != null)
        {
            // Terminal.Gui v2 StatusBar update
            Application.MainLoop.Invoke(() =>
            {
                _statusBar.Items = new StatusItem[]
                {
                    new StatusItem(Key.Null, message, null),
                    new StatusItem(Key.F1, "~F1~ Help", () => ShowHelp()),
                    new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", () => Application.RequestStop())
                };
                _statusBar.SetNeedsDisplay();
            });
        }
    }

    private void UpdateProjectContext(Project project)
    {
        _activeProject = project;
        UpdateContextDisplay();
        _ = _appState.SetLastProjectIdAsync(project.Id);
        _ = RefreshIndexStatusAsync();
        StartBackgroundIndexing(project);
    }

    private void UpdateSessionContext(Session session)
    {
        _activeSession = session;
        UpdateContextDisplay();
    }

    // ======================
    // Async Operation Helpers
    // ======================

    private async Task<T?> RunWithProgress<T>(string message, Func<Task<T>> action) where T : class
    {
        try
        {
            UpdateStatusBar(message);
            var result = await action();
            UpdateStatusBar("Ready");
            return result;
        }
        catch (Exception ex)
        {
            UpdateStatusBar("Error");
            FrameHelpers.ShowError($"Operation failed: {ex.Message}");
            return null;
        }
    }

    private async Task RunWithProgress(string message, Func<Task> action)
    {
        try
        {
            UpdateStatusBar(message);
            await action();
            UpdateStatusBar("Ready");
        }
        catch (Exception ex)
        {
            UpdateStatusBar("Error");
            FrameHelpers.ShowError($"Operation failed: {ex.Message}");
        }
    }

    // ======================
    // Formatting Helpers
    // ======================

    private string FormatAgentName(AgentProfile agent)
    {
        return $"{Icons.Agent} {agent.Name} - {agent.Model}";
    }

    private string FormatSessionName(Session session)
    {
        return $"{Icons.Session} {session.Name} - {session.UpdatedAt.ToLocalTime():g}";
    }

    private string FormatProjectName(Project project)
    {
        return $"{Icons.Project} {project.Name} - {project.RootPath}";
    }

    private string FormatPipelineName(Pipeline pipeline)
    {
        return $"{Icons.Pipeline} {pipeline.Name}";
    }
}
```

## Key Changes

### 1. Remove Spectre.Console Rendering
**Before:**
```csharp
private void RenderHeader()
{
    var title = new FigletText("OpenFork").Color(Theme.Primary);
    AnsiConsole.Write(title);
    AnsiConsole.Write(new Rule("..."));
}
```

**After:**
```csharp
// Header is now part of Window title and menu bar
// No separate render method needed
```

### 2. Remove Context Rendering
**Before:**
```csharp
private void RenderContext()
{
    var table = Tables.Create("Project", "Session", "Agent", "Pipeline", "Index");
    table.AddRow(...);
    AnsiConsole.Write(Panels.Create(table, "Context"));
}
```

**After:**
```csharp
private void UpdateContextDisplay()
{
    // Updates _contextFrame with current labels
    // Called from UpdateProjectContext, UpdateSessionContext
}
```

### 3. Remove Pause Method
**Before:**
```csharp
private void Pause()
{
    AnsiConsole.WriteLine();
    AnsiConsole.Prompt(new TextPrompt<string>("Press Enter...").AllowEmpty());
}
```

**After:**
```csharp
// Not needed in Terminal.Gui - UI is event-driven
// Use MessageBox.Query for confirmations instead
```

### 4. Simplify Selection Methods
**Before:**
```csharp
return AnsiConsole.Prompt(
    Prompts.Selection<string>("Provider")
        .AddChoices(keys));
```

**After:**
```csharp
return DialogHelpers.PromptSelection("Select Provider", keys, k => k) 
    ?? defaultValue;
```

### 5. Replace Directory Browser
**Before:**
```csharp
var browser = new Browser
{
    ActualFolder = startDirectory,
    PageSize = 16,
    DisplayIcons = true,
    CanCreateFolder = true
};
return browser.GetFolderPath(startDirectory).GetAwaiter().GetResult();
```

**After:**
```csharp
return FileDialogHelpers.SelectFolder(startDirectory, "Select Project Root");
```

### 6. Add UI Thread Safety
```csharp
// Update UI on main thread
Application.MainLoop.Invoke(() =>
{
    UpdateContextDisplay();
    _contentFrame?.SetNeedsDisplay();
});
```

## Migration Steps

1. **Remove rendering methods**
   - Delete `RenderHeader()`
   - Delete `RenderContext()`
   - Delete `Pause()`

2. **Update state management**
   - Add `UpdateProjectContext()`
   - Add `UpdateSessionContext()`
   - Add `UpdateStatusBar()`

3. **Simplify selection methods**
   - Update `SelectProviderKey()` to use DialogHelpers
   - Update `SelectModel()` to use DialogHelpers
   - Update `SelectDirectory()` to use FileDialogHelpers

4. **Add async helpers**
   - Add `RunWithProgress<T>()`
   - Add `RunWithProgress()`

5. **Add formatting helpers**
   - Add `FormatAgentName()`
   - Add `FormatSessionName()`
   - Add `FormatProjectName()`
   - Add `FormatPipelineName()`

6. **Update background tasks**
   - Use `Application.MainLoop.Invoke()` for UI updates
   - Ensure thread safety

7. **Remove MenuChoice record**
   - No longer needed with Terminal.Gui menus

## Testing

1. Verify state loads correctly on startup
2. Test background indexing with UI updates
3. Verify provider/model selection dialogs
4. Test directory selection
5. Verify status bar updates
6. Test thread-safe UI updates from background tasks
