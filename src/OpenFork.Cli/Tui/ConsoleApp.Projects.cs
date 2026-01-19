using OpenFork.Core.Domain;
using OpenFork.Search.Services;
using Terminal.Gui;
using Microsoft.Extensions.Logging;

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
      Height = Dim.Fill(),
      ColorScheme = Theme.Schemes.Base
    };

    // Keyboard hints at top
    var hintsLabel = new Label("j/k:Navigate  Enter:Select  n:New  d:Details  Esc:Back")
    {
      X = 1,
      Y = 0,
      ColorScheme = Theme.Schemes.Muted
    };

    // Load projects
    var projects = _projects.ListAsync().GetAwaiter().GetResult();

    // Create ListView
    var listView = new ListView()
    {
      X = 0,
      Y = 1,
      Width = Dim.Fill(),
      Height = Dim.Fill() - 5,
      AllowsMarking = false,
      CanFocus = true,
      ColorScheme = Theme.Schemes.List
    };

    // Format items with active indicator
    var items = projects.Select(p =>
    {
      var isActive = _activeProject?.Id == p.Id;
      var marker = isActive ? $"{Icons.Check} " : "  ";
      return $"{marker}{Icons.Project} {p.Name} - {p.RootPath}";
    }).ToList();

    listView.SetSource(items);

    // Vim-style navigation
    listView.KeyPress += (e) =>
    {
      if (e.KeyEvent.Key == Key.j)
      {
        if (listView.SelectedItem < projects.Count - 1)
          listView.SelectedItem++;
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.k)
      {
        if (listView.SelectedItem > 0)
          listView.SelectedItem--;
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.n)
      {
        _ = CreateProjectAsync();
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.d)
      {
        if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
          ShowProjectDetail(projects[listView.SelectedItem]);
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.Esc)
      {
        ShowMainMenu();
        e.Handled = true;
      }
    };

    // Button panel
    var buttonY = Pos.Bottom(listView) + 1;

    var newButton = new Button("_New Project")
    {
      X = 1,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    newButton.Clicked += async () => await CreateProjectAsync();

    var selectButton = new Button("_Select")
    {
      X = Pos.Right(newButton) + Layout.ButtonSpacing,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    selectButton.Clicked += () =>
    {
      if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
      {
        var project = projects[listView.SelectedItem];
        SelectProject(project);
      }
    };

    var detailButton = new Button("_Details")
    {
      X = Pos.Right(selectButton) + Layout.ButtonSpacing,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    detailButton.Clicked += () =>
    {
      if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
      {
        var project = projects[listView.SelectedItem];
        ShowProjectDetail(project);
      }
    };

    var backButton = new Button("_Back")
    {
      X = Pos.Right(detailButton) + Layout.ButtonSpacing,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    backButton.Clicked += () => ShowMainMenu();

    // Double-click to select
    listView.OpenSelectedItem += (e) =>
    {
      if (listView.SelectedItem >= 0 && listView.SelectedItem < projects.Count)
      {
        var project = projects[listView.SelectedItem];
        SelectProject(project);
      }
    };

    container.Add(hintsLabel, listView, newButton, selectButton, detailButton, backButton);

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

      // Only clear session if it doesn't belong to this project
      if (_activeSession != null && _activeSession.ProjectId != project.Id)
      {
        _activeSession = null;
        UpdateContextDisplay();
      }

      ShowMainMenu();
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
    var path = SelectDirectory(Environment.CurrentDirectory);

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

      // Only clear session if it doesn't belong to this new project (though unlikely)
      if (_activeSession != null && _activeSession.ProjectId != _activeProject.Id)
      {
        _activeSession = null;
      }

      StartBackgroundIndexing(_activeProject);
    });

    UpdateContextDisplay();
    ShowProjectsScreen();
    FrameHelpers.ShowSuccess($"Project '{name}' created. Indexing in background...");
  }

  private void ShowProjectDetail(Project project)
  {
    var dialog = new Dialog($"{Icons.Project} Project: {project.Name}")
    {
      Width = Dim.Percent(80),
      Height = 15,
      ColorScheme = Theme.Schemes.Base
    };

    // Project info
    var infoFrame = new FrameView("Information")
    {
      X = 1,
      Y = 1,
      Width = Dim.Fill() - 1,
      Height = 8,
      ColorScheme = Theme.Schemes.Panel
    };

    var nameLabel = ThemeHelper.CreateAccentLabel($"Name: {project.Name}");
    nameLabel.X = 1;
    nameLabel.Y = 0;

    var pathLabel = new Label($"Path: {project.RootPath}")
    {
      X = 1,
      Y = 1
    };

    var indexStatus = GetIndexStatusText();
    var indexStatusLabel = _isIndexing
      ? ThemeHelper.CreateWarningLabel($"Index Status: {indexStatus}")
      : new Label($"Index Status: {indexStatus}") { X = 1, Y = 2 };
    if (!_isIndexing)
    {
      indexStatusLabel.X = 1;
      indexStatusLabel.Y = 2;
    }

    infoFrame.Add(nameLabel, pathLabel, indexStatusLabel);

    // Buttons
    var reindexButton = new Button("_Reindex")
    {
      X = 1,
      Y = Pos.Bottom(infoFrame) + 1,
      ColorScheme = Theme.Schemes.Button
    };
    reindexButton.Clicked += () =>
    {
      dialog.RequestStop();
      ReindexProjectAsync(project);
    };

    var closeButton = new Button("_Close")
    {
      X = Pos.Right(reindexButton) + Layout.ButtonSpacing,
      Y = Pos.Bottom(infoFrame) + 1,
      IsDefault = true,
      ColorScheme = Theme.Schemes.Button
    };
    closeButton.Clicked += () => dialog.RequestStop();

    dialog.Add(infoFrame, reindexButton, closeButton);
    Application.Run(dialog);
  }

  private void ReindexProjectAsync(Project project)
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

    if (_isIndexing)
    {
      FrameHelpers.ShowInfo("Indexing is already in progress");
      return;
    }

    // Cancel any existing indexing
    _indexingCts?.Cancel();
    _indexingCts = new CancellationTokenSource();
    var token = _indexingCts.Token;

    _isIndexing = true;
    _indexProgress = null;

    _logger.LogInformation("Starting background reindex for project {ProjectId}", project.Id);

    // Start background indexing
    _ = Task.Run(async () =>
    {
      try
      {
        var progress = new Progress<IndexProgress>(p =>
            {
              _indexProgress = p;
              Application.MainLoop.Invoke(() =>
                  {
                    UpdateContextDisplay();
                    // Update main menu if it's showing
                    if (_contentFrame?.Title == "Main Menu")
                    {
                      ShowMainMenu();
                    }
                  });
            });

        await _indexService.ReindexProjectAsync(project.Id, project.RootPath, progress, token);

        if (!token.IsCancellationRequested)
        {
          _cachedIndexStatus = await _indexService.GetIndexStatusAsync(project.Id, token);
          Application.MainLoop.Invoke(() =>
              {
                UpdateContextDisplay();
                FrameHelpers.ShowSuccess("Project reindexed successfully");
                _logger.LogInformation("Reindex completed for project {ProjectId}", project.Id);
                // Update main menu if showing
                if (_contentFrame?.Title == "Main Menu")
                {
                  ShowMainMenu();
                }
              });
        }
      }
      catch (OperationCanceledException)
      {
        _logger.LogInformation("Reindex cancelled for project {ProjectId}", project.Id);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error reindexing project {ProjectId}", project.Id);
        Application.MainLoop.Invoke(() =>
            {
              FrameHelpers.ShowError($"Reindex failed: {ex.Message}");
            });
      }
      finally
      {
        _isIndexing = false;
        _indexProgress = null;
        Application.MainLoop.Invoke(UpdateContextDisplay);
      }
    }, token);

    ShowProjectsScreen();
    FrameHelpers.ShowInfo("Reindexing started in background. Check main menu for progress.");
  }
}
