using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed record DatabaseStorageInfo(
    long DatabaseBytes,
    long WriteAheadLogBytes,
    long SharedMemoryBytes)
{
    public long TotalBytes => checked(DatabaseBytes + WriteAheadLogBytes + SharedMemoryBytes);
}

public sealed record DatabaseCompactionResult(
    DatabaseStorageInfo Before,
    DatabaseStorageInfo After);

public sealed class DatabaseMaintenanceService(
    IDbContextFactory<DashboardDbContext> contextFactory,
    IOptions<StorageOptions> storageOptions)
{
    private readonly SemaphoreSlim _compactionLock = new(1, 1);
    private readonly string _databasePath = storageOptions.Value.DatabasePath;

    public DatabaseStorageInfo GetStorageInfo() => new(
        FileSize(_databasePath),
        FileSize($"{_databasePath}-wal"),
        FileSize($"{_databasePath}-shm"));

    public async Task<DatabaseCompactionResult> CompactAsync(CancellationToken cancellationToken)
    {
        await _compactionLock.WaitAsync(cancellationToken);
        try
        {
            var before = GetStorageInfo();
            await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
            {
                await context.Database.OpenConnectionAsync(cancellationToken);
                await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
                await context.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken);
                await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
            }

            return new DatabaseCompactionResult(before, GetStorageInfo());
        }
        finally
        {
            _compactionLock.Release();
        }
    }

    private static long FileSize(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }
}
