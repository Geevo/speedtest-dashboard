using Microsoft.EntityFrameworkCore;
using SpeedtestDashboard.Infrastructure.Persistence;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public sealed class ApiIdempotencyStore(
    IDbContextFactory<DashboardDbContext> contextFactory,
    TimeProvider timeProvider) : IApiIdempotencyStore
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);

    public async Task<ApiIdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        var cutoff = (timeProvider.GetUtcNow() - RetentionPeriod).UtcDateTime;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.ApiIdempotencyRecords
            .SingleOrDefaultAsync(record => record.Key == key && record.CreatedAtUtc >= cutoff, cancellationToken);
        return entity is null
            ? null
            : new ApiIdempotencyRecord(entity.Key, entity.RequestHash, entity.JobId, new DateTimeOffset(entity.CreatedAtUtc, TimeSpan.Zero));
    }

    public async Task SaveAsync(string key, string requestHash, Guid jobId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cutoff = now - RetentionPeriod;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.ApiIdempotencyRecords
            .Where(record => record.CreatedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var existing = await context.ApiIdempotencyRecords.SingleOrDefaultAsync(record => record.Key == key, cancellationToken);
        if (existing is null)
        {
            context.ApiIdempotencyRecords.Add(new ApiIdempotencyRecordEntity
            {
                Key = key,
                RequestHash = requestHash,
                JobId = jobId,
                CreatedAtUtc = now
            });
        }
        else
        {
            existing.RequestHash = requestHash;
            existing.JobId = jobId;
            existing.CreatedAtUtc = now;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request already recorded this key; the first writer wins.
        }
    }
}
