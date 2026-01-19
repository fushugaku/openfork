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
    var agentColumns = await connection.QueryAsync<string>(
        "SELECT name FROM pragma_table_info('agents')");

    if (!agentColumns.Contains("max_iterations"))
    {
      await connection.ExecuteAsync(
          "ALTER TABLE agents ADD COLUMN max_iterations integer not null default 0");
    }

    // Add is_compacted column to messages if it doesn't exist
    var messageColumns = await connection.QueryAsync<string>(
        "SELECT name FROM pragma_table_info('messages')");

    if (!messageColumns.Contains("is_compacted"))
    {
      await connection.ExecuteAsync(
          "ALTER TABLE messages ADD COLUMN is_compacted integer not null default 0");
    }

    // Create message_parts table if it doesn't exist
    await connection.ExecuteAsync("""
        create table if not exists message_parts (
            id text primary key,
            session_id integer not null,
            message_id integer not null,
            order_index integer not null,
            type text not null,
            data_json text not null,
            created_at text not null,
            updated_at text null
        );
        create index if not exists idx_message_parts_message_id on message_parts(message_id);
        create index if not exists idx_message_parts_session_id on message_parts(session_id);
        """);

    // Create custom_agents table if it doesn't exist
    await connection.ExecuteAsync("""
        create table if not exists custom_agents (
            id text primary key,
            name text not null,
            slug text not null unique,
            description text,
            category text not null default 'Primary',
            provider_id text,
            model_id text,
            temperature real default 0.7,
            max_tokens integer,
            system_prompt text not null,
            prompt_variables_json text,
            use_default_system_prefix integer default 1,
            execution_mode text default 'Agentic',
            max_iterations integer default 30,
            timeout_seconds real,
            can_spawn_subagents integer default 0,
            allowed_subagent_types_json text,
            tools_config_json text,
            permissions_json text,
            icon_emoji text,
            color text,
            display_order integer default 0,
            is_visible integer default 1,
            created_at text not null,
            updated_at text
        );
        create index if not exists idx_custom_agents_slug on custom_agents(slug);
        create index if not exists idx_custom_agents_category on custom_agents(category);
        """);

    // Create subsessions table if it doesn't exist
    await connection.ExecuteAsync("""
        create table if not exists subsessions (
            id text primary key,
            parent_session_id integer not null,
            parent_message_id integer,
            agent_slug text not null default 'general',
            status text not null default 'Pending',
            prompt text,
            description text,
            result text,
            error text,
            max_iterations integer default 10,
            iterations_used integer default 0,
            effective_permissions_json text,
            created_at text not null,
            completed_at text
        );
        create index if not exists idx_subsessions_parent on subsessions(parent_session_id);
        create index if not exists idx_subsessions_status on subsessions(status);
        """);
  }
}
