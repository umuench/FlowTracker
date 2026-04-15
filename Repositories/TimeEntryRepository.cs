using Dapper;
using FlowTracker.Domain;
using FlowTracker.Infrastructure;

namespace FlowTracker.Repositories;

public sealed class TimeEntryRepository(SqliteConnectionFactory connectionFactory) : ITimeEntryRepository
{
    private readonly SqliteConnectionFactory _connectionFactory = connectionFactory;

    private sealed class TimeEntryRow
    {
        public long Id { get; init; }
        public string UserId { get; init; } = string.Empty;
        public string StartTime { get; init; } = string.Empty;
        public string? EndTime { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string CreatedAt { get; init; } = string.Empty;
        public bool IsDeleted { get; init; }
    }

    public async Task<TimeEntry> StartTrackingAsync(string userId, string category, string description, DateTimeOffset startTimeUtc, CancellationToken cancellationToken = default)
    {
        await StopTrackingAsync(userId, startTimeUtc, cancellationToken).ConfigureAwait(false);

        await using var connection = _connectionFactory.CreateOpenConnection();

        const string insertSql = """
            INSERT INTO TimeEntries (UserId, StartTime, EndTime, Category, Description, CreatedAt, IsDeleted)
            VALUES (@UserId, @StartTime, NULL, @Category, @Description, @CreatedAt, 0);
            SELECT last_insert_rowid();
            """;

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            insertSql,
            new
            {
                UserId = userId,
                StartTime = SerializeUtc(startTimeUtc),
                Category = category,
                Description = description,
                CreatedAt = SerializeUtc(DateTimeOffset.UtcNow)
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new TimeEntry
        {
            Id = id,
            UserId = userId,
            StartTime = startTimeUtc,
            EndTime = null,
            Category = category,
            Description = description,
            CreatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false
        };
    }

    public async Task StopTrackingAsync(string userId, DateTimeOffset endTimeUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateOpenConnection();

        const string updateSql = """
            UPDATE TimeEntries
            SET EndTime = @EndTime
            WHERE UserId = @UserId
              AND EndTime IS NULL
              AND IsDeleted = 0;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            updateSql,
            new { UserId = userId, EndTime = SerializeUtc(endTimeUtc) },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TimeEntry>> GetEntriesAsync(string userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateOpenConnection();

        var sql = includeDeleted
            ? """
              SELECT Id, UserId, StartTime, EndTime, Category, Description, CreatedAt, IsDeleted
              FROM TimeEntries
              WHERE UserId = @UserId
                AND StartTime >= @FromUtc
                AND StartTime < @ToUtc
              ORDER BY StartTime ASC;
              """
            : """
              SELECT Id, UserId, StartTime, EndTime, Category, Description, CreatedAt, IsDeleted
              FROM TimeEntries
              WHERE UserId = @UserId
                AND IsDeleted = 0
                AND StartTime >= @FromUtc
                AND StartTime < @ToUtc
              ORDER BY StartTime ASC;
              """;

        var rows = await connection.QueryAsync<TimeEntryRow>(new CommandDefinition(
            sql,
            new
            {
                UserId = userId,
                FromUtc = SerializeUtc(fromUtc),
                ToUtc = SerializeUtc(toUtc)
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(static row => new TimeEntry
        {
            Id = row.Id,
            UserId = row.UserId,
            StartTime = ParseUtc(row.StartTime),
            EndTime = ParseNullableUtc(row.EndTime),
            Category = row.Category,
            Description = row.Description,
            CreatedAt = ParseUtc(row.CreatedAt),
            IsDeleted = row.IsDeleted
        }).ToList();
    }

    public async Task UpdateEntryAsync(TimeEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateOpenConnection();

        const string sql = """
            UPDATE TimeEntries
            SET StartTime = @StartTime,
                EndTime = @EndTime,
                Category = @Category,
                Description = @Description
            WHERE Id = @Id
              AND UserId = @UserId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entry.Id,
                entry.UserId,
                StartTime = SerializeUtc(entry.StartTime),
                EndTime = entry.EndTime is null ? null : SerializeUtc(entry.EndTime.Value),
                entry.Category,
                entry.Description
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task DeleteEntryAsync(long id, string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateOpenConnection();

        const string sql = """
            UPDATE TimeEntries
            SET IsDeleted = 1
            WHERE Id = @Id
              AND UserId = @UserId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { Id = id, UserId = userId },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static string SerializeUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private static DateTimeOffset ParseUtc(string value)
    {
        if (DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        var parsedDateTime = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        return new DateTimeOffset(DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Utc));
    }

    private static DateTimeOffset? ParseNullableUtc(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseUtc(value);
}
