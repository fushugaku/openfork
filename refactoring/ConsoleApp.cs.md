# Refactoring: ConsoleApp.cs

## Overview
Convert main application loop from Spectre.Console prompts to Terminal.Gui window-based UI.

## Current Implementation
```csharp
public async Task RunAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        if (!_stateLoaded)
        {
            await StatusSpinner.RunAsync("Loading state...", LoadLastProjectAsync);
            _stateLoaded = true;
        }

        AnsiConsole.Clear();
        RenderHeader();
        RenderContext();

        var choice = AnsiConsole.Prompt(
            Prompts.Selection<string>("Main Menu")
                .AddChoices(
                    $"{Icons.Project} Projects",
                    $"{Icons.Session} Sessions",
                    // ...
                ));

        switch (choice)
        {
            case var c when c.Contains("Projects"):
                await ProjectsScreenAsync(cancellationToken);
                break;
            // ...
        }
    }
}
```

## Target Implementation

### Architecture Change
- From: Loop with prompts (blocking)
- To: Window with event-driven UI

### Main Window Structure
```
┌─────────────────────────────────────┐
│ OpenFork - AI Agent Manager         │
├─────────────────────────────────────┤
│ Context Panel (top)                 │
│  Project: X | Session: Y | Agent: Z │
├─────────────────────────────────────┤
│                                     │
│                                     │
│         Main Content Area           │
│         (Dynamic Views)             │
│                                     │
│                                     │
├─────────────────────────────────────┤
│ Status: Ready          Index: OK    │
└─────────────────────────────────────┘
```

## Refactored Code

```csharp
using OpenFork.Core.Domain;
using OpenFork.Core.Config;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using OpenFork.Search.Services;
using OpenFork.Search.Tools;
using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private readonly ProjectService _projects;
    private readonly SessionService _sessions;
    private readonly AgentService _agents;
    private readonly PipelineService _pipelines;
    private readonly ChatService _chat;
    private readonly AppSettings _settings;
    private readonly AppStateService _appState;
    private readonly ProjectIndexService _indexService;
    private readonly ToolRegistry _toolRegistry;

    private Project? _activeProject;
    private Session? _activeSession;
    private bool _stateLoaded;

    // Terminal.Gui components
    private Window? _mainWindow;
    private FrameView? _contextFrame;
    private FrameView? _contentFrame;
    private StatusBar? _statusBar;
    private Label? _statusLabel;

    public ConsoleApp(
        ProjectService projects,
        SessionService sessions,
        AgentService agents,
        PipelineService pipelines,
        ChatService chat,
        AppSettings settings,
        AppStateService appState,
        ProjectIndexService indexService,
        ToolRegistry toolRegistry)
    {
        _projects = projects;
        _sessions = sessions;
        _agents = agents;
        _pipelines = pipelines;
        _chat = chat;
        _settings = settings;
        _appState = appState;
        _indexService = indexService;
        _toolRegistry = toolRegistry;

        _toolRegistry.Register(new SearchProjectTool(
            indexService,
            () => _activeProject?.Id,
            () => _activeProject?.RootPath));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Load initial state
        if (!_stateLoaded)
        {
            await ProgressHelpers.RunAsync("Loading state...", LoadLastProjectAsync);
            _stateLoaded = true;
        }

        // Create main window
        _mainWindow = new Window("OpenFork - AI Agent Manager")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Create menu bar
        var menu = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_File", new MenuItem[]
            {
                new MenuItem("_Exit", "", () => 
                {
                    if (FrameHelpers.Confirm("Exit", "Are you sure you want to exit?"))
                    {
                        Application.RequestStop();
                    }
                })
            }),
            new MenuBarItem("_Project", new MenuItem[]
            {
                new MenuItem("_Projects", "", () => ShowProjectsScreen()),
                new MenuItem("_Sessions", "", () => ShowSessionsScreen())
            }),
            new MenuBarItem("_Agents", new MenuItem[]
            {
                new MenuItem("_Agents", "", () => ShowAgentsScreen()),
                new MenuItem("_Pipelines", "", () => ShowPipelinesScreen())
            }),
            new MenuBarItem("_Chat", new MenuItem[]
            {
                new MenuItem("_Start Chat", "", () => ShowChatScreen())
            })
        });

        // Create context panel (top)
        _contextFrame = new FrameView("Context")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 5
        };
        UpdateContextDisplay();

        // Create content frame (main area)
        _contentFrame = new FrameView("Welcome")
        {
            X = 0,
            Y = Pos.Bottom(_contextFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1
        };

        // Show welcome screen
        ShowWelcomeScreen();

        // Create status bar
        _statusBar = new StatusBar(new StatusItem[]
        {
            new StatusItem(Key.F1, "~F1~ Help", () => ShowHelp()),
            new StatusItem(Key.F10, "~F10~ Menu", () => { }),
            new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", () => Application.RequestStop())
        });

        // Add components to main window
        Application.Top.Add(menu);
        _mainWindow.Add(_contextFrame, _contentFrame);
        Application.Top.Add(_mainWindow);
        Application.Top.Add(_statusBar);

        // Run the application
        Application.Run();
    }

    private void ShowWelcomeScreen()
    {
        _contentFrame!.RemoveAll();
        _contentFrame.Title = "Welcome";

        var welcomeText = new Label()
        {
            X = Pos.Center(),
            Y = Pos.Center(),
            Text = $@"
{Icons.Project} OpenFork - AI Agent Manager

Press F10 or Alt to access the menu
Select Project → Sessions → Agents/Pipelines → Chat

Quick Keys:
  F1  - Help
  ^Q  - Quit
            ".Trim(),
            TextAlignment = TextAlignment.Centered
        };

        _contentFrame.Add(welcomeText);
    }

    private void ShowHelp()
    {
        MessageBox.Query("Help", 
            @"OpenFork - AI Agent Manager

Navigation:
  F10 / Alt     - Open menu
  Tab / Shift+Tab - Navigate fields
  Enter         - Select/Confirm
  Esc           - Cancel/Back
  ^Q            - Quit

Workflow:
  1. Select/Create a Project
  2. Select/Create a Session
  3. Configure Agent or Pipeline
  4. Start Chat

For more information, see documentation.", 
            "OK");
    }

    private void UpdateContextDisplay()
    {
        if (_contextFrame == null) return;

        _contextFrame.RemoveAll();

        var projectLabel = new Label($"Project: {_activeProject?.Name ?? "None"}")
        {
            X = 1,
            Y = 1,
            ColorScheme = new ColorScheme { Normal = Theme.Primary }
        };

        var sessionLabel = new Label($"Session: {_activeSession?.Name ?? "None"}")
        {
            X = 30,
            Y = 1,
            ColorScheme = new ColorScheme { Normal = Theme.Secondary }
        };

        var agentLabel = new Label($"Agent: {(_activeSession?.ActiveAgentId.HasValue == true ? $"#{_activeSession.ActiveAgentId}" : "None")}")
        {
            X = 60,
            Y = 1,
            ColorScheme = new ColorScheme { Normal = Theme.Success }
        };

        var indexLabel = new Label($"Index: {GetIndexStatusText()}")
        {
            X = 1,
            Y = 2
        };

        _contextFrame.Add(projectLabel, sessionLabel, agentLabel, indexLabel);
    }

    private void ShowProjectsScreen()
    {
        _contentFrame!.RemoveAll();
        _contentFrame.Title = "Projects";
        
        // Will be implemented in ConsoleApp.Projects.cs
        var projectsView = CreateProjectsView();
        _contentFrame.Add(projectsView);
    }

    private void ShowSessionsScreen()
    {
        _contentFrame!.RemoveAll();
        _contentFrame.Title = "Sessions";
        
        // Will be implemented in ConsoleApp.Sessions.cs
        var sessionsView = CreateSessionsView();
        _contentFrame.Add(sessionsView);
    }

    private void ShowAgentsScreen()
    {
        _contentFrame!.RemoveAll();
        _contentFrame.Title = "Agents";
        
        // Will be implemented in ConsoleApp.Agents.cs
        var agentsView = CreateAgentsView();
        _contentFrame.Add(agentsView);
    }

    private void ShowPipelinesScreen()
    {
        _contentFrame!.RemoveAll();
        _contentFrame.Title = "Pipelines";
        
        // Will be implemented in ConsoleApp.Pipelines.cs
        var pipelinesView = CreatePipelinesView();
        _contentFrame.Add(pipelines View);
    }

    private void ShowChatScreen()
    {
        if (_activeProject == null)
        {
            FrameHelpers.ShowError("Please select a project first");
            return;
        }

        if (_activeSession == null)
        {
            FrameHelpers.ShowError("Please select a session first");
            return;
        }

        _contentFrame!.RemoveAll();
        _contentFrame.Title = "Chat";
        
        // Will be implemented in ConsoleApp.Chat.cs
        var chatView = CreateChatView();
        _contentFrame.Add(chatView);
    }

    // Placeholder methods - implemented in partial class files
    private View CreateProjectsView() => new View();
    private View CreateSessionsView() => new View();
    private View CreateAgentsView() => new View();
    private View CreatePipelinesView() => new View();
    private View CreateChatView() => new View();
}
```

## Migration Steps

1. **Remove Spectre.Console dependencies**
   - Remove `using Spectre.Console`
   - Add `using Terminal.Gui`

2. **Convert RunAsync method**
   - Remove while loop
   - Create Window and UI components
   - Add MenuBar for navigation
   - Create content frame for dynamic views

3. **Add context panel**
   - Display active project, session, agent
   - Update on state changes

4. **Convert menu choices to MenuBar**
   - File → Exit
   - Project → Projects, Sessions
   - Agents → Agents, Pipelines
   - Chat → Start Chat

5. **Create view navigation methods**
   - `ShowProjectsScreen()`
   - `ShowSessionsScreen()`
   - `ShowAgentsScreen()`
   - `ShowPipelinesScreen()`
   - `ShowChatScreen()`

6. **Update state management**
   - Call `UpdateContextDisplay()` when state changes
   - No need to clear console - just swap views

## Testing

1. Verify application starts and shows welcome screen
2. Test menu navigation (F10, Alt)
3. Test keyboard shortcuts
4. Verify context panel updates
5. Test each screen navigation
6. Verify graceful exit
