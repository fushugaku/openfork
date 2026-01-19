using Microsoft.Extensions.Logging;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Config;
using OpenFork.Core.Domain;
using OpenFork.Core.Tools;

namespace OpenFork.Core.Services;

public class BootstrapService
{
    private readonly AppSettings _settings;
    private readonly AgentService _agents;
    private readonly IPipelineRepository _pipelines;
    private readonly IAgentRegistry _agentRegistry;
    private readonly HookLoader _hookLoader;
    private readonly IToolFileLoader _toolFileLoader;
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<BootstrapService> _logger;

    /// <summary>
    /// Base directory for resolving relative paths (typically the config directory).
    /// </summary>
    public string? ConfigDirectory { get; set; }

    public BootstrapService(
        AppSettings settings,
        AgentService agents,
        IPipelineRepository pipelines,
        IAgentRegistry agentRegistry,
        HookLoader hookLoader,
        IToolFileLoader toolFileLoader,
        ToolRegistry toolRegistry,
        ILogger<BootstrapService> logger)
    {
        _settings = settings;
        _agents = agents;
        _pipelines = pipelines;
        _agentRegistry = agentRegistry;
        _hookLoader = hookLoader;
        _toolFileLoader = toolFileLoader;
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // Initialize the agent registry with custom agents from storage
        await _agentRegistry.InitializeAsync();

        // Load hooks from configuration
        await _hookLoader.LoadHooksAsync();

        // Load pipeline tools from *.tool.json files
        await LoadPipelineToolsAsync();

        // Agents are now loaded directly from settings via AgentService
        // No need to sync to database

        // Sync pipelines from settings
        foreach (var pipelineConfig in _settings.Pipelines)
        {
            var existing = await _pipelines.GetByNameAsync(pipelineConfig.Name);
            var pipeline = existing ?? new Pipeline();
            pipeline.Name = pipelineConfig.Name;
            pipeline.Description = pipelineConfig.Description;
            pipeline.UpdatedAt = DateTimeOffset.UtcNow;
            if (pipeline.CreatedAt == default)
            {
                pipeline.CreatedAt = pipeline.UpdatedAt;
            }

            pipeline = await _pipelines.UpsertAsync(pipeline);
            var steps = new List<PipelineStep>();
            var index = 0;
            foreach (var step in pipelineConfig.Steps)
            {
                var agent = await _agents.GetByNameAsync(step.Agent);
                if (agent == null)
                {
                    continue;
                }

                steps.Add(new PipelineStep
                {
                    PipelineId = pipeline.Id,
                    OrderIndex = index++,
                    AgentId = agent.Id,
                    HandoffMode = step.HandoffMode
                });
            }

            await _pipelines.UpsertStepsAsync(pipeline.Id, steps);
        }
    }

    private async Task LoadPipelineToolsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.ToolsDirectory))
        {
            _logger.LogDebug("No ToolsDirectory configured, skipping pipeline tool loading");
            return;
        }

        // Resolve the tools directory path
        var toolsDir = _settings.ToolsDirectory;

        // If relative path, resolve from config directory
        if (!Path.IsPathRooted(toolsDir))
        {
            var baseDir = ConfigDirectory ?? Environment.CurrentDirectory;
            toolsDir = Path.GetFullPath(Path.Combine(baseDir, toolsDir));
        }

        _logger.LogInformation("Loading pipeline tools from: {ToolsDirectory}", toolsDir);

        try
        {
            var tools = await _toolFileLoader.LoadToolsAsync(toolsDir);

            foreach (var tool in tools)
            {
                _toolRegistry.Register(tool);
                _logger.LogInformation("Registered pipeline tool: {ToolName}", tool.Name);
            }

            if (tools.Count > 0)
            {
                _logger.LogInformation("Loaded {Count} pipeline tools", tools.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pipeline tools from {ToolsDirectory}", toolsDir);
        }
    }
}
