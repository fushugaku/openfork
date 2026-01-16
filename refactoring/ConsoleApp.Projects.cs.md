# Refactoring: ConsoleApp.Projects.cs

## Overview
Convert Projects screen from Spectre.Console tables/prompts to Terminal.Gui ListView and forms.

## Current Flow
1. Clear console
2. Render header and context
3. Load projects and display table
4. Show selection prompt with menu choices
5. Handle: Create, Select, Detail view
6. Clear and repeat

## Target Flow
1. Create ListView with projects
2. Add buttons for actions (New, Select, Detail, Reindex)
3. Handle selections in event handlers
4. Update content frame without clearing

## Refactored Implementation

```csharp
using OpenFork.Core.Domain;
using OpenFork.Search.Services;
using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private View CreateProjectsView()
    {
        var container = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Load projects
        var projects = _projects.ListAsync().GetAwaiter().GetResult();

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

        // Format items with active indicator
        var items = projects.Select(p =>
        {
            var isActive = _activeProject?.Id == p.Id;
            var marker = isActive ? $"{Icons.Check} " : "  ";
            return $"{marker}{Icons.Project} {p.Name} - {p.RootPath}";
        }).ToList();

        listView.SetSource(items);

        // Button panel
        var buttonY = Pos.Bottom(listView);
        
        var newButton = new Button("New Project")
        {
            X = 1,
            Y = buttonY
        };
        newButton.Clicked += async () => await CreateProjectAsync();

        var selectButton = new Button("Select")
        {
            X = Pos.Right(newButton) + 2,
            Y = buttonY
        };
        selectButton.Clicked += () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
            {
                var project = projects[listView.SelectedItem];
                SelectProject(project);
            }
        };

        var detailButton = new Button("Details")
        {
            X = Pos.Right(selectButton) + 2,
            Y = buttonY
        };
        detailButton.Clicked += () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
            {
                var project = projects[listView.SelectedItem];
                ShowProjectDetail(project);
            }
        };

        var backButton = new Button("Back")
        {
            X = Pos.Right(detailButton) + 2,
            Y = buttonY
        };
        backButton.Clicked += () => ShowWelcomeScreen();

        // Double-click to select
        listView.OpenSelectedItem += (e) =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
            {
                var project = projects[listView.SelectedItem];
                SelectProject(project);
            }
        };

        container.Add(listView, newButton, selectButton, detailButton, backButton);

        return container;
    }

    private void SelectProject(Project project)
    {
        if (_activeProject?.Id == project.Id)
        {
            ShowProjectDetail(project);
        }
        else
        {
            UpdateProjectContext(project);
            _activeSession = null;
            ShowWelcomeScreen();
            FrameHelpers.ShowInfo($"Project '{project.Name}' selected");
        }
    }

    private async Task CreateProjectAsync()
    {
        // Get project name
        var name = DialogHelpers.PromptText(
            "New Project",
            "Project name:",
            "",
            required: true);

        if (string.IsNullOrWhiteSpace(name))
            return;

        // Select directory
        var path = FileDialogHelpers.SelectFolder(
            Environment.CurrentDirectory,
            "Select Project Root");

        if (string.IsNullOrWhiteSpace(path))
            return;

        // Confirm
        if (!DialogHelpers.Confirm("Create Project", 
            $"Create project '{name}' at:\n{path}?"))
            return;

        // Create project
        await RunWithProgress("Creating project...", async () =>
        {
            var project = new Project { Name = name, RootPath = path };
            _activeProject = await _projects.UpsertAsync(project);
            await _appState.SetLastProjectIdAsync(_activeProject.Id);
            _activeSession = null;
            StartBackgroundIndexing(_activeProject);
        });

        UpdateContextDisplay();
        ShowProjectsScreen();
        FrameHelpers.ShowSuccess($"Project '{name}' created. Indexing in background...");
    }

    private void ShowProjectDetail(Project project)
    {
        var dialog = new Dialog($"Project: {project.Name}")
        {
            Width = Dim.Percent(80),
            Height = 15
        };

        // Project info
        var infoFrame = new FrameView("Information")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1,
            Height = 8
        };

        var nameLabel = new Label($"Name: {project.Name}")
        {
            X = 1,
            Y = 0
        };

        var pathLabel = new Label($"Path: {project.RootPath}")
        {
            X = 1,
            Y = 1
        };

        var indexStatusLabel = new Label($"Index Status: {GetIndexStatusText()}")
        {
            X = 1,
            Y = 2
        };

        infoFrame.Add(nameLabel, pathLabel, indexStatusLabel);

        // Buttons
        var reindexButton = new Button("Reindex")
        {
            X = 1,
            Y = Pos.Bottom(infoFrame) + 1
        };
        reindexButton.Clicked += async () =>
        {
            dialog.RequestStop();
            await ReindexProjectAsync(project);
        };

        var closeButton = new Button("Close")
        {
            X = Pos.Right(reindexButton) + 2,
            Y = Pos.Bottom(infoFrame) + 1,
            IsDefault = true
        };
        closeButton.Clicked += () => dialog.RequestStop();

        dialog.Add(infoFrame, reindexButton, closeButton);
        Application.Run(dialog);
    }

    private async Task ReindexProjectAsync(Project project)
    {
        if (!_settings.Search.EnableSemanticSearch)
        {
            FrameHelpers.ShowError("Semantic search is disabled");
            return;
        }

        if (!Directory.Exists(project.RootPath))
        {
            FrameHelpers.ShowError($"Project path not found: {project.RootPath}");
            return;
        }

        var progressDialog = new Dialog("Reindexing Project")
        {
            Width = 60,
            Height = 8
        };

        var statusLabel = new Label("Starting...")
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill() - 1
        };

        var progressBar = new ProgressBar()
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill() - 1
        };

        progressDialog.Add(statusLabel, progressBar);

        var indexTask = Task.Run(async () =>
        {
            var progress = new Progress<IndexProgress>(p =>
            {
                Application.MainLoop.Invoke(() =>
                {
                    statusLabel.Text = $"Indexing: {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})";
                    progressBar.Fraction = (float)p.ProcessedFiles / p.TotalFiles;
                    statusLabel.SetNeedsDisplay();
                    progressBar.SetNeedsDisplay();
                });
            });

            await _indexService.ReindexProjectAsync(project.Id, project.RootPath, progress);
            
            Application.MainLoop.Invoke(() =>
            {
                progressDialog.RequestStop();
            });
        });

        Application.Run(progressDialog);

        await indexTask;
        await RefreshIndexStatusAsync();
        
        ShowProjectsScreen();
        FrameHelpers.ShowSuccess("Project reindexed successfully");
    }
}
```

## Key Changes

### 1. Table → ListView
**Before:**
```csharp
var table = Tables.Create("Id", "Name", "Path");
foreach (var project in projects)
{
    table.AddRow($"{project.Id}", project.Name, project.RootPath);
}
AnsiConsole.Write(Panels.Create(table, "Projects"));
```

**After:**
```csharp
var listView = new ListView()
{
    Width = Dim.Fill(),
    Height = Dim.Fill() - 4
};
var items = projects.Select(p => $"{Icons.Project} {p.Name} - {p.RootPath}").ToList();
listView.SetSource(items);
```

### 2. Selection Prompt → Buttons
**Before:**
```csharp
var choice = AnsiConsole.Prompt(
    Prompts.Selection<MenuChoice>("Select Project")
        .AddChoices(choices));
```

**After:**
```csharp
var selectButton = new Button("Select");
selectButton.Clicked += () => SelectProject(selectedProject);
```

### 3. Clear/Render → Update Frame
**Before:**
```csharp
AnsiConsole.Clear();
RenderHeader();
RenderContext();
RenderProjectsTable(projects);
```

**After:**
```csharp
_contentFrame.RemoveAll();
_contentFrame.Add(CreateProjectsView());
UpdateContextDisplay();
```

### 4. Async Prompts → Dialogs
**Before:**
```csharp
var name = AnsiConsole.Prompt(Prompts.RequiredText("Project name"));
var path = SelectDirectory(Environment.CurrentDirectory);
```

**After:**
```csharp
var name = DialogHelpers.PromptText("New Project", "Project name:", "", true);
var path = FileDialogHelpers.SelectFolder(Environment.CurrentDirectory);
```

### 5. Status Spinner → Progress Dialog
**Before:**
```csharp
await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("Reindexing...", async ctx =>
    {
        await _indexService.ReindexProjectAsync(...);
    });
```

**After:**
```csharp
var progressDialog = new Dialog("Reindexing");
var progressBar = new ProgressBar();
// Update progress via Application.MainLoop.Invoke()
```

## Migration Steps

1. **Delete entire old implementation**
   - Remove `ProjectsScreenAsync()`
   - Remove `ProjectDetailScreenAsync()`
   - Remove `CreateProjectAsync()`
   - Remove `ReindexProjectAsync()`
   - Remove `RenderProjectsTable()`
   - Remove `RenderProjectDetail()`
   - Remove `BuildProjectChoices()`

2. **Create new CreateProjectsView()**
   - ListView for projects
   - Buttons for actions
   - Event handlers

3. **Create helper methods**
   - `SelectProject()`
   - `ShowProjectDetail()`

4. **Recreate async operations**
   - `CreateProjectAsync()` with dialogs
   - `ReindexProjectAsync()` with progress

5. **Test all functionality**
   - List projects
   - Create new project
   - Select project
   - View details
   - Reindex

## Testing

1. View empty projects list
2. Create new project
3. Select existing project
4. View project details
5. Reindex project with progress
6. Navigate back to welcome
7. Verify context updates
