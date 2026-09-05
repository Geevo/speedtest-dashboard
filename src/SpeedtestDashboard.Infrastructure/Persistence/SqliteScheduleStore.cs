using Microsoft.EntityFrameworkCore;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Infrastructure.Persistence.Entities;

namespace SpeedtestDashboard.Infrastructure.Persistence;

public sealed class SqliteScheduleStore(
    IDbContextFactory<DashboardDbContext> contextFactory,
    TimeProvider timeProvider) : ISpeedTestScheduleStore
{
    private const int MaximumRunHistory = 200;

    public async Task<SpeedTestSchedule> CreateAsync(
        ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var entity = new SpeedTestScheduleEntity { Id = Guid.NewGuid() };
        ApplyDraft(entity, draft);
        entity.CreatedAtUtc = now.UtcDateTime;
        entity.UpdatedAtUtc = now.UtcDateTime;
        entity.NextRunAtUtc = nextRunAtUtc?.UtcDateTime;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.SpeedTestSchedules.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<SpeedTestSchedule?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.SpeedTestSchedules.AsNoTracking()
            .SingleOrDefaultAsync(schedule => schedule.Id == id, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SpeedTestSchedule>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.SpeedTestSchedules.AsNoTracking()
            .OrderBy(schedule => schedule.Name)
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<SpeedTestSchedule?> UpdateAsync(
        Guid id, ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.SpeedTestSchedules.SingleOrDefaultAsync(schedule => schedule.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        ApplyDraft(entity, draft);
        entity.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.NextRunAtUtc = nextRunAtUtc?.UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<SpeedTestSchedule?> SetEnabledAsync(
        Guid id, bool enabled, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.SpeedTestSchedules.SingleOrDefaultAsync(schedule => schedule.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Enabled = enabled;
        entity.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entity.NextRunAtUtc = nextRunAtUtc?.UtcDateTime;
        await context.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await context.SpeedTestSchedules
            .Where(schedule => schedule.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<IReadOnlyList<SpeedTestSchedule>> GetDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken)
    {
        var cutoff = asOfUtc.UtcDateTime;
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.SpeedTestSchedules.AsNoTracking()
            .Where(schedule => schedule.Enabled && schedule.NextRunAtUtc != null && schedule.NextRunAtUtc <= cutoff)
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    public async Task<bool> TryClaimRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAtUtc,
        ScheduleRun run,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var expected = expectedNextRunAtUtc.UtcDateTime;
        var attempted = run.AttemptedAtUtc.UtcDateTime;
        var next = nextRunAtUtc?.UtcDateTime;
        var claimed = await context.SpeedTestSchedules
            .Where(schedule =>
                schedule.Id == scheduleId &&
                schedule.Enabled &&
                schedule.NextRunAtUtc == expected)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(schedule => schedule.LastRunAtUtc, attempted)
                .SetProperty(schedule => schedule.LastRunStatus, ScheduleRunStatus.Pending.ToString().ToLowerInvariant())
                .SetProperty(schedule => schedule.NextRunAtUtc, next),
                cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        context.ScheduleRuns.Add(new ScheduleRunEntity
        {
            Id = run.Id,
            ScheduleId = scheduleId,
            ScheduledForUtc = run.ScheduledForUtc.UtcDateTime,
            AttemptedAtUtc = run.AttemptedAtUtc.UtcDateTime,
            JobId = run.JobId,
            Status = run.Status.ToString().ToLowerInvariant(),
            FailureCode = run.FailureCode
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task CompleteRunAsync(
        Guid scheduleId,
        Guid runId,
        Guid? jobId,
        ScheduleRunStatus status,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        if (status == ScheduleRunStatus.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A claimed run must be completed with a terminal admission status.");
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var run = await context.ScheduleRuns.SingleOrDefaultAsync(
            entity => entity.Id == runId && entity.ScheduleId == scheduleId,
            cancellationToken);
        if (run is null || !string.Equals(run.Status, "pending", StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        run.JobId = jobId;
        run.Status = status.ToString().ToLowerInvariant();
        run.FailureCode = failureCode;

        var schedule = await context.SpeedTestSchedules.SingleOrDefaultAsync(
            entity => entity.Id == scheduleId && entity.LastRunAtUtc == run.AttemptedAtUtc,
            cancellationToken);
        if (schedule is not null)
        {
            schedule.LastJobId = jobId ?? schedule.LastJobId;
            schedule.LastRunStatus = run.Status;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecoverPendingRunsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var pendingRuns = await context.ScheduleRuns
            .Where(run => run.Status == "pending")
            .ToListAsync(cancellationToken);
        foreach (var run in pendingRuns)
        {
            run.Status = ScheduleRunStatus.Skipped.ToString().ToLowerInvariant();
            run.FailureCode = ScheduleFailureCodes.ApplicationOffline;

            var schedule = await context.SpeedTestSchedules.SingleOrDefaultAsync(
                entity =>
                    entity.Id == run.ScheduleId &&
                    entity.LastRunAtUtc == run.AttemptedAtUtc &&
                    entity.LastRunStatus == "pending",
                cancellationToken);
            if (schedule is not null)
            {
                schedule.LastRunStatus = run.Status;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleRun>> ListRunsAsync(Guid scheduleId, int limit, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await context.ScheduleRuns.AsNoTracking()
            .Where(run => run.ScheduleId == scheduleId)
            .OrderByDescending(run => run.AttemptedAtUtc)
            .Take(Math.Clamp(limit, 1, MaximumRunHistory))
            .ToListAsync(cancellationToken);
        return entities.Select(ToDomain).ToArray();
    }

    private static void ApplyDraft(SpeedTestScheduleEntity entity, ScheduleDraft draft)
    {
        entity.Name = draft.Name;
        entity.ProviderId = draft.ProviderId.Value;
        entity.ServerId = draft.ServerId;
        entity.RecurrenceKind = draft.RecurrenceKind.ToString().ToLowerInvariant();
        entity.RunAtUtc = draft.RunAtUtc?.UtcDateTime;
        entity.IntervalMinutes = draft.IntervalMinutes;
        entity.TimeOfDayMinutes = draft.TimeOfDayMinutes;
        entity.DayOfWeek = draft.DayOfWeek is null ? null : (int)draft.DayOfWeek.Value;
        entity.TimeZoneId = draft.TimeZoneId;
        entity.Enabled = draft.Enabled;
    }

    private static SpeedTestSchedule ToDomain(SpeedTestScheduleEntity entity) => new(
        entity.Id,
        entity.Name,
        ProviderId.Parse(entity.ProviderId),
        entity.ServerId,
        Enum.Parse<ScheduleRecurrenceKind>(entity.RecurrenceKind, ignoreCase: true),
        AsUtcOffset(entity.RunAtUtc),
        entity.IntervalMinutes,
        entity.TimeOfDayMinutes,
        entity.DayOfWeek is null ? null : (DayOfWeek)entity.DayOfWeek.Value,
        entity.TimeZoneId,
        entity.Enabled,
        AsUtcOffset(entity.CreatedAtUtc)!.Value,
        AsUtcOffset(entity.UpdatedAtUtc)!.Value,
        AsUtcOffset(entity.LastRunAtUtc),
        AsUtcOffset(entity.NextRunAtUtc),
        entity.LastJobId,
        entity.LastRunStatus);

    private static ScheduleRun ToDomain(ScheduleRunEntity entity) => new(
        entity.Id,
        entity.ScheduleId,
        AsUtcOffset(entity.ScheduledForUtc)!.Value,
        AsUtcOffset(entity.AttemptedAtUtc)!.Value,
        entity.JobId,
        Enum.Parse<ScheduleRunStatus>(entity.Status, ignoreCase: true),
        entity.FailureCode);

    private static DateTimeOffset? AsUtcOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
