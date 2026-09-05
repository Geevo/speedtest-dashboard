using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Tests.Schedules;

public sealed class ScheduleWorkerTests
{
    private static readonly ProviderId FixtureProvider = ProviderId.Parse("fixture");

    [Fact]
    public async Task OneOffSchedule_ExecutesOnceThenNeverAgain()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider);
        var runAt = now.AddMinutes(10);
        var id = harness.Store.Seed(CreateSchedule(ScheduleRecurrenceKind.OneOff, runAtUtc: runAt, nextRunAtUtc: runAt));

        timeProvider.Advance(TimeSpan.FromMinutes(11));
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        var afterFirstPoll = harness.Store.Get(id);
        Assert.Single(harness.Store.Runs);
        Assert.Equal(ScheduleRunStatus.Queued, harness.Store.Runs[0].Status);
        Assert.NotNull(afterFirstPoll.LastJobId);
        Assert.Null(afterFirstPoll.NextRunAtUtc);

        timeProvider.Advance(TimeSpan.FromDays(1));
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        Assert.Single(harness.Store.Runs); // no second run was ever attempted
    }

    [Fact]
    public async Task RecurringSchedule_AdvancesAndRunsAgain()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider);
        var id = harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10)));

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await harness.Worker.PollOnceAsync(CancellationToken.None);
        var firstJob = harness.Store.Get(id).LastJobId;

        // The prior job must finish before the next occurrence is admitted (see the
        // dedicated previous-run-active test for the still-running case).
        harness.JobStore.Transition(firstJob!.Value, SpeedTestJobStatus.Cancelled, "Cancelled", out _);

        Assert.NotNull(harness.Store.Get(id).NextRunAtUtc);
        timeProvider.Advance(harness.Store.Get(id).NextRunAtUtc!.Value - timeProvider.GetUtcNow());
        await harness.Worker.PollOnceAsync(CancellationToken.None);
        var secondJob = harness.Store.Get(id).LastJobId;

        Assert.Equal(2, harness.Store.Runs.Count);
        Assert.NotEqual(firstJob, secondJob);
    }

    [Fact]
    public async Task DisabledSchedule_NeverExecutes()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider);
        harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10), enabled: false));

        timeProvider.Advance(TimeSpan.FromDays(1));
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        Assert.Empty(harness.Store.Runs);
    }

    [Fact]
    public async Task QueueFull_SkipsTheOccurrenceAndAdvances()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider, queueCapacity: 1);
        harness.Queue.TryEnqueue(Guid.NewGuid()); // occupy the only queue slot
        var id = harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10)));

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        var run = Assert.Single(harness.Store.Runs);
        Assert.Equal(ScheduleRunStatus.Skipped, run.Status);
        Assert.Equal(ScheduleFailureCodes.QueueFull, run.FailureCode);
        Assert.Null(run.JobId);
        Assert.NotNull(harness.Store.Get(id).NextRunAtUtc); // advanced, not stuck retrying immediately
        Assert.True(harness.Store.Get(id).NextRunAtUtc > timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task ConcurrentScheduleChange_RejectsTheClaimBeforeSubmittingAJob()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var provider = new FakeSpeedTestProvider();
        var harness = new ScheduleWorkerHarness(provider, timeProvider);
        harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10)));
        harness.Store.RejectNextClaim = true;

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        Assert.Empty(harness.Store.Runs);
        Assert.Equal(0, provider.HealthCheckCalls);
    }

    [Fact]
    public async Task PreviousRunStillActive_SkipsTheNewOccurrence()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        // No worker drains the in-memory queue in this harness, so the first job stays "queued" (non-terminal).
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider, queueCapacity: 4);
        var id = harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10)));

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await harness.Worker.PollOnceAsync(CancellationToken.None);
        var firstRunJobId = harness.Store.Get(id).LastJobId;
        Assert.NotNull(firstRunJobId);

        timeProvider.Advance(harness.Store.Get(id).NextRunAtUtc!.Value - timeProvider.GetUtcNow());
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, harness.Store.Runs.Count);
        var secondRun = harness.Store.Runs[1];
        Assert.Equal(ScheduleRunStatus.Skipped, secondRun.Status);
        Assert.Equal(ScheduleFailureCodes.PreviousRunActive, secondRun.FailureCode);
        Assert.Equal(firstRunJobId, harness.Store.Get(id).LastJobId); // unchanged; no duplicate job was queued
    }

    [Fact]
    public async Task StartupReconciliation_RecordsAtMostOneMissedRunAndDoesNotReplay()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider);
        var id = harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10)));

        // The application was "offline" for 5 hours, during which ~30 ten-minute occurrences were missed.
        timeProvider.Advance(TimeSpan.FromHours(5));
        await harness.Worker.ReconcileOnStartupAsync(CancellationToken.None);

        var missedRun = Assert.Single(harness.Store.Runs);
        Assert.Equal(ScheduleRunStatus.Skipped, missedRun.Status);
        Assert.Equal(ScheduleFailureCodes.ApplicationOffline, missedRun.FailureCode);
        Assert.Null(missedRun.JobId);
        var rescheduled = harness.Store.Get(id).NextRunAtUtc;
        Assert.NotNull(rescheduled);
        Assert.True(rescheduled > timeProvider.GetUtcNow());

        // The first normal poll immediately afterward must not find anything newly due.
        await harness.Worker.PollOnceAsync(CancellationToken.None);
        Assert.Single(harness.Store.Runs);
    }

    [Fact]
    public async Task ExpiredOneOffSchedule_IsMarkedMissedAndNeverExecutes()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var harness = new ScheduleWorkerHarness(new FakeSpeedTestProvider(), timeProvider);
        var runAt = now.AddMinutes(5);
        var id = harness.Store.Seed(CreateSchedule(ScheduleRecurrenceKind.OneOff, runAtUtc: runAt, nextRunAtUtc: runAt));

        // The application was offline across the scheduled moment.
        timeProvider.Advance(TimeSpan.FromHours(1));
        await harness.Worker.ReconcileOnStartupAsync(CancellationToken.None);

        var run = Assert.Single(harness.Store.Runs);
        Assert.Equal(ScheduleRunStatus.Skipped, run.Status);
        Assert.Null(run.JobId);
        Assert.Null(harness.Store.Get(id).NextRunAtUtc); // never runs
        Assert.Null(harness.Store.Get(id).LastJobId);
    }

    [Fact]
    public async Task ProviderBecomingUnavailable_RecordsASafeFailureAndAdvances()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var provider = new FakeSpeedTestProvider();
        var harness = new ScheduleWorkerHarness(provider, timeProvider);
        var id = harness.Store.Seed(CreateSchedule(
            ScheduleRecurrenceKind.Interval, intervalMinutes: 10, nextRunAtUtc: now.AddMinutes(10)));

        // The provider that was valid at schedule-creation time is no longer available.
        provider.HealthState = ProviderHealthState.Unavailable;
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        await harness.Worker.PollOnceAsync(CancellationToken.None);

        var run = Assert.Single(harness.Store.Runs);
        Assert.Equal(ScheduleRunStatus.Failed, run.Status);
        Assert.Equal(ScheduleFailureCodes.ProviderUnavailable, run.FailureCode);
        Assert.Null(run.JobId);
        Assert.NotNull(harness.Store.Get(id).NextRunAtUtc); // still advances instead of retrying forever
    }

    private static SpeedTestSchedule CreateSchedule(
        ScheduleRecurrenceKind kind,
        DateTimeOffset? runAtUtc = null,
        int? intervalMinutes = null,
        DateTimeOffset? nextRunAtUtc = null,
        bool enabled = true) => new(
        Guid.NewGuid(),
        "Test schedule",
        FixtureProvider,
        null,
        kind,
        runAtUtc,
        intervalMinutes,
        null,
        null,
        "Europe/London",
        enabled,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        nextRunAtUtc,
        null,
        null);
}
