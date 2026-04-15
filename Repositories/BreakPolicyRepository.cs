using Dapper;
using FlowTracker.Domain;
using FlowTracker.Infrastructure;

namespace FlowTracker.Repositories;

/// <summary>
/// SQLite/Dapper-Implementierung für Pausenrichtlinien.
/// </summary>
public sealed class BreakPolicyRepository(SqliteConnectionFactory connectionFactory) : IBreakPolicyRepository
{
    private readonly SqliteConnectionFactory _connectionFactory = connectionFactory;

    /// <inheritdoc />
    public async Task<BreakPolicy?> GetDefaultPolicyAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateOpenConnection();

        const string sql = """
            SELECT Id, Name, DurationMinutes, TargetWorkMinutes, EarliestStart, LatestStart, AutoApply, IsPaid, RequiresReason
            FROM BreakPolicies
            ORDER BY Id ASC
            LIMIT 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<BreakPolicy>(
            new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
