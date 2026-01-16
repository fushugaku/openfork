using OpenFork.Core.Domain;
using Spectre.Console;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private async Task SessionsScreenAsync(CancellationToken cancellationToken = default)
    {
        if (_activeProject == null)
        {
            AnsiConsole.Write(Panels.Error("Select a project first"));
            Pause();
            return;
        }

        AnsiConsole.Clear();
        RenderHeader();
        RenderContext();

        var sessions = await StatusSpinner.RunAsync("Loading sessions...", () => _sessions.ListByProjectAsync(_activeProject.Id));
        RenderSessionsTable(sessions);

        var choices = BuildSessionChoices(sessions);
        var choice = AnsiConsole.Prompt(
            Prompts.Selection<MenuChoice>($"Sessions for {_activeProject.Name}")
                .UseConverter(c => c.Label)
                .AddChoices(choices));

        if (choice.IsBack) return;

        if (choice.IsCreate)
        {
            var name = AnsiConsole.Prompt(Prompts.RequiredText("Session name"));
            var session = new Session { ProjectId = _activeProject.Id, Name = name };
            _activeSession = await StatusSpinner.RunAsync("Creating session...", () => _sessions.UpsertAsync(session));
            return;
        }

        if (choice.Id.HasValue)
        {
            _activeSession = sessions.First(s => s.Id == choice.Id.Value);
            await SyncProjectFromSessionAsync();
        }
    }

    private async Task EnsureSessionSelectedAsync()
    {
        var sessions = await StatusSpinner.RunAsync("Loading sessions...", () => _sessions.ListByProjectAsync(_activeProject!.Id));

        var choices = new List<MenuChoice>
        {
            new($"[{Theme.Success.ToMarkup()}]{Icons.Add} Create new session[/]", null, true, false)
        };

        choices.AddRange(sessions.Select(s => new MenuChoice(
            $"[{Theme.Primary.ToMarkup()}]{Markup.Escape(s.Name)}[/]  [{Theme.Muted.ToMarkup()}]#{s.Id}[/]",
            s.Id, false, false)));

        choices.Add(new MenuChoice($"[{Theme.Muted.ToMarkup()}]{Icons.Back} Back[/]", null, false, true));

        var selection = AnsiConsole.Prompt(
            Prompts.Selection<MenuChoice>("Select Session")
                .UseConverter(c => c.Label)
                .AddChoices(choices));

        if (selection.IsBack) return;

        if (selection.IsCreate)
        {
            var name = AnsiConsole.Prompt(Prompts.RequiredText("Session name"));
            var session = new Session { ProjectId = _activeProject!.Id, Name = name };
            _activeSession = await StatusSpinner.RunAsync("Creating session...", () => _sessions.UpsertAsync(session));
            return;
        }

        if (selection.Id.HasValue)
        {
            _activeSession = sessions.First(s => s.Id == selection.Id.Value);
            await SyncProjectFromSessionAsync();
        }
    }

    private async Task SyncProjectFromSessionAsync()
    {
        if (_activeSession == null || _activeProject?.Id == _activeSession.ProjectId) 
            return;

        _activeProject = await StatusSpinner.RunAsync("Loading project...", () => _projects.GetAsync(_activeSession.ProjectId));
        if (_activeProject != null)
        {
            await _appState.SetLastProjectIdAsync(_activeProject.Id);
            await RefreshIndexStatusAsync();
            StartBackgroundIndexing(_activeProject);
        }
    }

    private void RenderSessionsTable(List<Session> sessions)
    {
        if (sessions.Count == 0)
        {
            AnsiConsole.Write(Panels.Info("No sessions yet. Create one to start chatting!"));
            AnsiConsole.WriteLine();
            return;
        }

        var table = Tables.Create("Id", "Name", "Updated");
        foreach (var session in sessions)
        {
            var isActive = _activeSession?.Id == session.Id;
            var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
            table.AddRow(
                $"{marker}{session.Id}",
                $"[{(isActive ? Theme.Success : Theme.Primary).ToMarkup()}]{Markup.Escape(session.Name)}[/]",
                $"[{Theme.Muted.ToMarkup()}]{session.UpdatedAt.ToLocalTime():g}[/]"
            );
        }

        AnsiConsole.Write(Panels.Create(table, $"{Icons.Session} Sessions"));
        AnsiConsole.WriteLine();
    }

    private List<MenuChoice> BuildSessionChoices(List<Session> sessions)
    {
        var choices = new List<MenuChoice>
        {
            new($"[{Theme.Success.ToMarkup()}]{Icons.Add} Create new session[/]", null, true, false)
        };

        choices.AddRange(sessions.Select(s =>
        {
            var isActive = _activeSession?.Id == s.Id;
            var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
            return new MenuChoice($"{marker}[{Theme.Primary.ToMarkup()}]{Markup.Escape(s.Name)}[/]  [{Theme.Muted.ToMarkup()}]{s.UpdatedAt.ToLocalTime():g}[/]", s.Id, false, false);
        }));

        choices.Add(new MenuChoice($"[{Theme.Muted.ToMarkup()}]{Icons.Back} Back[/]", null, false, true));
        return choices;
    }
}
