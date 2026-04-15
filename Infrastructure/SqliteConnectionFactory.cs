using Microsoft.Data.Sqlite;

namespace FlowTracker.Infrastructure;

public sealed class SqliteConnectionFactory(SqliteOptions options)
{
    private readonly SqliteOptions _options = options;
    public string DatabasePath => _options.DatabasePath;

    public SqliteConnection CreateOpenConnection()
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
        connection.Open();
        return connection;
    }
}
