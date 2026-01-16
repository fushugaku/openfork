using OpenFork.Core.Domain;
using Spectre.Console;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
    private async Task PipelinesScreenAsync(CancellationToken cancellationToken = default)
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

        var pipelines = await StatusSpinner.RunAsync("Loading pipelines...", _pipelines.ListAsync);
        RenderPipelinesTable(pipelines);

        var choices = BuildPipelineChoices(pipelines);
        var selection = AnsiConsole.Prompt(
            Prompts.Selection<MenuChoice>("Pipelines")
                .UseConverter(c => c.Label)
                .AddChoices(choices));

        if (selection.IsBack) return;

        if (selection.IsCreate)
        {
            await CreatePipelineAsync();
            return;
        }

        if (!selection.Id.HasValue) return;

        var selected = pipelines.First(p => p.Id == selection.Id.Value);
        await PipelineDetailScreenAsync(selected);
    }

    private async Task PipelineDetailScreenAsync(Pipeline pipeline)
    {
        AnsiConsole.Clear();
        RenderHeader();

        var steps = await StatusSpinner.RunAsync("Loading steps...", () => _pipelines.ListStepsAsync(pipeline.Id));
        RenderPipelineDetail(pipeline, steps);

        var action = AnsiConsole.Prompt(
            Prompts.Selection<string>("Actions")
                .AddChoices($"{Icons.Check} Set Active", $"{Icons.Settings} Edit", "🗑️ Delete", $"{Icons.Back} Back"));

        switch (action)
        {
            case var a when a.Contains("Set Active"):
                _activeSession!.ActivePipelineId = pipeline.Id;
                _activeSession.ActiveAgentId = null;
                _activeSession = await StatusSpinner.RunAsync("Saving...", () => _sessions.UpsertAsync(_activeSession));
                break;
            case var a when a.Contains("Edit"):
                await EditPipelineAsync(pipeline);
                break;
            case var a when a.Contains("Delete"):
                if (AnsiConsole.Prompt(Prompts.Confirm($"Delete pipeline '{pipeline.Name}'?")))
                {
                    await StatusSpinner.RunAsync("Deleting...", () => _pipelines.DeleteAsync(pipeline.Id));
                }
                break;
        }
    }

    private async Task CreatePipelineAsync()
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.Write(Panels.Create(new Text("Configure a new agent pipeline"), "New Pipeline"));
        AnsiConsole.WriteLine();

        var name = AnsiConsole.Prompt(Prompts.RequiredText("Pipeline name"));
        var description = AnsiConsole.Prompt(Prompts.OptionalText("Description"));

        var pipeline = await StatusSpinner.RunAsync("Creating pipeline...",
            () => _pipelines.UpsertAsync(new Pipeline { Name = name, Description = description }));

        await ConfigurePipelineStepsAsync(pipeline);
        AnsiConsole.Write(Panels.Success($"Pipeline '{name}' created"));
        Pause();
    }

    private async Task EditPipelineAsync(Pipeline pipeline)
    {
        AnsiConsole.Clear();
        RenderHeader();
        AnsiConsole.Write(Panels.Create(new Text($"Editing: {pipeline.Name}"), "Edit Pipeline"));
        AnsiConsole.WriteLine();

        var name = AnsiConsole.Prompt(
            Prompts.RequiredText("Pipeline name")
                .DefaultValue(pipeline.Name));

        var description = AnsiConsole.Prompt(
            Prompts.OptionalText("Description", pipeline.Description ?? ""));

        pipeline.Name = name;
        pipeline.Description = description;

        await StatusSpinner.RunAsync("Saving pipeline...", () => _pipelines.UpsertAsync(pipeline));
        await ConfigurePipelineStepsAsync(pipeline);
    }

    private async Task ConfigurePipelineStepsAsync(Pipeline pipeline)
    {
        var agents = await StatusSpinner.RunAsync("Loading agents...", _agents.ListAsync);

        if (agents.Count == 0)
        {
            AnsiConsole.Write(Panels.Error("No agents available. Create an agent first."));
            return;
        }

        var stepCount = AnsiConsole.Prompt(Prompts.Number("Number of steps", 2));
        var steps = new List<PipelineStep>();

        for (var i = 0; i < stepCount; i++)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[{Theme.Primary.ToMarkup()}]Step {i + 1}[/]").LeftJustified());

            var agentChoice = AnsiConsole.Prompt(
                Prompts.Selection<AgentProfile>("Select agent")
                    .UseConverter(a => $"{Icons.Agent} {a.Name} [{Theme.Muted.ToMarkup()}]{a.Model}[/]")
                    .AddChoices(agents));

            var handoff = AnsiConsole.Prompt(
                Prompts.Selection<string>("Handoff mode")
                    .AddChoices("full - Pass entire conversation", "last - Pass only last response")
                    .UseConverter(h => h));

            steps.Add(new PipelineStep
            {
                PipelineId = pipeline.Id,
                OrderIndex = i,
                AgentId = agentChoice.Id,
                HandoffMode = handoff.Split(' ')[0]
            });
        }

        await StatusSpinner.RunAsync("Saving steps...", () => _pipelines.UpsertStepsAsync(pipeline.Id, steps));
    }

    private void RenderPipelinesTable(List<Pipeline> pipelines)
    {
        if (pipelines.Count == 0)
        {
            AnsiConsole.Write(Panels.Info("No pipelines configured. Create one to chain agents!"));
            AnsiConsole.WriteLine();
            return;
        }

        var table = Tables.Create("Id", "Name", "Description");
        foreach (var pipeline in pipelines)
        {
            var isActive = _activeSession?.ActivePipelineId == pipeline.Id;
            var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
            table.AddRow(
                $"{marker}{pipeline.Id}",
                $"[{(isActive ? Theme.Success : Theme.Primary).ToMarkup()}]{Markup.Escape(pipeline.Name)}[/]",
                $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(pipeline.Description ?? "")}[/]"
            );
        }

        AnsiConsole.Write(Panels.Create(table, $"{Icons.Pipeline} Pipelines"));
        AnsiConsole.WriteLine();
    }

    private void RenderPipelineDetail(Pipeline pipeline, List<PipelineStep> steps)
    {
        var agents = _agents.ListAsync().GetAwaiter().GetResult();
        var agentMap = agents.ToDictionary(a => a.Id, a => a.Name);

        var table = Tables.Create("Order", "Agent", "Handoff");
        foreach (var step in steps.OrderBy(s => s.OrderIndex))
        {
            agentMap.TryGetValue(step.AgentId, out var name);
            table.AddRow(
                $"[{Theme.Accent.ToMarkup()}]{step.OrderIndex + 1}[/]",
                $"[{Theme.Primary.ToMarkup()}]{Markup.Escape(name ?? $"#{step.AgentId}")}[/]",
                $"[{Theme.Muted.ToMarkup()}]{Markup.Escape(step.HandoffMode)}[/]"
            );
        }

        AnsiConsole.Write(Panels.Create(table, $"{Icons.Pipeline} {pipeline.Name}"));
        if (!string.IsNullOrWhiteSpace(pipeline.Description))
        {
            AnsiConsole.Write(Panels.Info(pipeline.Description));
        }
        AnsiConsole.WriteLine();
    }

    private List<MenuChoice> BuildPipelineChoices(List<Pipeline> pipelines)
    {
        var choices = new List<MenuChoice>
        {
            new($"[{Theme.Success.ToMarkup()}]{Icons.Add} Create new pipeline[/]", null, true, false)
        };

        choices.AddRange(pipelines.Select(p =>
        {
            var isActive = _activeSession?.ActivePipelineId == p.Id;
            var marker = isActive ? $"[{Theme.Success.ToMarkup()}]{Icons.Check}[/] " : "";
            return new MenuChoice($"{marker}[{Theme.Primary.ToMarkup()}]{Markup.Escape(p.Name)}[/]  [{Theme.Muted.ToMarkup()}]{Markup.Escape(p.Description ?? "")}[/]", p.Id, false, false);
        }));

        choices.Add(new MenuChoice($"[{Theme.Muted.ToMarkup()}]{Icons.Back} Back[/]", null, false, true));
        return choices;
    }
}
