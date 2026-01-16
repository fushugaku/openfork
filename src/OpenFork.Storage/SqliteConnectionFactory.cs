using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using OpenFork.Core.Config;

namespace OpenFork.Storage;

public class SqliteConnectionFactory
{
    private readonly AppSettings _settings;
    private static bool _typeHandlersRegistered;

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

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path
        }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static void RegisterTypeHandlers()
    {
        if (_typeHandlersRegistered) return;
        
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        _typeHandlersRegistered = true;
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
