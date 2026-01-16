using OpenFork.Core.Domain;
using OpenFork.Search.Services;
using Spectre.Console;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private async Task ProjectsScreenAsync(CancellationToken cancellationToken = default)
    {
        AnsiConsole.Clear();
        RenderHeader();

        var projects = await StatusSpinner.RunAsync("Loading projects...", _projects.ListAsync);
        RenderProjectsTable(projects);

        var choices = BuildProjectChoices(projects);
        var choice = AnsiConsole.Prompt(
            Prompts.Selection<MenuChoice>("Select Project")
                .UseConverter(c => c.Label)
                .AddChoices(choices));

        if (choice.IsBack) return;

        if (choice.IsCreate)
        {
            await CreateProjectAsync();
            return;
        }

        if (choice.Id.HasValue)
        {
            var selected = projects.First(p => p.Id == choice.Id.Value);
            
            if (_activeProject?.Id == selected.Id)
            {
                await ProjectDetailScreenAsync(selected);
            }
            else
            {
                _activeProject = selected;
                await _appState.SetLastProjectIdAsync(_activeProject.Id);
                _activeSession = null;
                await RefreshIndexStatusAsync();
                StartBackgroundIndexing(_activeProject);
            }
        }
    }

    private async Task ProjectDetailScreenAsync(Project project)
    {
        AnsiConsole.Clear();
        RenderHeader();
        
        await RefreshIndexStatusAsync();
        RenderProjectDetail(project);

        var choices = new List<string>
        {
            $"🔄 Reindex project",
            $"{Icons.Back} Back"
        };

        var action = AnsiConsole.Prompt(
            Prompts.Selection<string>("Actions")
                .AddChoices(choices));

        if (action.Contains("Reindex"))
        {
            await ReindexProjectAsync(project);
        }
    }

    private void RenderProjectDetail(Project project)
    {
        var table = Tables.Create("Property", "Value");
        table.AddRow("Name", $"[{Theme.Primary.ToMarkup()}]{Markup.Escape(project.Name)}[/]");
        table.AddRow("Path", $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(project.RootPath)}[/]");
        table.AddRow("Index Status", GetIndexStatusText());

        AnsiConsole.Write(Panels.Create(table, $"{Icons.Project} {project.Name}"));
        AnsiConsole.WriteLine();
    }

    private async Task ReindexProjectAsync(Project project)
    {
        if (!_settings.Search.EnableSemanticSearch)
        {
            AnsiConsole.Write(Panels.Error("Semantic search is disabled"));
            Pause();
            return;
        }

        if (!Directory.Exists(project.RootPath))
        {
            AnsiConsole.Write(Panels.Error($"Project path not found: {project.RootPath}"));
            Pause();
            return;
        }

        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"[{Theme.Muted.ToMarkup()}]Reindexing project...[/]", async ctx =>
                {
                    var progress = new Progress<IndexProgress>(p =>
                    {
                        ctx.Status($"[{Theme.Muted.ToMarkup()}]Indexing: {p.CurrentFile} ({p.ProcessedFiles}/{p.TotalFiles})[/]");
                    });

                    await _indexService.ReindexProjectAsync(project.Id, project.RootPath, progress);
                });

            await RefreshIndexStatusAsync();
            AnsiConsole.Write(Panels.Success("Project reindexed successfully"));
        }
        catch (Exception ex)
        {
            AnsiConsole.Write(Panels.Error($"Reindex failed: {ex.Message}"));
        }

        Pause();
    }

    private async Task CreateProjectAsync()
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.Write(Panels.Create(new Text("Create a new project workspace"), "New Project"));
        AnsiConsole.WriteLine();

        var name = AnsiConsole.Prompt(Prompts.RequiredText("Project name"));
        var path = SelectDirectory(Environment.CurrentDirectory);

        if (string.IsNullOrWhiteSpace(path)) return;

        var project = new Project { Name = name, RootPath = path };
        _activeProject = await StatusSpinner.RunAsync("Creating project...", () => _projects.UpsertAsync(project));
        await _appState.SetLastProjectIdAsync(_activeProject.Id);
        _activeSession = null;

        StartBackgroundIndexing(_activeProject);

        AnsiConsole.Write(Panels.Success($"Project '{name}' created. Indexing in background..."));
        Pause();
    }

    private void RenderProjectsTable(List<Project> projects)
    {
        if (projects.Count == 0)
        {
            AnsiConsole.Write(Panels.Info("No projects yet. Create one to get started!"));
            AnsiConsole.WriteLine();
            return;
        }

        var table = Tables.Create("Id", "Name", "Path");
        foreach (var project in projects)
        {
            var isActive = _activeProject?.Id == project.Id;
            var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
            table.AddRow(
                $"{marker}{project.Id}",
                $"[{(isActive ? Theme.Success : Theme.Primary).ToMarkup()}]{Markup.Escape(project.Name)}[/]",
                $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(project.RootPath)}[/]"
            );
        }

        AnsiConsole.Write(Panels.Create(table, $"{Icons.Project} Projects"));
        AnsiConsole.WriteLine();
    }

    private List<MenuChoice> BuildProjectChoices(List<Project> projects)
    {
        var choices = new List<MenuChoice>
        {
            new($"[{Theme.Success.ToMarkup()}]{Icons.Add} Create new project[/]", null, true, false)
        };

        choices.AddRange(projects.Select(p =>
        {
            var isActive = _activeProject?.Id == p.Id;
            var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
            return new MenuChoice($"{marker}[{Theme.Primary.ToMarkup()}]{Markup.Escape(p.Name)}[/]  [{Theme.Muted.ToMarkup()}]{Markup.Escape(p.RootPath)}[/]", p.Id, false, false);
        }));

        choices.Add(new MenuChoice($"[{Theme.Muted.ToMarkup()}]{Icons.Back} Back[/]", null, false, true));
        return choices;
    }
}
