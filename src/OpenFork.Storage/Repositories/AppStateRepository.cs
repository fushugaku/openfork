using Dapper;
using OpenFork.Core.Abstractions;

namespace OpenFork.Storage.Repositories;

public class AppStateRepository : IAppStateRepository
{
    private readonly SqliteConnectionFactory _factory;

    public AppStateRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<long?> GetLongAsync(string key)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var value = await connection.QuerySingleOrDefaultAsync<string?>(
            "select value from app_state where key=@Key",
            new { Key = key });
        if (long.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public async Task SetLongAsync(string key, long value)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "insert into app_state (key, value) values (@Key, @Value) on conflict(key) do update set value=@Value",
            new { Key = key, Value = value.ToString() });
    }
}
