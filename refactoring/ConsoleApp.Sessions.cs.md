# Refactoring: ConsoleApp.Sessions.cs

## Overview
Convert Sessions screen from Spectre.Console to Terminal.Gui ListView with similar pattern to Projects.

## Current Implementation
- Display sessions table for active project
- Selection prompt with create/select/back options
- Sync project from session if needed

## Target Implementation

```csharp
using OpenFork.Core.Domain;
using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private View CreateSessionsView()
    {
        if (_activeProject == null)
        {
            FrameHelpers.ShowError("Please select a project first");
            return new View();
        }

        var container = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Load sessions for active project
        var sessions = _sessions.ListByProjectAsync(_activeProject.Id).GetAwaiter().GetResult();

        // Create ListView
        var listView = new ListView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 4,
            AllowsMarking = false,
            CanFocus = true
        };

        // Format items
        var items = sessions.Select(s =>
        {
            var isActive = _activeSession?.Id == s.Id;
            var marker = isActive ? $"{Icons.Check} " : "  ";
            return $"{marker}{Icons.Session} {s.Name} - {s.UpdatedAt.ToLocalTime():g}";
        }).ToList();

        listView.SetSource(items);

        // Buttons
        var buttonY = Pos.Bottom(listView);

        var newButton = new Button("New Session")
        {
            X = 1,
            Y = buttonY
        };
        newButton.Clicked += () => CreateNewSession();

        var selectButton = new Button("Select")
        {
            X = Pos.Right(newButton) + 2,
            Y = buttonY
        };
        selectButton.Clicked += async () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
            {
                var session = sessions[listView.SelectedItem];
                await SelectSessionAsync(session);
            }
        };

        var deleteButton = new Button("Delete")
        {
            X = Pos.Right(selectButton) + 2,
            Y = buttonY
        };
        deleteButton.Clicked += async () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
            {
                var session = sessions[listView.SelectedItem];
                await DeleteSessionAsync(session);
            }
        };

        var backButton = new Button("Back")
        {
            X = Pos.Right(deleteButton) + 2,
            Y = buttonY
        };
        backButton.Clicked += () => ShowWelcomeScreen();

        // Double-click to select
        listView.OpenSelectedItem += async (e) =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
            {
                var session = sessions[listView.SelectedItem];
                await SelectSessionAsync(session);
            }
        };

        container.Add(listView, newButton, selectButton, deleteButton, backButton);

        return container;
    }

    private void CreateNewSession()
    {
        if (_activeProject == null)
        {
            FrameHelpers.ShowError("No active project");
            return;
        }

        var name = DialogHelpers.PromptText(
            "New Session",
            "Session name:",
            "",
            required: true);

        if (string.IsNullOrWhiteSpace(name))
            return;

        _ = Task.Run(async () =>
        {
            var session = new Session 
            { 
                ProjectId = _activeProject.Id, 
                Name = name 
            };
            
            _activeSession = await _sessions.UpsertAsync(session);
            
            Application.MainLoop.Invoke(() =>
            {
                UpdateSessionContext(_activeSession);
                ShowSessionsScreen();
                FrameHelpers.ShowSuccess($"Session '{name}' created");
            });
        });
    }

    private async Task SelectSessionAsync(Session session)
    {
        _activeSession = session;
        await SyncProjectFromSessionAsync();
        UpdateSessionContext(session);
        ShowWelcomeScreen();
        FrameHelpers.ShowInfo($"Session '{session.Name}' selected");
    }

    private async Task DeleteSessionAsync(Session session)
    {
        if (!DialogHelpers.Confirm("Delete Session", 
            $"Are you sure you want to delete session '{session.Name}'?"))
            return;

        await RunWithProgress("Deleting session...", async () =>
        {
            await _sessions.DeleteAsync(session.Id);
            
            if (_activeSession?.Id == session.Id)
            {
                _activeSession = null;
                UpdateContextDisplay();
            }
        });

        ShowSessionsScreen();
        FrameHelpers.ShowSuccess("Session deleted");
    }

    private async Task SyncProjectFromSessionAsync()
    {
        if (_activeSession == null || _activeProject?.Id == _activeSession.ProjectId)
            return;

        await RunWithProgress("Loading project...", async () =>
        {
            _activeProject = await _projects.GetAsync(_activeSession.ProjectId);
            if (_activeProject != null)
            {
                await _appState.SetLastProjectIdAsync(_activeProject.Id);
                await RefreshIndexStatusAsync();
                StartBackgroundIndexing(_activeProject);
                UpdateContextDisplay();
            }
        });
    }
}
```

## Key Changes

### 1. Check for Active Project First
```csharp
if (_activeProject == null)
{
    FrameHelpers.ShowError("Please select a project first");
    return new View();
}
```

### 2. ListView with Session Formatting
```csharp
var items = sessions.Select(s =>
{
    var isActive = _activeSession?.Id == s.Id;
    var marker = isActive ? $"{Icons.Check} " : "  ";
    return $"{marker}{Icons.Session} {s.Name} - {s.UpdatedAt.ToLocalTime():g}";
}).ToList();
```

### 3. Async Session Selection
```csharp
private async Task SelectSessionAsync(Session session)
{
    _activeSession = session;
    await SyncProjectFromSessionAsync(); // Important!
    UpdateSessionContext(session);
    ShowWelcomeScreen();
}
```

### 4. Session Creation Dialog
```csharp
var name = DialogHelpers.PromptText("New Session", "Session name:", "", true);
if (!string.IsNullOrWhiteSpace(name))
{
    var session = new Session { ProjectId = _activeProject.Id, Name = name };
    _activeSession = await _sessions.UpsertAsync(session);
}
```

### 5. Delete Confirmation
```csharp
if (DialogHelpers.Confirm("Delete Session", $"Delete '{session.Name}'?"))
{
    await _sessions.DeleteAsync(session.Id);
    if (_activeSession?.Id == session.Id)
        _activeSession = null;
}
```

## Removed Methods

- `SessionsScreenAsync()` - replaced by `CreateSessionsView()`
- `EnsureSessionSelectedAsync()` - logic moved to chat screen
- `RenderSessionsTable()` - replaced by ListView
- `BuildSessionChoices()` - no longer needed

## New Methods

- `CreateSessionsView()` - main view creation
- `CreateNewSession()` - session creation with dialog
- `SelectSessionAsync()` - session selection logic
- `DeleteSessionAsync()` - session deletion with confirmation
- `SyncProjectFromSessionAsync()` - unchanged, kept as-is

## Migration Steps

1. **Delete old implementation**
   - Remove `SessionsScreenAsync()`
   - Remove `EnsureSessionSelectedAsync()` (keep for chat screen)
   - Remove `RenderSessionsTable()`
   - Remove `BuildSessionChoices()`

2. **Create CreateSessionsView()**
   - Check for active project
   - Create ListView
   - Add buttons (New, Select, Delete, Back)
   - Wire up event handlers

3. **Add async helpers**
   - `CreateNewSession()`
   - `SelectSessionAsync()`
   - `DeleteSessionAsync()`

4. **Keep SyncProjectFromSessionAsync()**
   - Already properly implemented
   - Just ensure it's called from SelectSessionAsync

5. **Update Chat screen**
   - Implement inline session selection if needed
   - Or show error and redirect to sessions screen

## Testing

1. Verify "no active project" error
2. Create new session
3. List sessions for project
4. Select existing session
5. Delete session
6. Verify active marker
7. Test session sync when switching projects
8. Verify context updates
