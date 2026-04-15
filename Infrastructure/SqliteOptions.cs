namespace FlowTracker.Infrastructure;

public sealed class SqliteOptions(string databasePath)
{
    public string DatabasePath { get; } = databasePath;
}
