using OpenFork.Core.Domain;
using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private View CreatePipelinesView()
    {
        if (_activeProject == null)
        {
            FrameHelpers.ShowError("Please select a project first");
            return new View();
        }

        if (_activeSession == null)
        {
            FrameHelpers.ShowError("Please select a session first");
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
        var hintsLabel = new Label("j/k:Navigate  Enter:Activate  n:New  Del:Delete  Esc:Back")
        {
            X = 1,
            Y = 0,
            ColorScheme = Theme.Schemes.Muted
        };

        // Load pipelines
        var pipelines = _pipelines.ListAsync().GetAwaiter().GetResult();

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
        var items = pipelines.Select(p =>
        {
            var isActive = _activeSession?.ActivePipelineId == p.Id;
            var marker = isActive ? $"{Icons.Check} " : "  ";
            var desc = string.IsNullOrWhiteSpace(p.Description) ? "" : $" - {p.Description}";
            return $"{marker}{Icons.Pipeline} {p.Name}{desc}";
        }).ToList();

        listView.SetSource(items);

        // Vim-style navigation
        listView.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.j)
            {
                if (listView.SelectedItem < pipelines.Count - 1)
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
                CreatePipelineDialog();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.DeleteChar || e.KeyEvent.Key == Key.Backspace)
            {
                if (listView.SelectedItem >= 0 && listView.SelectedItem < pipelines.Count)
                    _ = DeletePipeline(pipelines[listView.SelectedItem]);
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

        var newButton = new Button("_New Pipeline")
        {
            X = 1,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        newButton.Clicked += () => CreatePipelineDialog();

        var setActiveButton = new Button("Set _Active")
        {
            X = Pos.Right(newButton) + Layout.ButtonSpacing,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        setActiveButton.Clicked += async () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < pipelines.Count)
            {
                var pipeline = pipelines[listView.SelectedItem];
                await SetActivePipeline(pipeline);
            }
        };

        var deleteButton = new Button("_Delete")
        {
            X = Pos.Right(setActiveButton) + Layout.ButtonSpacing,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        deleteButton.Clicked += async () =>
        {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < pipelines.Count)
            {
                var pipeline = pipelines[listView.SelectedItem];
                await DeletePipeline(pipeline);
            }
        };

        var backButton = new Button("_Back")
        {
            X = Pos.Right(deleteButton) + Layout.ButtonSpacing,
            Y = buttonY,
            ColorScheme = Theme.Schemes.Button
        };
        backButton.Clicked += () => ShowMainMenu();

        container.Add(hintsLabel, listView, newButton, setActiveButton, deleteButton, backButton);

        return container;
    }

    private void CreatePipelineDialog()
    {
        var name = DialogHelpers.PromptText("New Pipeline", "Pipeline name:", "", required: true);
        if (string.IsNullOrWhiteSpace(name))
            return;

        var description = DialogHelpers.PromptText("Pipeline Description", "Description (optional):", "", required: false);

        _ = Task.Run(async () =>
        {
            var pipeline = await _pipelines.UpsertAsync(new Pipeline
            {
                Name = name,
                Description = description
            });

            // Configure steps
            var agents = await _agents.ListAsync();
            if (agents.Count > 0)
            {
                Application.MainLoop.Invoke(() =>
                {
                    ConfigurePipelineStepsDialog(pipeline, agents);
                });
            }
            else
            {
                Application.MainLoop.Invoke(() =>
                {
                    ShowPipelinesScreen();
                    FrameHelpers.ShowSuccess($"Pipeline '{name}' created (no agents available for steps)");
                });
            }
        });
    }

    private void ConfigurePipelineStepsDialog(Pipeline pipeline, List<AgentProfile> agents)
    {
        var stepCountText = DialogHelpers.PromptText("Pipeline Steps", "Number of steps:", "2", required: true);
        if (!int.TryParse(stepCountText, out var stepCount) || stepCount < 1)
        {
            ShowPipelinesScreen();
            return;
        }

        var steps = new List<PipelineStep>();

        for (var i = 0; i < stepCount; i++)
        {
            var agent = DialogHelpers.PromptSelection($"Step {i + 1} - Select Agent", agents, a => $"{a.Name} ({a.Model})");
            if (agent == null)
            {
                ShowPipelinesScreen();
                return;
            }

            var handoffModes = new[] { "full - Pass entire conversation", "last - Pass only last response" };
            var handoffMode = DialogHelpers.PromptSelection($"Step {i + 1} - Handoff Mode", handoffModes, h => h);
            if (handoffMode == null)
            {
                ShowPipelinesScreen();
                return;
            }

            steps.Add(new PipelineStep
            {
                PipelineId = pipeline.Id,
                OrderIndex = i,
                AgentId = agent.Id,
                HandoffMode = handoffMode.Split(' ')[0]
            });
        }

        _ = Task.Run(async () =>
        {
            await _pipelines.UpsertStepsAsync(pipeline.Id, steps);

            Application.MainLoop.Invoke(() =>
            {
                ShowPipelinesScreen();
                FrameHelpers.ShowSuccess($"Pipeline '{pipeline.Name}' created with {steps.Count} steps");
            });
        });
    }

    private async Task SetActivePipeline(Pipeline pipeline)
    {
        if (_activeSession == null)
        {
            FrameHelpers.ShowError("No active session");
            return;
        }

        await RunWithProgress("Setting active pipeline...", async () =>
        {
            _activeSession.ActivePipelineId = pipeline.Id;
            _activeSession.ActiveAgentId = null;
            _activeSession = await _sessions.UpsertAsync(_activeSession);
            UpdateContextDisplay();
        });

        ShowPipelinesScreen();
        FrameHelpers.ShowSuccess($"Pipeline '{pipeline.Name}' activated");
    }

    private async Task DeletePipeline(Pipeline pipeline)
    {
        if (!DialogHelpers.Confirm("Delete Pipeline", $"Are you sure you want to delete pipeline '{pipeline.Name}'?"))
            return;

        await RunWithProgress("Deleting pipeline...", async () =>
        {
            await _pipelines.DeleteAsync(pipeline.Id);
        });

        ShowPipelinesScreen();
        FrameHelpers.ShowSuccess("Pipeline deleted");
    }
}
