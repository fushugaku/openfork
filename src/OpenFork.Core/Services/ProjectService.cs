using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Core.Services;

public class ProjectService
{
    private readonly IProjectRepository _projects;

    public ProjectService(IProjectRepository projects)
    {
        _projects = projects;
    }

    public Task<List<Project>> ListAsync() => _projects.ListAsync();

    public Task<Project?> GetAsync(long id) => _projects.GetAsync(id);

    public Task<Project> UpsertAsync(Project project) => _projects.UpsertAsync(project);
}
