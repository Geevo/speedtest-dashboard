using SpeedtestDashboard.Core.Schedules;

namespace SpeedtestDashboard.Api.Tests.Schedules;

internal sealed class FakeScheduleStore : ISpeedTestScheduleStore
{
    private readonly Dictionary<Guid, SpeedTestSchedule> _schedules = [];
    private readonly List<ScheduleRun> _runs = [];

    public Guid Seed(SpeedTestSchedule schedule)
    {
        _schedules[schedule.Id] = schedule;
        return schedule.Id;
    }

    public SpeedTestSchedule Get(Guid id) => _schedules[id];

    public IReadOnlyList<ScheduleRun> Runs => _runs;

    public bool RejectNextClaim { get; set; }

    public Task<SpeedTestSchedule> CreateAsync(ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Worker tests seed schedules directly.");
    }

    public Task<SpeedTestSchedule?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_schedules.GetValueOrDefault(id));

    public Task<IReadOnlyList<SpeedTestSchedule>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpeedTestSchedule>>(_schedules.Values.ToArray());

    public Task<SpeedTestSchedule?> UpdateAsync(
        Guid id, ScheduleDraft draft, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Worker tests seed schedules directly.");
    }

    public Task<SpeedTestSchedule?> SetEnabledAsync(
        Guid id, bool enabled, DateTimeOffset? nextRunAtUtc, CancellationToken cancellationToken)
    {
        if (!_schedules.TryGetValue(id, out var schedule))
        {
            return Task.FromResult<SpeedTestSchedule?>(null);
        }

        var updated = schedule with { Enabled = enabled, NextRunAtUtc = nextRunAtUtc };
        _schedules[id] = updated;
        return Task.FromResult<SpeedTestSchedule?>(updated);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_schedules.Remove(id));

    public Task<IReadOnlyList<SpeedTestSchedule>> GetDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpeedTestSchedule>>(_schedules.Values
            .Where(schedule => schedule.Enabled && schedule.NextRunAtUtc is not null && schedule.NextRunAtUtc <= asOfUtc)
            .ToArray());

    public Task<bool> TryClaimRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAtUtc,
        ScheduleRun run,
        DateTimeOffset? nextRunAtUtc,
        CancellationToken cancellationToken)
    {
        if (RejectNextClaim)
        {
            RejectNextClaim = false;
            return Task.FromResult(false);
        }

        if (!_schedules.TryGetValue(scheduleId, out var schedule) ||
            !schedule.Enabled ||
            schedule.NextRunAtUtc != expectedNextRunAtUtc)
        {
            return Task.FromResult(false);
        }

        _runs.Add(run);
        _schedules[scheduleId] = schedule with
        {
            LastRunAtUtc = run.AttemptedAtUtc,
            LastRunStatus = ScheduleRunStatus.Pending.ToString().ToLowerInvariant(),
            NextRunAtUtc = nextRunAtUtc
        };

        return Task.FromResult(true);
    }

    public Task CompleteRunAsync(
        Guid scheduleId,
        Guid runId,
        Guid? jobId,
        ScheduleRunStatus status,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        var runIndex = _runs.FindIndex(run => run.Id == runId && run.ScheduleId == scheduleId);
        if (runIndex < 0 || _runs[runIndex].Status != ScheduleRunStatus.Pending)
        {
            return Task.CompletedTask;
        }

        _runs[runIndex] = _runs[runIndex] with { JobId = jobId, Status = status, FailureCode = failureCode };
        if (_schedules.TryGetValue(scheduleId, out var schedule) &&
            schedule.LastRunAtUtc == _runs[runIndex].AttemptedAtUtc)
        {
            _schedules[scheduleId] = schedule with
            {
                LastJobId = jobId ?? schedule.LastJobId,
                LastRunStatus = status.ToString().ToLowerInvariant()
            };
        }

        return Task.CompletedTask;
    }

    public Task RecoverPendingRunsAsync(CancellationToken cancellationToken)
    {
        for (var index = 0; index < _runs.Count; index++)
        {
            var run = _runs[index];
            if (run.Status != ScheduleRunStatus.Pending)
            {
                continue;
            }

            _runs[index] = run with
            {
                Status = ScheduleRunStatus.Skipped,
                FailureCode = ScheduleFailureCodes.ApplicationOffline
            };
            if (_schedules.TryGetValue(run.ScheduleId, out var schedule) &&
                schedule.LastRunAtUtc == run.AttemptedAtUtc)
            {
                _schedules[run.ScheduleId] = schedule with
                {
                    LastRunStatus = ScheduleRunStatus.Skipped.ToString().ToLowerInvariant()
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScheduleRun>> ListRunsAsync(Guid scheduleId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScheduleRun>>(_runs
            .Where(run => run.ScheduleId == scheduleId)
            .OrderByDescending(run => run.AttemptedAtUtc)
            .Take(limit)
            .ToArray());
}
