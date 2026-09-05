using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Core.Schedules;

public enum ScheduleRecurrenceKind
{
    OneOff,
    Interval,
    Daily,
    Weekly
}

public enum ScheduleRunStatus
{
    Pending,
    Queued,
    Skipped,
    Failed
}

public static class ScheduleFailureCodes
{
    public const string QueueFull = "queue_full";
    public const string PreviousRunActive = "previous_run_active";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string InvalidServer = "invalid_server";
    public const string PersistenceFailed = "persistence_failed";
    public const string ApplicationOffline = "application_offline";
}

public sealed record SpeedTestSchedule(
    Guid Id,
    string Name,
    ProviderId ProviderId,
    string? ServerId,
    ScheduleRecurrenceKind RecurrenceKind,
    DateTimeOffset? RunAtUtc,
    int? IntervalMinutes,
    int? TimeOfDayMinutes,
    DayOfWeek? DayOfWeek,
    string TimeZoneId,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? NextRunAtUtc,
    Guid? LastJobId,
    string? LastRunStatus);

public sealed record ScheduleRun(
    Guid Id,
    Guid ScheduleId,
    DateTimeOffset ScheduledForUtc,
    DateTimeOffset AttemptedAtUtc,
    Guid? JobId,
    ScheduleRunStatus Status,
    string? FailureCode);

public sealed record ScheduleDraft(
    string Name,
    ProviderId ProviderId,
    string? ServerId,
    ScheduleRecurrenceKind RecurrenceKind,
    DateTimeOffset? RunAtUtc,
    int? IntervalMinutes,
    int? TimeOfDayMinutes,
    DayOfWeek? DayOfWeek,
    string TimeZoneId,
    bool Enabled);

public interface ISpeedTestScheduleStore
{
    Task<SpeedTestSchedule> CreateAsync(ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken);

    Task<SpeedTestSchedule?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpeedTestSchedule>> ListAsync(CancellationToken cancellationToken);

    Task<SpeedTestSchedule?> UpdateAsync(Guid id, ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken);

    Task<SpeedTestSchedule?> SetEnabledAsync(Guid id, bool enabled, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpeedTestSchedule>> GetDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken);

    Task<bool> TryClaimRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAtUtc,
        ScheduleRun run,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken);

    Task CompleteRunAsync(
        Guid scheduleId,
        Guid runId,
        Guid? jobId,
        ScheduleRunStatus status,
        string? failureCode,
        CancellationToken cancellationToken);

    Task RecoverPendingRunsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ScheduleRun>> ListRunsAsync(Guid scheduleId, int limit, CancellationToken cancellationToken);
}
