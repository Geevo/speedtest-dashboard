using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class DashboardDatabaseInitializer(
    SqliteConnectionFactory connectionFactory,
    SqliteSpeedTestStore store,
    IOptions<StorageOptions> options,
    ILogger<DashboardDatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(options.Value.DatabasePath)
            ?? throw new InvalidOperationException("The storage database directory is invalid.");
        Directory.CreateDirectory(directory);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var schemaVersion = await GetSchemaVersionAsync(connection, cancellationToken);
        if (schemaVersion == 0 && await HasApplicationTablesAsync(connection, cancellationToken))
        {
            throw new InvalidOperationException(
                "The database uses the retired EF Core schema. This NativeAOT build requires a fresh database.");
        }
        if (schemaVersion > 1)
        {
            throw new InvalidOperationException($"The database schema version {schemaVersion} is newer than this application supports.");
        }

        await using (var schema = connectionFactory.CreateCommand(connection, DashboardSchema.Sql))
        {
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connectionFactory.CreateCommand(connection, "PRAGMA journal_mode=WAL;");
        var journalMode = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (!IsWalEnabled(journalMode))
        {
            logger.LogWarning(
                "SQLite could not enable WAL journal mode; continuing with journal mode {JournalMode}.",
                string.IsNullOrWhiteSpace(journalMode) ? "unknown" : journalMode);
        }
        else
        {
            logger.LogInformation("SQLite journal mode is WAL.");
        }

        await store.ReconcileInterruptedJobsAsync(cancellationToken);
    }

    internal static bool IsWalEnabled(string? journalMode) =>
        string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase);

    private async Task<long> GetSchemaVersionAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, "PRAGMA user_version;");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> HasApplicationTablesAsync(Microsoft.Data.Sqlite.SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connectionFactory.CreateCommand(connection, """
            SELECT EXISTS(
                SELECT 1 FROM sqlite_master
                WHERE type = 'table' AND name IN ('SpeedTestJobs', 'SpeedTestResults', 'Users', 'DashboardSettings'));
            """);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }
}
