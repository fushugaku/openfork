using Dapper;

namespace OpenFork.Storage;

public class SchemaInitializer
{
  private readonly SqliteConnectionFactory _factory;

  public SchemaInitializer(SqliteConnectionFactory factory)
  {
    _factory = factory;
  }

  public async Task InitializeAsync()
  {
    using var connection = _factory.Create();
    await connection.OpenAsync();
    await connection.ExecuteAsync("""
            create table if not exists projects (
                id integer primary key autoincrement,
                name text not null,
                root_path text not null,
                created_at text not null,
                updated_at text not null
            );
            create table if not exists sessions (
                id integer primary key autoincrement,
                project_id integer not null,
                name text not null,
                active_agent_id integer null,
                active_pipeline_id integer null,
                created_at text not null,
                updated_at text not null
            );
            create table if not exists messages (
                id integer primary key autoincrement,
                session_id integer not null,
                agent_id integer null,
                pipeline_step_id integer null,
                role text not null,
                content text not null,
                tool_calls_json text null,
                created_at text not null
            );
            create table if not exists agents (
                id integer primary key autoincrement,
                name text not null,
                system_prompt text not null,
                provider_key text not null,
                model text not null,
                max_iterations integer not null default 0,
                created_at text not null,
                updated_at text not null
            );
            create table if not exists pipelines (
                id integer primary key autoincrement,
                name text not null,
                description text null,
                created_at text not null,
                updated_at text not null
            );
            create table if not exists pipeline_steps (
                id integer primary key autoincrement,
                pipeline_id integer not null,
                order_index integer not null,
                agent_id integer not null,
                handoff_mode text not null
            );
            create table if not exists app_state (
                key text primary key,
                value text not null
            );
            """);

    // Migrations for existing databases
    await RunMigrationsAsync(connection);
  }

  private async Task RunMigrationsAsync(Microsoft.Data.Sqlite.SqliteConnection connection)
  {
    // Add max_iterations column if it doesn't exist
    var columns = await connection.QueryAsync<string>(
        "SELECT name FROM pragma_table_info('agents')");

    if (!columns.Contains("max_iterations"))
    {
      await connection.ExecuteAsync(
          "ALTER TABLE agents ADD COLUMN max_iterations integer not null default 0");
    }
  }
}
