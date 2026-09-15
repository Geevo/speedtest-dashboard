using Microsoft.Data.Sqlite;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed class ApiIdempotencyStore(
    SqliteConnectionFactory connectionFactory,
    TimeProvider timeProvider) : IApiIdempotencyStore
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);
    // One instance owns the database. Fixed lock stripes bound memory regardless of key count.
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var gate = _gates[(uint)key.GetHashCode(StringComparison.Ordinal) % (uint)_gates.Length];
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    public async Task<ApiIdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        var cutoff = (timeProvider.GetUtcNow() - RetentionPeriod).UtcDateTime;
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, """
            SELECT Key, RequestHash, JobId, CreatedAtUtc FROM ApiIdempotencyRecords
            WHERE Key = @key AND CreatedAtUtc >= @cutoff;
            """);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@cutoff", cutoff);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ApiIdempotencyRecord(reader.GetString(0), reader.GetString(1), reader.GetGuid(2),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)))
            : null;
    }

    public async Task SaveAsync(string key, string requestHash, Guid jobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - RetentionPeriod;

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var cleanup = connectionFactory.CreateCommand(connection,
            "DELETE FROM ApiIdempotencyRecords WHERE CreatedAtUtc < @cutoff;", transaction))
        {
            cleanup.Parameters.AddWithValue("@cutoff", cutoff);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var save = connectionFactory.CreateCommand(connection, """
            INSERT INTO ApiIdempotencyRecords (Key, RequestHash, JobId, CreatedAtUtc) VALUES (@key, @hash, @jobId, @created)
            ON CONFLICT(Key) DO UPDATE SET RequestHash = excluded.RequestHash, JobId = excluded.JobId, CreatedAtUtc = excluded.CreatedAtUtc;
            """, transaction))
        {
            save.Parameters.AddWithValue("@key", key); save.Parameters.AddWithValue("@hash", requestHash);
            save.Parameters.AddWithValue("@jobId", jobId); save.Parameters.AddWithValue("@created", now);
            await save.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
