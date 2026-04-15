using Dapper;
using System.IO;

namespace FlowTracker.Infrastructure;

public sealed class DatabaseInitializer(SqliteConnectionFactory connectionFactory)
{
    private readonly SqliteConnectionFactory _connectionFactory = connectionFactory;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_connectionFactory.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = _connectionFactory.CreateOpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        const string schemaSql = """
            CREATE TABLE IF NOT EXISTS TimeEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NULL,
                Category TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_TimeEntries_UserId_StartTime
                ON TimeEntries (UserId, StartTime);

            CREATE INDEX IF NOT EXISTS IX_TimeEntries_UserId_EndTime
                ON TimeEntries (UserId, EndTime);

            CREATE TABLE IF NOT EXISTS WorkSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                SessionDate TEXT NOT NULL,
                WorkStart TEXT NULL,
                WorkEnd TEXT NULL,
                State TEXT NOT NULL,
                Version INTEGER NOT NULL DEFAULT 1
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_WorkSessions_UserId_SessionDate
                ON WorkSessions (UserId, SessionDate);

            CREATE TABLE IF NOT EXISTS SessionEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId INTEGER NULL,
                UserId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                ReasonCode TEXT NULL,
                EventTime TEXT NOT NULL,
                MetadataJson TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_SessionEvents_UserId_EventTime
                ON SessionEvents (UserId, EventTime);

            CREATE TABLE IF NOT EXISTS BreakPolicies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DurationMinutes INTEGER NOT NULL,
                TargetWorkMinutes INTEGER NOT NULL DEFAULT 480,
                EarliestStart TEXT NULL,
                LatestStart TEXT NULL,
                AutoApply INTEGER NOT NULL DEFAULT 0,
                IsPaid INTEGER NOT NULL DEFAULT 0,
                RequiresReason INTEGER NOT NULL DEFAULT 0
            );

            INSERT INTO BreakPolicies (Name, DurationMinutes, EarliestStart, LatestStart, AutoApply, IsPaid, RequiresReason)
            SELECT 'Mittag Standard', 60, '11:00', '15:00', 0, 0, 1
            WHERE NOT EXISTS (SELECT 1 FROM BreakPolicies);
            """;

        await connection.ExecuteAsync(new CommandDefinition(schemaSql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "ALTER TABLE BreakPolicies ADD COLUMN TargetWorkMinutes INTEGER NOT NULL DEFAULT 480;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
        catch
        {
            // Spalte existiert bereits oder wird von älteren SQLite-Versionen nicht erneut angelegt.
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE BreakPolicies SET TargetWorkMinutes = COALESCE(TargetWorkMinutes, 480) WHERE TargetWorkMinutes IS NULL OR TargetWorkMinutes <= 0;",
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
