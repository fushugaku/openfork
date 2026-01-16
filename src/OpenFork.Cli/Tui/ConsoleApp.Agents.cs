using OpenFork.Core.Domain;
using Spectre.Console;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
  private async Task AgentsScreenAsync(CancellationToken cancellationToken = default)
  {
    AnsiConsole.Clear();
    RenderHeader();
    RenderContext();

    if (_activeProject == null)
    {
      AnsiConsole.Write(Panels.Error("Select a project first"));
      Pause();
      return;
    }

    if (_activeSession == null)
    {
      await EnsureSessionSelectedAsync();
      if (_activeSession == null) return;
    }

    var agents = await StatusSpinner.RunAsync("Loading agents...", _agents.ListAsync);
    RenderAgentsTable(agents);

    var choices = BuildAgentChoices(agents);
    var selection = AnsiConsole.Prompt(
        Prompts.Selection<MenuChoice>("Agents")
            .UseConverter(c => c.Label)
            .AddChoices(choices));

    if (selection.IsBack) return;

    if (selection.IsCreate)
    {
      await CreateAgentAsync();
      return;
    }

    if (!selection.Id.HasValue) return;

    var selected = agents.First(a => a.Id == selection.Id.Value);
    await AgentDetailScreenAsync(selected);
  }

  private async Task AgentDetailScreenAsync(AgentProfile agent)
  {
    AnsiConsole.Clear();
    RenderHeader();
    RenderAgentDetail(agent);

    var action = AnsiConsole.Prompt(
        Prompts.Selection<string>("Actions")
            .AddChoices($"{Icons.Check} Set Active", $"{Icons.Settings} Edit", $"🗑️ Delete", $"{Icons.Back} Back"));

    switch (action)
    {
      case var a when a.Contains("Set Active"):
        _activeSession!.ActiveAgentId = agent.Id;
        _activeSession.ActivePipelineId = null;
        _activeSession = await StatusSpinner.RunAsync("Saving...", () => _sessions.UpsertAsync(_activeSession));
        break;
      case var a when a.Contains("Edit"):
        await EditAgentAsync(agent);
        break;
      case var a when a.Contains("Delete"):
        if (AnsiConsole.Prompt(Prompts.Confirm($"Delete agent '{agent.Name}'?")))
        {
          await StatusSpinner.RunAsync("Deleting...", () => _agents.DeleteAsync(agent.Id));
        }
        break;
    }
  }

  private async Task CreateAgentAsync()
  {
    AnsiConsole.Clear();
    RenderHeader();
    AnsiConsole.Write(Panels.Create(new Text("Configure a new AI agent"), "New Agent"));
    AnsiConsole.WriteLine();

    var name = AnsiConsole.Prompt(Prompts.RequiredText("Agent name"));
    var providerKey = SelectProviderKey();
    var model = SelectModel(providerKey);

    AnsiConsole.WriteLine();
    var systemPrompt = MultilineInput.Read("System prompt");

    var summaryTable = Tables.Create("Field", "Value");
    summaryTable.AddRow("Name", name);
    summaryTable.AddRow("Provider", providerKey);
    summaryTable.AddRow("Model", model);
    summaryTable.AddRow("System Prompt", systemPrompt.Length > 50 ? $"{systemPrompt[..50]}..." : systemPrompt);

    AnsiConsole.WriteLine();
    AnsiConsole.Write(Panels.Create(summaryTable, "Review"));

    if (!AnsiConsole.Prompt(Prompts.Confirm("Create this agent?"))) return;

    var agent = new AgentProfile
    {
      Name = name,
      SystemPrompt = systemPrompt,
      ProviderKey = providerKey,
      Model = model
    };

    await StatusSpinner.RunAsync("Creating agent...", () => _agents.UpsertAsync(agent));
    AnsiConsole.Write(Panels.Success($"Agent '{name}' created"));
    Pause();
  }

  private async Task EditAgentAsync(AgentProfile agent)
  {
    AnsiConsole.Clear();
    RenderHeader();
    AnsiConsole.Write(Panels.Create(new Text($"Editing: {agent.Name}"), "Edit Agent"));
    AnsiConsole.WriteLine();

    var name = AnsiConsole.Prompt(
        Prompts.RequiredText("Agent name")
            .DefaultValue(agent.Name));

    var providerKey = SelectProviderKey();
    var model = SelectModel(providerKey);

    AnsiConsole.WriteLine();
    var systemPrompt = MultilineInput.ReadWithDefault("System prompt", agent.SystemPrompt);

    agent.Name = name;
    agent.SystemPrompt = systemPrompt;
    agent.ProviderKey = providerKey;
    agent.Model = model;

    await StatusSpinner.RunAsync("Saving agent...", () => _agents.UpsertAsync(agent));
    AnsiConsole.Write(Panels.Success("Agent updated"));
    Pause();
  }

  private void RenderAgentsTable(List<AgentProfile> agents)
  {
    if (agents.Count == 0)
    {
      AnsiConsole.Write(Panels.Info("No agents configured. Create one to start!"));
      AnsiConsole.WriteLine();
      return;
    }

    var table = Tables.Create("Id", "Name", "Provider", "Model");
    foreach (var agent in agents)
    {
      var isActive = _activeSession?.ActiveAgentId == agent.Id;
      var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
      table.AddRow(
          $"{marker}{agent.Id}",
          $"[{(isActive ? Theme.Success : Theme.Primary).ToMarkup()}]{Markup.Escape(agent.Name)}[/]",
          $"[{Theme.Secondary.ToMarkup()}]{Markup.Escape(agent.ProviderKey)}[/]",
          $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(agent.Model)}[/]"
      );
    }

    AnsiConsole.Write(Panels.Create(table, $"{Icons.Agent} Agents"));
    AnsiConsole.WriteLine();
  }

  private void RenderAgentDetail(AgentProfile agent)
  {
    var table = Tables.Create("Property", "Value");
    table.AddRow("Name", $"[{Theme.Primary.ToMarkup()}]{Markup.Escape(agent.Name)}[/]");
    table.AddRow("Provider", $"[{Theme.Secondary.ToMarkup()}]{Markup.Escape(agent.ProviderKey)}[/]");
    table.AddRow("Model", $"[{Theme.Accent.ToMarkup()}]{Markup.Escape(agent.Model)}[/]");
    table.AddRow("System Prompt", agent.SystemPrompt.Length > 100
        ? $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(agent.SystemPrompt[..100])}...[/]"
        : $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(agent.SystemPrompt)}[/]");

    AnsiConsole.Write(Panels.Create(table, $"{Icons.Agent} {agent.Name}"));
    AnsiConsole.WriteLine();
  }

  private List<MenuChoice> BuildAgentChoices(List<AgentProfile> agents)
  {
    var choices = new List<MenuChoice>
        {
            new($"[{Theme.Success.ToMarkup()}]{Icons.Add} Create new agent[/]", null, true, false)
        };

    choices.AddRange(agents.Select(a =>
    {
      var isActive = _activeSession?.ActiveAgentId == a.Id;
      var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
      return new MenuChoice($"{marker}[{Theme.Primary.ToMarkup()}]{Markup.Escape(a.Name)}[/]  [{Theme.Muted.ToMarkup()}]{a.ProviderKey}/{a.Model}[/]", a.Id, false, false);
    }));

    choices.Add(new MenuChoice($"[{Theme.Muted.ToMarkup()}]{Icons.Back} Back[/]", null, false, true));
    return choices;
  }
}
