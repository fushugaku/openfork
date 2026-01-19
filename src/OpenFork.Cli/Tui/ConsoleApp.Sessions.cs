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
            Height = Dim.Fill(),
            ColorScheme = Theme.Schemes.Base
        };

        // Keyboard hints at top
        var hintsLabel = new Label("j/k:Navigate  Enter:Select  n:New  Del:Delete  Esc:Back")
        {
            X = 1,
            Y = 0,
            ColorScheme = Theme.Schemes.Muted
        };

        // Load sessions for active project
        var sessions = _sessions.ListByProjectAsync(_activeProject.Id).GetAwaiter().GetResult();

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

        // Format items
        var items = sessions.Select(s =>
        {
            var isActive = _activeSession?.Id == s.Id;
            var marker = isActive ? $"{Icons.Check} " : "  ";
            return $"{marker}{Icons.Session} {s.Name} - {s.UpdatedAt.ToLocalTime():g}";
        }).ToList();

        listView.SetSource(items);

        // Vim-style navigation
        listView.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.j)
            {
                if (listView.SelectedItem < sessions.Count - 1)
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
                CreateNewSession();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.DeleteChar || e.KeyEvent.Key == Key.Backspace)
            {
                if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
                    _ = DeleteSessionAsync(sessions[listView.SelectedItem]);
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.Esc)
            {
                ShowMainMenu();
                e.Handled = true;
            }
        };

        // Buttons
        var buttonY = Pos.Bottom(listView) + 1;

        var newButton = new Button("_New Session")
        {
            X = 1,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        newButton.Clicked += () => CreateNewSession();

        var selectButton = new Button("_Select")
        {
            X = Pos.Right(newButton) + Layout.ButtonSpacing,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        selectButton.Clicked += async () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
            {
                var session = sessions[listView.SelectedItem];
                await SelectSessionAsync(session);
            }
        };

        var deleteButton = new Button("_Delete")
        {
            X = Pos.Right(selectButton) + Layout.ButtonSpacing,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        deleteButton.Clicked += async () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
            {
                var session = sessions[listView.SelectedItem];
                await DeleteSessionAsync(session);
            }
        };

        var backButton = new Button("_Back")
        {
            X = Pos.Right(deleteButton) + Layout.ButtonSpacing,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        backButton.Clicked += () => ShowMainMenu();

        // Double-click to select
        listView.OpenSelectedItem += async (e) =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < sessions.Count)
            {
                var session = sessions[listView.SelectedItem];
                await SelectSessionAsync(session);
            }
        };

        container.Add(hintsLabel, listView, newButton, selectButton, deleteButton, backButton);

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
        ShowMainMenu();
        FrameHelpers.ShowInfo($"Session '{session.Name}' selected");
    }

    private async Task DeleteSessionAsync(Session session)
    {
        if (!DialogHelpers.Confirm("Delete Session",
            $"Are you sure you want to delete session '{session.Name}'?"))
            return;

        // Note: SessionService doesn't have DeleteAsync - would need to add to repository/service
        FrameHelpers.ShowError("Delete session not implemented yet");
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
