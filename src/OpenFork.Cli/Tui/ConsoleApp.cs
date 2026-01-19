using OpenFork.Core.Domain;
using OpenFork.Core.Config;
using OpenFork.Core.Events;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using OpenFork.Search.Services;
using OpenFork.Search.Tools;
using Terminal.Gui;
using Microsoft.Extensions.Logging;

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
  private readonly IEventBus _eventBus;
  private readonly ISubagentService _subagentService;
  private readonly ILogger<ConsoleApp> _logger;

  private Project? _activeProject;
  private Session? _activeSession;
  private bool _stateLoaded;

  // Terminal.Gui components
  private MenuBar? _menuBar;
  private FrameView? _contextFrame;
  private FrameView? _contentFrame;
  private StatusBar? _statusBar;

  // Indexing progress
  private OpenFork.Search.Services.IndexProgress? _indexProgress;

  public ConsoleApp(
      ProjectService projects,
      SessionService sessions,
      AgentService agents,
      PipelineService pipelines,
      ChatService chat,
      AppSettings settings,
      AppStateService appState,
      ProjectIndexService indexService,
      ToolRegistry toolRegistry,
      IEventBus eventBus,
      ISubagentService subagentService,
      ILogger<ConsoleApp> logger)
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
    _eventBus = eventBus;
    _subagentService = subagentService;
    _logger = logger;

    _logger.LogInformation("ConsoleApp constructor called");

    _toolRegistry.Register(new SearchProjectTool(
        indexService,
        () => _activeProject?.Id,
        () => _activeProject?.RootPath));

    // Subscribe to subagent events for real-time visibility
    SubscribeToSubagentEvents();
  }

  /// <summary>
  /// Subscribes to subagent lifecycle events for real-time UI updates.
  /// </summary>
  private void SubscribeToSubagentEvents()
  {
    _eventBus.Subscribe<SubSessionCreatedEvent>(OnSubSessionCreated);
    _eventBus.Subscribe<SubSessionStatusChangedEvent>(OnSubSessionStatusChanged);
    _eventBus.Subscribe<SubSessionProgressEvent>(OnSubSessionProgress);
    _eventBus.Subscribe<SubSessionCompletedEvent>(OnSubSessionCompleted);
    _eventBus.Subscribe<SubSessionFailedEvent>(OnSubSessionFailed);
    _eventBus.Subscribe<SubSessionCancelledEvent>(OnSubSessionCancelled);
  }

  private Task OnSubSessionCreated(SubSessionCreatedEvent evt)
  {
    _logger.LogInformation("🚀 Subagent spawned: {AgentSlug} - {Description}", evt.AgentSlug, evt.Description);
    Application.MainLoop.Invoke(() =>
    {
      _subagentTracker.Add(evt.SubSessionId, evt.AgentSlug, evt.Description ?? "Task");
    });
    return Task.CompletedTask;
  }

  private Task OnSubSessionStatusChanged(SubSessionStatusChangedEvent evt)
  {
    _logger.LogInformation("📍 Subagent {Id} status: {Old} → {New}", evt.SubSessionId, evt.OldStatus, evt.NewStatus);
    Application.MainLoop.Invoke(() =>
    {
      _subagentTracker.UpdateStatus(evt.SubSessionId, evt.NewStatus);
    });
    return Task.CompletedTask;
  }

  private Task OnSubSessionProgress(SubSessionProgressEvent evt)
  {
    // Progress events are frequent - only log at debug level
    _logger.LogDebug("⏳ Subagent {Id} progress: {PartType}", evt.SubSessionId, evt.PartType);
    return Task.CompletedTask;
  }

  private Task OnSubSessionCompleted(SubSessionCompletedEvent evt)
  {
    _logger.LogInformation("✅ Subagent {Id} completed in {Duration} ({Iterations} iterations)",
        evt.SubSessionId, evt.Duration, evt.IterationsUsed);
    Application.MainLoop.Invoke(() =>
    {
      _subagentTracker.Complete(evt.SubSessionId, true, evt.Duration);
    });
    return Task.CompletedTask;
  }

  private Task OnSubSessionFailed(SubSessionFailedEvent evt)
  {
    _logger.LogWarning("❌ Subagent {Id} failed: {Error}", evt.SubSessionId, evt.Error);
    Application.MainLoop.Invoke(() =>
    {
      _subagentTracker.Complete(evt.SubSessionId, false, TimeSpan.Zero, evt.Error);
    });
    return Task.CompletedTask;
  }

  private Task OnSubSessionCancelled(SubSessionCancelledEvent evt)
  {
    _logger.LogInformation("🚫 Subagent {Id} cancelled: {Reason}", evt.SubSessionId, evt.Reason);
    Application.MainLoop.Invoke(() =>
    {
      _subagentTracker.Remove(evt.SubSessionId);
    });
    return Task.CompletedTask;
  }

  public async Task RunAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("RunAsync started");

    // Enable mouse support explicitly
    Application.IsMouseDisabled = false;
    _logger.LogInformation("Mouse enabled, Driver={Driver}", Application.Driver?.GetType().Name ?? "null");

    // Set up global mouse wheel handler for chat scrolling
    Application.RootMouseEvent += HandleGlobalMouseEvent;

    _logger.LogInformation("Creating menu bar...");
    // Create menu bar with theme
    _menuBar = new MenuBar(new MenuBarItem[]
    {
      new MenuBarItem("_File", new MenuItem[]
      {
        new MenuItem("_Exit", "", () =>
        {
          _logger.LogInformation("Exit menu clicked");
          if (DialogHelpers.Confirm("Exit", "Are you sure you want to exit?"))
          {
            Application.RequestStop();
          }
        })
      }),
      new MenuBarItem("_Project", new MenuItem[]
      {
        new MenuItem("_Projects", "F2", () => ShowProjectsScreen()),
        new MenuItem("_Sessions", "F3", () => ShowSessionsScreen())
      }),
      new MenuBarItem("_Pipelines", new MenuItem[]
      {
        new MenuItem("_Pipelines", "F4", () => ShowPipelinesScreen())
      }),
      new MenuBarItem("_Chat", new MenuItem[]
      {
        new MenuItem("_Start Chat", "F5", () => ShowChatScreen())
      })
    });
    _menuBar.ColorScheme = Theme.Schemes.Menu;
    _logger.LogInformation("Menu bar created");

    _logger.LogInformation("Creating context panel...");
    // Create context panel (below menu) - compact height
    _contextFrame = new FrameView("Context")
    {
      X = 0,
      Y = 1, // Below menu bar
      Width = Dim.Fill(),
      Height = Layout.ContextPanelHeight,
      ColorScheme = Theme.Schemes.Panel
    };
    UpdateContextDisplay();
    _logger.LogInformation("Context panel created");

    _logger.LogInformation("Creating content frame...");
    // Create content frame (main area) with theme
    _contentFrame = new FrameView("Welcome")
    {
      X = 0,
      Y = Pos.Bottom(_contextFrame),
      Width = Dim.Fill(),
      Height = Dim.Fill() - Layout.StatusBarHeight,
      ColorScheme = Theme.Schemes.Panel
    };
    _logger.LogInformation("Content frame created");

    _logger.LogInformation("Showing welcome screen...");
    // Show welcome screen
    ShowWelcomeScreen();
    _logger.LogInformation("Welcome screen shown");

    _logger.LogInformation("Creating status bar...");
    // Create status bar with comprehensive shortcuts
    _statusBar = new StatusBar(new StatusItem[]
    {
      new StatusItem(Key.F1, "~F1~ Help", () => ShowHelp()),
      new StatusItem(Key.F2, "~F2~ Projects", () => ShowProjectsScreen()),
      new StatusItem(Key.F3, "~F3~ Sessions", () => ShowSessionsScreen()),
      new StatusItem(Key.F4, "~F4~ Pipelines", () => ShowPipelinesScreen()),
      new StatusItem(Key.F5, "~F5~ Chat", () => ShowChatScreen()),
      new StatusItem(Key.Esc, "~Esc~ Back", () => ShowMainMenu()),
      new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", () =>
      {
        if (DialogHelpers.Confirm("Exit", "Are you sure you want to exit?"))
          Application.RequestStop();
      })
    });
    _statusBar.ColorScheme = Theme.Schemes.StatusBar;
    _logger.LogInformation("Status bar created");

    _logger.LogInformation("Adding components to Application.Top...");
    // Add components in correct Z-order (menu and status bar on top)
    Application.Top.Add(_contextFrame);
    _logger.LogInformation("Context frame added to Application.Top");
    Application.Top.Add(_contentFrame);
    _logger.LogInformation("Content frame added to Application.Top");
    Application.Top.Add(_menuBar);  // Menu on top
    _logger.LogInformation("Menu added to Application.Top");
    Application.Top.Add(_statusBar);  // Status bar on top
    _logger.LogInformation("Status bar added to Application.Top");

    _logger.LogInformation("All components added. Calling Application.Run()...");

    // Load initial state AFTER UI is running
    if (!_stateLoaded)
    {
      _logger.LogInformation("Scheduling initial state load...");
      Application.MainLoop.Invoke(async () =>
      {
        _logger.LogInformation("Loading initial state (async)...");
        await LoadLastProjectAsync();
        _stateLoaded = true;
        _logger.LogInformation("Initial state loaded");
      });
    }

    // Auto-open the main menu on startup
    Application.MainLoop.Invoke(() =>
    {
      _logger.LogInformation("Auto-opening main menu...");
      ShowMainMenu();
    });

    // Run the application
    Application.Run();
    _logger.LogInformation("Application.Run() finished");
  }

  private void ShowMainMenu()
  {
    // Clear chat view reference when leaving chat
    _activeChatView = null;

    // Restore menu bar and context frame if hidden
    _menuBar!.Visible = true;
    _contextFrame!.Visible = true;

    // Restore content frame position and border
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "Main Menu";
    _contentFrame.Border.BorderStyle = BorderStyle.Single;
    _contentFrame.X = 0;
    _contentFrame.Y = Pos.Bottom(_contextFrame);
    _contentFrame.Width = Dim.Fill();
    _contentFrame.Height = Dim.Fill() - Layout.StatusBarHeight;

    var mainMenuView = new View()
    {
      X = 0,
      Y = 0,
      Width = Dim.Fill(),
      Height = Dim.Fill(),
      ColorScheme = Theme.Schemes.Base
    };

    // Title with accent color
    var title = ThemeHelper.CreateAccentLabel("OpenFork - AI Agent Manager");
    title.X = Pos.Center();
    title.Y = 1;
    title.TextAlignment = TextAlignment.Centered;
    mainMenuView.Add(title);

    // Show indexing status if active
    int menuStartY = 3;
    if (_isIndexing && _indexProgress != null)
    {
      var indexStatus = new FrameView("Indexing")
      {
        X = Pos.Center(),
        Y = 3,
        Width = Dim.Percent(60),
        Height = 4,
        ColorScheme = Theme.Schemes.Panel
      };

      var progressFraction = _indexProgress.TotalFiles > 0
        ? (float)_indexProgress.ProcessedFiles / _indexProgress.TotalFiles : 0f;
      var progressText = $"{ProgressHelpers.RenderProgressText(progressFraction, 30)} {_indexProgress.ProcessedFiles}/{_indexProgress.TotalFiles}";

      var statusLabel = ThemeHelper.CreateWarningLabel(progressText);
      statusLabel.X = 1;
      statusLabel.Y = 0;
      indexStatus.Add(statusLabel);

      var fileLabel = ThemeHelper.CreateMutedLabel(_indexProgress.CurrentFile.Length > 40
        ? "..." + _indexProgress.CurrentFile[^37..] : _indexProgress.CurrentFile);
      fileLabel.X = 1;
      fileLabel.Y = 1;
      indexStatus.Add(fileLabel);

      mainMenuView.Add(indexStatus);
      menuStartY = 8;
    }

    var menuItems = new[]
    {
      ("F2 📦 Projects", (Action)(() => ShowProjectsScreen())),
      ("F3 💬 Sessions", (Action)(() => ShowSessionsScreen())),
      ("F4 ⚡ Pipelines", (Action)(() => ShowPipelinesScreen())),
      ("F5 🤖 Chat", (Action)(() => ShowChatScreen())),
      ("^Q 🚪 Quit", (Action)(() => {
        if (DialogHelpers.Confirm("Exit", "Are you sure you want to exit?"))
        {
          Application.RequestStop();
        }
      }))
    };

    int yPos = menuStartY;
    foreach (var (label, action) in menuItems)
    {
      var button = new Button(label)
      {
        X = Pos.Center(),
        Y = yPos,
        ColorScheme = Theme.Schemes.Button
      };
      button.Clicked += () => action();
      mainMenuView.Add(button);
      yPos += 1;
    }

    var hint = ThemeHelper.CreateMutedLabel("↑↓ Navigate  Enter Select  1-5 Quick");
    hint.X = Pos.Center();
    hint.Y = Pos.AnchorEnd(1);
    mainMenuView.Add(hint);

    _contentFrame.Add(mainMenuView);

    // Handle keyboard shortcuts
    mainMenuView.KeyPress += (e) =>
    {
      switch (e.KeyEvent.Key)
      {
        case Key.D1:
          ShowProjectsScreen();
          e.Handled = true;
          break;
        case Key.D2:
          ShowSessionsScreen();
          e.Handled = true;
          break;
        case Key.D3:
          ShowPipelinesScreen();
          e.Handled = true;
          break;
        case Key.D4:
          ShowChatScreen();
          e.Handled = true;
          break;
        case Key.q:
        case Key.Q:
          if (DialogHelpers.Confirm("Exit", "Are you sure you want to exit?"))
          {
            Application.RequestStop();
          }
          e.Handled = true;
          break;
      }
    };

    mainMenuView.SetFocus();
    _logger.LogInformation("Main menu displayed");
  }

  private void ShowWelcomeScreen()
  {
    _logger.LogInformation("ShowWelcomeScreen called");
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "Welcome";

    var titleLabel = ThemeHelper.CreateAccentLabel("🚀 OpenFork - AI Agent Manager");
    titleLabel.X = Pos.Center();
    titleLabel.Y = Pos.Center() - 2;
    titleLabel.TextAlignment = TextAlignment.Centered;

    var helpText = ThemeHelper.CreateMutedLabel("📖 F1 Help │ 🔢 F2-F5 Navigate │ 🚪 ^Q Quit");
    helpText.X = Pos.Center();
    helpText.Y = Pos.Center();
    helpText.TextAlignment = TextAlignment.Centered;

    _contentFrame.Add(titleLabel, helpText);
    _logger.LogInformation("Welcome text added to content frame");
  }

  private void ShowHelp()
  {
    MessageBox.Query("Help",
      @"OpenFork - AI Agent Manager

Navigation:
  F10 / Alt     - Open menu
  Enter         - Select/Confirm
  Esc           - Cancel/Back
  ^Q            - Quit

Chat:
  Tab/Shift+Tab - Switch agent
  Enter/Ctrl+S  - Send message

Workflow:
  1. Select/Create a Project (F2)
  2. Select/Create a Session (F3)
  3. Start Chat (F5) - use Tab to switch agents

For more information, see documentation.",
      "OK");
  }

  private void UpdateContextDisplay()
  {
    _logger.LogInformation("UpdateContextDisplay called");
    if (_contextFrame == null)
    {
      _logger.LogWarning("UpdateContextDisplay: _contextFrame is null");
      return;
    }

    _contextFrame.RemoveAll();

    // Project label with accent color if set
    var projectName = _activeProject?.Name ?? "None";
    var projectLabelText = new Label("📦 ") { X = 1, Y = 0, ColorScheme = Theme.Schemes.Base };
    var projectValue = _activeProject != null
      ? ThemeHelper.CreateAccentLabel(projectName)
      : ThemeHelper.CreateMutedLabel(projectName);
    projectValue.X = 4;
    projectValue.Y = 0;

    // Session label
    var sessionName = _activeSession?.Name ?? "None";
    var sessionLabelText = new Label("│ 💬 ") { X = 20, Y = 0, ColorScheme = Theme.Schemes.Base };
    var sessionValue = _activeSession != null
      ? ThemeHelper.CreateAccentLabel(sessionName)
      : ThemeHelper.CreateMutedLabel(sessionName);
    sessionValue.X = 26;
    sessionValue.Y = 0;

    // Agent label (show current agent)
    var agentName = GetCurrentAgentName();
    var agentLabelText = new Label("│ 🤖 ") { X = 45, Y = 0, ColorScheme = Theme.Schemes.Base };
    var agentValue = agentName != "None"
      ? ThemeHelper.CreateAccentLabel(agentName)
      : ThemeHelper.CreateMutedLabel(agentName);
    agentValue.X = 51;
    agentValue.Y = 0;

    _contextFrame.Add(projectLabelText, projectValue, sessionLabelText, sessionValue, agentLabelText, agentValue);
    _logger.LogInformation("Context display updated with project={Project}, session={Session}, agent={Agent}", projectName, sessionName, agentName);
  }

  private string GetCurrentAgentName()
  {
    if (_activeSession?.ActiveAgentId != null)
    {
      var agent = _agents.GetAsync(_activeSession.ActiveAgentId.Value).GetAwaiter().GetResult();
      if (agent != null)
        return agent.Name;
    }

    if (_activeSession?.ActivePipelineId != null)
    {
      return "Pipeline";
    }

    return "None";
  }

  private void HandleGlobalMouseEvent(MouseEvent me)
  {
    // Handle mouse wheel scrolling for active chat markdown view
    if (_activeChatView == null) return;

    if (me.Flags.HasFlag(MouseFlags.WheeledUp) || me.Flags.HasFlag(MouseFlags.WheeledDown))
    {
      const int scrollLines = 3;

      if (me.Flags.HasFlag(MouseFlags.WheeledUp))
      {
        // Scroll up (toward top)
        _activeChatView.TopRow -= scrollLines;
      }
      else
      {
        // Scroll down (toward bottom)
        _activeChatView.TopRow += scrollLines;
      }

      _activeChatView.SetNeedsDisplay();
    }
  }

  private void ShowProjectsScreen()
  {
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "📦 Projects";

    var projectsView = CreateProjectsView();
    _contentFrame.Add(projectsView);
  }

  private void ShowSessionsScreen()
  {
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "💬 Sessions";

    var sessionsView = CreateSessionsView();
    _contentFrame.Add(sessionsView);
  }

  private void ShowAgentsScreen()
  {
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "🤖 Agents";

    var agentsView = CreateAgentsView();
    _contentFrame.Add(agentsView);
  }

  private void ShowPipelinesScreen()
  {
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "⚡ Pipelines";

    var pipelinesView = CreatePipelinesView();
    _contentFrame.Add(pipelinesView);
  }

  private async void ShowChatScreen()
  {
    if (_activeProject == null)
    {
      FrameHelpers.ShowError("Please select a project first (File → Project → Projects)");
      ShowProjectsScreen();
      return;
    }

    if (_activeSession == null)
    {
      // No active session - show session selection/creation screen
      _logger.LogInformation("No active session, redirecting to session selection");
      ShowSessionSelectionForChat();
      return;
    }

    // Hide menu bar and context frame for clean chat UI
    _menuBar!.Visible = false;
    _contextFrame!.Visible = false;

    // Expand content frame to fill screen (no title bar)
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "";
    _contentFrame.Border.BorderStyle = BorderStyle.None;
    _contentFrame.X = 0;
    _contentFrame.Y = 0;
    _contentFrame.Width = Dim.Fill();
    _contentFrame.Height = Dim.Fill() - 1;  // Leave room for status bar

    // Load history asynchronously
    var history = await _chat.ListMessagesAsync(_activeSession.Id);
    var chatView = CreateChatView(history);
    _contentFrame.Add(chatView);
  }

  private async void ShowSessionSelectionForChat()
  {
    _contentFrame!.RemoveAll();
    _contentFrame.Title = "💬 Select or Create Session";

    var container = new View()
    {
      X = 0,
      Y = 0,
      Width = Dim.Fill(),
      Height = Dim.Fill(),
      ColorScheme = Theme.Schemes.Base
    };

    var infoLabel = ThemeHelper.CreateMutedLabel("📝 Select an existing session or create a new one to start chatting:");
    infoLabel.X = 1;
    infoLabel.Y = 0;
    infoLabel.Width = Dim.Fill() - 2;

    // Get all sessions for current project
    var sessions = await _sessions.ListByProjectAsync(_activeProject!.Id);

    ListView? listView = null;

    if (sessions.Count > 0)
    {
      listView = new ListView()
      {
        X = 1,
        Y = 2,
        Width = Dim.Fill() - 2,
        Height = Dim.Fill() - 5,
        ColorScheme = Theme.Schemes.List
      };

      var items = sessions.Select(s =>
        $"💬 {s.Name} - 📅 {s.CreatedAt:yyyy-MM-dd HH:mm}"
      ).ToList();

      listView.SetSource(items);

      // Double-click to select
      listView.OpenSelectedItem += (e) =>
      {
        if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
        {
          var session = sessions[listView.SelectedItem];
          SelectSessionForChat(session);
        }
      };

      container.Add(listView);
    }
    else
    {
      var noSessionsLabel = ThemeHelper.CreateWarningLabel("📭 No sessions found. Create a new session to start chatting.");
      noSessionsLabel.X = 1;
      noSessionsLabel.Y = 2;
      noSessionsLabel.Width = Dim.Fill() - 2;
      container.Add(noSessionsLabel);
    }

    // Buttons with theme
    var newButton = new Button("➕ _New")
    {
      X = 1,
      Y = Pos.AnchorEnd(2),
      ColorScheme = Theme.Schemes.Button
    };
    newButton.Clicked += () => CreateSessionForChat();

    var selectButton = new Button("✅ _Select")
    {
      X = Pos.Right(newButton) + Layout.ButtonSpacing,
      Y = Pos.AnchorEnd(2),
      ColorScheme = Theme.Schemes.Button
    };
    selectButton.Clicked += () =>
    {
      if (listView != null && listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
      {
        var session = sessions[listView.SelectedItem];
        SelectSessionForChat(session);
      }
    };

    var backButton = new Button("⬅️ _Back")
    {
      X = Pos.Right(selectButton) + Layout.ButtonSpacing,
      Y = Pos.AnchorEnd(2),
      ColorScheme = Theme.Schemes.Button
    };
    backButton.Clicked += () => ShowMainMenu();

    container.Add(infoLabel, newButton, backButton);
    if (sessions.Count > 0)
    {
      container.Add(selectButton);
    }

    _contentFrame.Add(container);
  }

  private async void CreateSessionForChat()
  {
    // Prompt for session name
    var name = DialogHelpers.PromptText(
      "New Session",
      "Session name:",
      $"Session {DateTime.Now:yyyy-MM-dd HH:mm}",
      required: true);

    if (string.IsNullOrWhiteSpace(name))
      return;

    // Create new session
    var session = new Session
    {
      Name = name,
      ProjectId = _activeProject!.Id,
      CreatedAt = DateTimeOffset.UtcNow,
      UpdatedAt = DateTimeOffset.UtcNow
    };

    var created = await _sessions.UpsertAsync(session);
    SelectSessionForChat(created);
    FrameHelpers.ShowSuccess($"Session '{name}' created");
  }

  private async void SelectSessionForChat(Session session)
  {
    _activeSession = session;

    // Load the session's project if it's different from current project
    if (_activeProject?.Id != session.ProjectId)
    {
      _logger.LogInformation("Loading project {ProjectId} for session {SessionId}", session.ProjectId, session.Id);
      var project = await _projects.GetAsync(session.ProjectId);
      if (project != null)
      {
        _activeProject = project;
        await _appState.SetLastProjectIdAsync(project.Id);
        await RefreshIndexStatusAsync();
        StartBackgroundIndexing(project);
      }
    }

    UpdateSessionContext(session);
    _logger.LogInformation("Session {SessionId} selected for chat", session.Id);
    ShowChatScreen();
  }

}
