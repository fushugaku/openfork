using OpenFork.Core.Domain;
using OpenFork.Core.Config;
using OpenFork.Core.Services;
using OpenFork.Core.Tools;
using OpenFork.Search.Services;
using OpenFork.Search.Tools;
using Spectre.Console;

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

  private Project? _activeProject;
  private Session? _activeSession;
  private bool _stateLoaded;

  public ConsoleApp(
      ProjectService projects,
      SessionService sessions,
      AgentService agents,
      PipelineService pipelines,
      ChatService chat,
      AppSettings settings,
      AppStateService appState,
      ProjectIndexService indexService,
      ToolRegistry toolRegistry)
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

    _toolRegistry.Register(new SearchProjectTool(
        indexService,
        () => _activeProject?.Id,
        () => _activeProject?.RootPath));
  }

  public async Task RunAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      if (!_stateLoaded)
      {
        await StatusSpinner.RunAsync("Loading state...", LoadLastProjectAsync);
        _stateLoaded = true;
      }

      AnsiConsole.Clear();
      RenderHeader();
      RenderContext();

      var choice = AnsiConsole.Prompt(
          Prompts.Selection<string>("Main Menu")
              .AddChoices(
                  $"{Icons.Project} Projects",
                  $"{Icons.Session} Sessions",
                  $"{Icons.Agent} Agents",
                  $"{Icons.Pipeline} Pipelines",
                  $"{Icons.Chat} Chat",
                  $"{Icons.Back} Exit"));

      switch (choice)
      {
        case var c when c.Contains("Projects"):
          await ProjectsScreenAsync(cancellationToken);
          break;
        case var c when c.Contains("Sessions"):
          await SessionsScreenAsync(cancellationToken);
          break;
        case var c when c.Contains("Agents"):
          await AgentsScreenAsync(cancellationToken);
          break;
        case var c when c.Contains("Pipelines"):
          await PipelinesScreenAsync(cancellationToken);
          break;
        case var c when c.Contains("Chat"):
          await ChatScreenAsync(cancellationToken);
          break;
        default:
          return;
      }
    }
  }
}
