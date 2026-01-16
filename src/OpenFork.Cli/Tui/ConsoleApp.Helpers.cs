using OpenFork.Core.Domain;
using OpenFork.Search.Models;
using OpenFork.Search.Services;
using Spectre.Console;

namespace OpenFork.Cli.Tui;

public partial class ConsoleApp
{
  private IndexStatus? _cachedIndexStatus;
  private volatile bool _isIndexing;
  private CancellationTokenSource? _indexingCts;

  private async Task LoadLastProjectAsync()
  {
    if (_activeProject != null) return;

    var id = await _appState.GetLastProjectIdAsync();
    if (id.HasValue)
    {
      var project = await _projects.GetAsync(id.Value);
      if (project != null)
      {
        _activeProject = project;
        await RefreshIndexStatusAsync();
        StartBackgroundIndexing(project);
      }
    }
  }

  private void StartBackgroundIndexing(Project project)
  {
    if (!_settings.Search.EnableSemanticSearch)
      return;

    if (!Directory.Exists(project.RootPath))
      return;

    _indexingCts?.Cancel();
    _indexingCts = new CancellationTokenSource();
    var token = _indexingCts.Token;

    _isIndexing = true;

    _ = Task.Run(async () =>
    {
      try
      {
        await _indexService.IndexProjectAsync(project.Id, project.RootPath, null, token);
        if (!token.IsCancellationRequested && _activeProject?.Id == project.Id)
        {
          _cachedIndexStatus = await _indexService.GetIndexStatusAsync(project.Id, token);
        }
      }
      catch
      {
      }
      finally
      {
        _isIndexing = false;
      }
    }, token);
  }

  private async Task RefreshIndexStatusAsync()
  {
    if (_activeProject == null || !_settings.Search.EnableSemanticSearch)
    {
      _cachedIndexStatus = null;
      return;
    }

    try
    {
      _cachedIndexStatus = await _indexService.GetIndexStatusAsync(_activeProject.Id);
    }
    catch
    {
      _cachedIndexStatus = null;
    }
  }

  private string GetIndexStatusText()
  {
    if (!_settings.Search.EnableSemanticSearch)
      return $"[{Theme.Muted.ToMarkup()}]disabled[/]";

    if (_isIndexing)
      return $"[{Theme.Accent.ToMarkup()}]indexing...[/]";

    if (_cachedIndexStatus == null)
      return $"[{Theme.Muted.ToMarkup()}]—[/]";

    if (!_cachedIndexStatus.IsAvailable)
      return $"[{Theme.Error.ToMarkup()}]unavailable[/]";

    if (!_cachedIndexStatus.Exists)
      return $"[{Theme.Warning.ToMarkup()}]not indexed[/]";

    return $"[{Theme.Success.ToMarkup()}]{_cachedIndexStatus.TotalFiles} files, {_cachedIndexStatus.TotalChunks} chunks[/]";
  }

  private string SelectProviderKey()
  {
    var keys = _settings.OpenAiCompatible.Keys.ToList();
    if (keys.Count == 0) return _settings.DefaultProviderKey ?? "";

    return AnsiConsole.Prompt(
        Prompts.Selection<string>("Provider")
            .AddChoices(keys));
  }

  private string SelectModel(string providerKey)
  {
    if (_settings.OpenAiCompatible.TryGetValue(providerKey, out var provider) && provider.AvailableModels.Count > 0)
    {
      var choices = provider.AvailableModels.Select(m => m.Name).ToList();
      return AnsiConsole.Prompt(
          Prompts.Selection<string>("Model")
              .AddChoices(choices));
    }

    return _settings.DefaultModel ?? "";
  }

  private string SelectDirectory(string startDirectory)
  {
    var browser = new Browser
    {
      ActualFolder = startDirectory,
      SelectedFile = "",
      PageSize = 16,
      DisplayIcons = true,
      CanCreateFolder = true,
      SelectFolderText = "Select Project Root",
      SelectActualText = $"{Icons.Check} Use this folder"
    };

    return browser.GetFolderPath(startDirectory).GetAwaiter().GetResult();
  }

  private void RenderHeader()
  {
    var title = new FigletText("OpenFork")
        .Color(Theme.Primary);
    AnsiConsole.Write(title);
    AnsiConsole.Write(new Rule($"[{Theme.Muted.ToMarkup()}]AI Agent Manager[/]").RuleStyle(Theme.MutedStyle));
    AnsiConsole.WriteLine();
  }

  private void RenderContext()
  {
    var project = _activeProject?.Name ?? "None";
    var session = _activeSession?.Name ?? "None";
    var agent = _activeSession?.ActiveAgentId?.ToString() ?? "—";
    var pipeline = _activeSession?.ActivePipelineId?.ToString() ?? "—";
    var indexStatus = GetIndexStatusText();

    var table = Tables.Create("Project", "Session", "Agent", "Pipeline", "Index");
    table.AddRow(
        $"[{Theme.Primary.ToMarkup()}]{Markup.Escape(project)}[/]",
        $"[{Theme.Secondary.ToMarkup()}]{Markup.Escape(session)}[/]",
        agent != "—" ? $"[{Theme.Success.ToMarkup()}]#{agent}[/]" : $"[{Theme.Muted.ToMarkup()}]{agent}[/]",
        pipeline != "—" ? $"[{Theme.Success.ToMarkup()}]#{pipeline}[/]" : $"[{Theme.Muted.ToMarkup()}]{pipeline}[/]",
        indexStatus
    );

    AnsiConsole.Write(Panels.Create(table, "Context"));
    AnsiConsole.WriteLine();
  }

  private void Pause()
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Prompt(
        new TextPrompt<string>($"[{Theme.Muted.ToMarkup()}]Press Enter to continue[/]")
            .AllowEmpty());
  }

  private record MenuChoice(string Label, long? Id, bool IsCreate, bool IsBack);
}
