using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class DashboardDatabaseInitializer(
    SqliteConnectionFactory connectionFactory,
    SqliteSpeedTestStore store,
    IOptions<StorageOptions> options,
    ILogger<DashboardDatabaseInitializer> logger)
{
    private const string MigrationResourcePrefix =
        "SpeedtestDashboard.Infrastructure.Persistence.Migrations.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(options.Value.DatabasePath)
            ?? throw new InvalidOperationException("The storage database directory is invalid.");
        Directory.CreateDirectory(directory);

        cancellationToken.ThrowIfCancellationRequested();
        var result = DbUp.DeployChanges.To
            .SqliteDatabase(PersistenceServiceCollectionExtensions.BuildConnectionString(options.Value))
            .WithScriptsEmbeddedInAssembly(
                typeof(DashboardDatabaseInitializer).Assembly,
                name => name.StartsWith(MigrationResourcePrefix, StringComparison.Ordinal))
            .WithTransaction()
            .WithExecutionTimeout(TimeSpan.FromSeconds(options.Value.CommandTimeoutSeconds))
            .LogTo(logger)
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
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
}
