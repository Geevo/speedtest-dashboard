using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class DashboardDatabaseInitializer(
    IDbContextFactory<DashboardDbContext> contextFactory,
    SqliteSpeedTestStore store,
    IOptions<StorageOptions> options,
    ILogger<DashboardDatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(options.Value.DatabasePath)
            ?? throw new InvalidOperationException("The storage database directory is invalid.");
        Directory.CreateDirectory(directory);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
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
