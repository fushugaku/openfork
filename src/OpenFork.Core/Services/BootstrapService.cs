using OpenFork.Core.Abstractions;
using OpenFork.Core.Config;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

public class BootstrapService
{
    private readonly AppSettings _settings;
    private readonly IAgentRepository _agents;
    private readonly IPipelineRepository _pipelines;

    public BootstrapService(AppSettings settings, IAgentRepository agents, IPipelineRepository pipelines)
    {
        _settings = settings;
        _agents = agents;
        _pipelines = pipelines;
    }

    public async Task InitializeAsync()
    {
        foreach (var agentConfig in _settings.Agents)
        {
            var existing = await _agents.GetByNameAsync(agentConfig.Name);
            var profile = existing ?? new AgentProfile();
            profile.Name = agentConfig.Name;
            profile.SystemPrompt = agentConfig.SystemPrompt;
            profile.ProviderKey = agentConfig.ProviderKey ?? _settings.DefaultProviderKey ?? _settings.OpenAiCompatible.Keys.FirstOrDefault() ?? "";
            profile.Model = agentConfig.Model ?? _settings.DefaultModel ?? "";
            profile.MaxIterations = agentConfig.MaxIterations;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            if (profile.CreatedAt == default)
            {
                profile.CreatedAt = profile.UpdatedAt;
            }

            await _agents.UpsertAsync(profile);
        }

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
}
