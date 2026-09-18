using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class DashboardDatabaseInitializer(
    SqliteConnectionFactory connectionFactory,
    SqliteSpeedTestStore store,
    IOptions<StorageOptions> options,
    TimeProvider timeProvider,
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
        var upgrader = DbUp.DeployChanges.To
            .SqliteDatabase(PersistenceServiceCollectionExtensions.BuildConnectionString(options.Value))
            .WithScriptsEmbeddedInAssembly(
                typeof(DashboardDatabaseInitializer).Assembly,
                name => name.StartsWith(MigrationResourcePrefix, StringComparison.Ordinal))
            .WithTransaction()
            .WithExecutionTimeout(TimeSpan.FromSeconds(options.Value.CommandTimeoutSeconds))
            .LogTo(logger)
            .Build();

        if (upgrader.IsUpgradeRequired())
        {
            await CreatePreMigrationBackupAsync(directory, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await ValidateDatabaseAsync(connection, cancellationToken);

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

    private async Task CreatePreMigrationBackupAsync(string databaseDirectory, CancellationToken cancellationToken)
    {
        if (!options.Value.CreateMigrationBackups ||
            !await ContainsApplicationSchemaAsync(cancellationToken))
        {
            return;
        }

        var backupDirectory = string.IsNullOrWhiteSpace(options.Value.MigrationBackupDirectory)
            ? Path.Combine(databaseDirectory, "backups")
            : options.Value.MigrationBackupDirectory;
        Directory.CreateDirectory(backupDirectory);

        var databaseName = Path.GetFileNameWithoutExtension(options.Value.DatabasePath);
        var timestamp = timeProvider.GetUtcNow().UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = GetAvailableBackupPath(backupDirectory, databaseName, timestamp);

        try
        {
            await using var source = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                ForeignKeys = true,
                DefaultTimeout = options.Value.CommandTimeoutSeconds
            }.ToString());
            await destination.OpenAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            source.BackupDatabase(destination);
            await ValidateDatabaseAsync(destination, cancellationToken);
        }
        catch
        {
            TryDeleteIncompleteBackup(backupPath);
            throw;
        }

        logger.LogInformation("Created pre-migration SQLite backup at {BackupPath}.", backupPath);
        PruneMigrationBackups(backupDirectory, databaseName);
    }

    private async Task<bool> ContainsApplicationSchemaAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connectionFactory.CreateCommand(connection, """
            SELECT EXISTS (
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                  AND name <> 'SchemaVersions'
            );
            """);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private static string GetAvailableBackupPath(string directory, string databaseName, string timestamp)
    {
        var prefix = $"{databaseName}.pre-migration-{timestamp}";
        var path = Path.Combine(directory, $"{prefix}.db");
        for (var suffix = 2; File.Exists(path); suffix++)
        {
            path = Path.Combine(directory, $"{prefix}-{suffix}.db");
        }

        return path;
    }

    private void PruneMigrationBackups(string directory, string databaseName)
    {
        try
        {
            var prefix = $"{databaseName}.pre-migration-";
            var backups = new DirectoryInfo(directory)
                .EnumerateFiles()
                .Where(file => file.Name.StartsWith(prefix, StringComparison.Ordinal) &&
                               string.Equals(file.Extension, ".db", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.CreationTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .Skip(options.Value.MigrationBackupRetentionCount)
                .ToArray();

            foreach (var backup in backups)
            {
                try
                {
                    backup.Delete();
                    logger.LogInformation("Removed expired pre-migration SQLite backup {BackupPath}.", backup.FullName);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception,
                        "Could not remove expired pre-migration SQLite backup {BackupPath}.", backup.FullName);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception,
                "Could not inspect pre-migration SQLite backups in {BackupDirectory} for retention.", directory);
        }
    }

    private static async Task ValidateDatabaseAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA quick_check;";
            await using var reader = await integrity.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var result = reader.GetString(0);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"SQLite integrity check failed: {result}");
                }
            }
        }

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_key_check;";
        await using var violations = await foreignKeys.ExecuteReaderAsync(cancellationToken);
        if (await violations.ReadAsync(cancellationToken))
        {
            var table = violations.GetString(0);
            var rowId = violations.IsDBNull(1)
                ? "unknown"
                : Convert.ToString(violations.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException(
                $"SQLite foreign key check failed for table '{table}', row '{rowId}'.");
        }
    }

    private static void TryDeleteIncompleteBackup(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Preserve the original backup or validation exception.
        }
    }
}
