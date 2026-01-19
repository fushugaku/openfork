using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using OpenFork.Core.Config;

namespace OpenFork.Storage;

public class SqliteConnectionFactory
{
    private readonly AppSettings _settings;
    private static int _typeHandlersRegistered;
    private static int _pragmasConfigured;

    public SqliteConnectionFactory(AppSettings settings)
    {
        _settings = settings;
        RegisterTypeHandlers();
    }

    public SqliteConnection Create()
    {
        var path = _settings.DatabasePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Clean up any leftover WAL files on first access
        if (Interlocked.CompareExchange(ref _pragmasConfigured, 1, 0) == 0)
        {
            CleanupWalFiles(path);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        return connection;
    }

    /// <summary>
    /// Cleans up any leftover WAL files from previous sessions.
    /// </summary>
    private static void CleanupWalFiles(string dbPath)
    {
        try
        {
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";

            // Only try to delete if the main db exists (WAL files without main db are orphaned)
            if (File.Exists(dbPath))
            {
                // First, open connection and force checkpoint to merge WAL into main db
                var tempConnStr = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
                using (var tempConn = new SqliteConnection(tempConnStr))
                {
                    tempConn.Open();
                    try
                    {
                        // Try to checkpoint and switch to DELETE mode
                        tempConn.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                        tempConn.Execute("PRAGMA journal_mode = DELETE;");
                    }
                    catch
                    {
                        // Ignore errors - might not be in WAL mode
                    }
                    tempConn.Close();
                }
            }

            // Now try to delete orphaned WAL files
            if (File.Exists(walPath))
            {
                try { File.Delete(walPath); } catch { /* ignore */ }
            }
            if (File.Exists(shmPath))
            {
                try { File.Delete(shmPath); } catch { /* ignore */ }
            }
        }
        catch
        {
            // Ignore cleanup errors - not critical
        }
    }

    /// <summary>
    /// Configures SQLite pragmas for optimal performance and to prevent WAL issues.
    /// </summary>
    private static void ConfigurePragmas(SqliteConnection connection)
    {
        connection.Open();
        try
        {
            // Use DELETE journal mode instead of WAL to avoid WAL file growth issues
            // WAL is faster but can cause "no space" errors if checkpoints don't run
            connection.Execute("PRAGMA journal_mode = DELETE;");

            // Set synchronous to NORMAL for better performance (still safe for most uses)
            connection.Execute("PRAGMA synchronous = NORMAL;");

            // Enable foreign keys
            connection.Execute("PRAGMA foreign_keys = ON;");

            // Set a reasonable cache size (negative = KB, positive = pages)
            connection.Execute("PRAGMA cache_size = -2000;"); // 2MB cache

            // Set temp store to memory
            connection.Execute("PRAGMA temp_store = MEMORY;");
        }
        finally
        {
            connection.Close();
        }
    }

    /// <summary>
    /// Runs a checkpoint and vacuum to clean up the database.
    /// Call this periodically or after heavy operations.
    /// </summary>
    public void Checkpoint()
    {
        using var connection = Create();
        connection.Open();

        // Force a checkpoint if WAL mode is somehow enabled
        try
        {
            connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch
        {
            // Ignore if not in WAL mode
        }
    }

    /// <summary>
    /// Vacuums the database to reclaim space and rebuild the file.
    /// </summary>
    public void Vacuum()
    {
        using var connection = Create();
        connection.Open();
        connection.Execute("VACUUM;");
    }

    private static void RegisterTypeHandlers()
    {
        // Thread-safe check-and-set using Interlocked
        if (Interlocked.CompareExchange(ref _typeHandlersRegistered, 1, 0) == 0)
        {
            SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        }
    }
}

public class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.Value = value.ToString("o");
    }

    public override DateTimeOffset Parse(object value)
    {
        return value switch
        {
            string s => DateTimeOffset.Parse(s),
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            _ => default
        };
    }
}
