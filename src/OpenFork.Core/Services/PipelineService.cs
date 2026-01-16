using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

public class PipelineService
{
    private readonly IPipelineRepository _pipelines;

    public PipelineService(IPipelineRepository pipelines)
    {
        _pipelines = pipelines;
    }

    public Task<List<Pipeline>> ListAsync() => _pipelines.ListAsync();

    public Task<Pipeline?> GetAsync(long id) => _pipelines.GetAsync(id);

    public Task<Pipeline?> GetByNameAsync(string name) => _pipelines.GetByNameAsync(name);

    public Task<Pipeline> UpsertAsync(Pipeline pipeline) => _pipelines.UpsertAsync(pipeline);

    public Task<List<PipelineStep>> ListStepsAsync(long pipelineId) => _pipelines.ListStepsAsync(pipelineId);

    public Task UpsertStepsAsync(long pipelineId, List<PipelineStep> steps) => _pipelines.UpsertStepsAsync(pipelineId, steps);

    public Task DeleteAsync(long id) => _pipelines.DeleteAsync(id);
}
