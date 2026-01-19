using OpenFork.Core.Domain;
using Terminal.Gui;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
  private View CreateAgentsView()
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
    var hintsLabel = new Label("j/k:Navigate  Enter:Activate  n:New  e:Edit  Del:Delete  Esc:Back")
    {
      X = 1,
      Y = 0,
      ColorScheme = Theme.Schemes.Muted
    };

    // Load agents
    var agents = _agents.ListAsync().GetAwaiter().GetResult();

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
    var items = agents.Select(a =>
    {
      var isActive = _activeSession?.ActiveAgentId == a.Id;
      var marker = isActive ? $"{Icons.Check} " : "  ";
      return $"{marker}{Icons.Agent} {a.Name} - {a.ProviderKey}/{a.Model}";
    }).ToList();

    listView.SetSource(items);

    // Vim-style navigation
    listView.KeyPress += (e) =>
    {
      if (e.KeyEvent.Key == Key.j)
      {
        if (listView.SelectedItem < agents.Count - 1)
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
        CreateAgentDialog();
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.e)
      {
        if (listView.SelectedItem >= 0 && listView.SelectedItem < agents.Count)
          EditAgentDialog(agents[listView.SelectedItem]);
        e.Handled = true;
      }
      else if (e.KeyEvent.Key == Key.DeleteChar || e.KeyEvent.Key == Key.Backspace)
      {
        if (listView.SelectedItem >= 0 && listView.SelectedItem < agents.Count)
          _ = DeleteAgent(agents[listView.SelectedItem]);
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

    var newButton = new Button("_New Agent")
    {
      X = 1,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    newButton.Clicked += () => CreateAgentDialog();

    var editButton = new Button("_Edit")
    {
      X = Pos.Right(newButton) + Layout.ButtonSpacing,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    editButton.Clicked += () =>
    {
      if (listView.SelectedItem >= 0 && listView.SelectedItem < agents.Count)
      {
        var agent = agents[listView.SelectedItem];
        EditAgentDialog(agent);
      }
    };

    var setActiveButton = new Button("Set _Active")
    {
      X = Pos.Right(editButton) + Layout.ButtonSpacing,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    setActiveButton.Clicked += async () =>
    {
      if (listView.SelectedItem >= 0 && listView.SelectedItem < agents.Count)
      {
        var agent = agents[listView.SelectedItem];
        await SetActiveAgent(agent);
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
      if (listView.SelectedItem >= 0 && listView.SelectedItem < agents.Count)
      {
        var agent = agents[listView.SelectedItem];
        await DeleteAgent(agent);
      }
    };

    var backButton = new Button("_Back")
    {
      X = Pos.Right(deleteButton) + Layout.ButtonSpacing,
      Y = buttonY,
      ColorScheme = Theme.Schemes.Button
    };
    backButton.Clicked += () => ShowMainMenu();

    container.Add(hintsLabel, listView, newButton, editButton, setActiveButton, deleteButton, backButton);

    return container;
  }

  private void CreateAgentDialog()
  {
    var name = DialogHelpers.PromptText("New Agent", "Agent name:", "", required: true);
    if (string.IsNullOrWhiteSpace(name))
      return;

    var providerKey = SelectProviderKey();
    var model = SelectModel(providerKey);
    var systemPrompt = TextViewHelpers.PromptMultiline("System Prompt", "Enter system prompt:", "");

    if (!DialogHelpers.Confirm("Create Agent", $"Create agent '{name}'?"))
      return;

    _ = Task.Run(async () =>
    {
      var agent = new AgentProfile
      {
        Name = name,
        SystemPrompt = systemPrompt,
        ProviderKey = providerKey,
        Model = model
      };

      await _agents.UpsertAsync(agent);

      Application.MainLoop.Invoke(() =>
          {
            ShowAgentsScreen();
            FrameHelpers.ShowSuccess($"Agent '{name}' created");
          });
    });
  }

  private void EditAgentDialog(AgentProfile agent)
  {
    var name = DialogHelpers.PromptText("Edit Agent", "Agent name:", agent.Name, required: true);
    if (string.IsNullOrWhiteSpace(name))
      return;

    var providerKey = SelectProviderKey();
    var model = SelectModel(providerKey);
    var systemPrompt = TextViewHelpers.PromptMultiline("System Prompt", "Enter system prompt:", agent.SystemPrompt);

    if (!DialogHelpers.Confirm("Update Agent", $"Update agent '{name}'?"))
      return;

    _ = Task.Run(async () =>
    {
      agent.Name = name;
      agent.SystemPrompt = systemPrompt;
      agent.ProviderKey = providerKey;
      agent.Model = model;

      await _agents.UpsertAsync(agent);

      Application.MainLoop.Invoke(() =>
          {
            ShowAgentsScreen();
            FrameHelpers.ShowSuccess("Agent updated");
          });
    });
  }

  private async Task SetActiveAgent(AgentProfile agent)
  {
    if (_activeSession == null)
    {
      FrameHelpers.ShowError("No active session");
      return;
    }

    await RunWithProgress("Setting active agent...", async () =>
    {
      _activeSession.ActiveAgentId = agent.Id;
      _activeSession.ActivePipelineId = null;
      _activeSession = await _sessions.UpsertAsync(_activeSession);
      UpdateContextDisplay();
    });

    ShowAgentsScreen();
    FrameHelpers.ShowSuccess($"Agent '{agent.Name}' activated");
  }

  private async Task DeleteAgent(AgentProfile agent)
  {
    if (!DialogHelpers.Confirm("Delete Agent", $"Are you sure you want to delete agent '{agent.Name}'?"))
      return;

    await RunWithProgress("Deleting agent...", async () =>
    {
      await _agents.DeleteAsync(agent.Id);
    });

    ShowAgentsScreen();
    FrameHelpers.ShowSuccess("Agent deleted");
  }
}
