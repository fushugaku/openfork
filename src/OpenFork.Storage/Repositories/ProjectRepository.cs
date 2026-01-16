using Dapper;
using OpenFork.Core.Abstractions;
using OpenFork.Core.Domain;

namespace OpenFork.Storage.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ProjectRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    private const string SelectSql = """
        select id, name, root_path as RootPath, created_at as CreatedAt, updated_at as UpdatedAt
        from projects
        """;

    public async Task<List<Project>> ListAsync()
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        var result = await connection.QueryAsync<Project>($"{SelectSql} order by updated_at desc");
        return result.ToList();
    }

    public async Task<Project?> GetAsync(long id)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();
        return await connection.QuerySingleOrDefaultAsync<Project>($"{SelectSql} where id = @Id", new { Id = id });
    }

    public async Task<Project> UpsertAsync(Project project)
    {
        using var connection = _factory.Create();
        await connection.OpenAsync();

        if (project.Id == 0)
        {
            project.CreatedAt = project.CreatedAt == default ? DateTimeOffset.UtcNow : project.CreatedAt;
            project.UpdatedAt = DateTimeOffset.UtcNow;
            var id = await connection.ExecuteScalarAsync<long>(
                "insert into projects (name, root_path, created_at, updated_at) values (@Name, @RootPath, @CreatedAt, @UpdatedAt); select last_insert_rowid();",
                project);
            project.Id = id;
            return project;
        }

        project.UpdatedAt = DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(
            "update projects set name=@Name, root_path=@RootPath, updated_at=@UpdatedAt where id=@Id",
            project);
        return project;
    }
}
