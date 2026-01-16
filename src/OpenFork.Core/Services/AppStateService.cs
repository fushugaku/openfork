using OpenFork.Core.Abstractions;

namespace OpenFork.Core.Services;

public class AppStateService
{
    private readonly IAppStateRepository _state;

    public AppStateService(IAppStateRepository state)
    {
        _state = state;
    }

    public Task<long?> GetLastProjectIdAsync() => _state.GetLongAsync("last_project_id");

    public Task SetLastProjectIdAsync(long projectId) => _state.SetLongAsync("last_project_id", projectId);
}
