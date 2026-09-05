using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Schedules;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Schedules;

/// <summary>
/// Polls due schedules and submits them through the same <see cref="ISpeedTestSubmissionService"/>
/// used by manual and machine test creation. This worker never executes a provider directly and
/// never bypasses the bounded queue.
/// </summary>
public sealed class ScheduleWorker(
    ISpeedTestScheduleStore scheduleStore,
    ISpeedTestSubmissionService submissionService,
    ISpeedTestJobStore jobStore,
    IOptions<ScheduleWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ScheduleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await scheduleStore.RecoverPendingRunsAsync(stoppingToken);
            await ReconcileOnStartupAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds)));
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "Unexpected scheduler polling failure.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    /// <summary>
    /// Handles occurrences that were due while the application was offline. It only records a
    /// skipped/missed run and recalculates the next future occurrence; it never enqueues a test.
    /// </summary>
    internal async Task ReconcileOnStartupAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await scheduleStore.GetDueAsync(now, cancellationToken);
        foreach (var schedule in due)
        {
            var scheduledFor = schedule.NextRunAtUtc ?? now;
            var timeZone = ResolveTimeZoneOrNull(schedule.TimeZoneId);
            DateTimeOffset? nextRun = schedule.RecurrenceKind == ScheduleRecurrenceKind.OneOff || timeZone is null
                ? null
                : ScheduleRecurrenceCalculator.ComputeNextFutureRunAfter(schedule, scheduledFor, now, timeZone);

            var run = PendingRun(schedule, scheduledFor);
            if (!await scheduleStore.TryClaimRunAsync(
                    schedule.Id, scheduledFor, run, nextRun, cancellationToken))
            {
                continue;
            }

            await scheduleStore.CompleteRunAsync(
                schedule.Id,
                run.Id,
                jobId: null,
                ScheduleRunStatus.Skipped,
                ScheduleFailureCodes.ApplicationOffline,
                cancellationToken);

            logger.LogInformation(
                "Schedule {ScheduleId} missed its occurrence while the application was offline; skipping to the next future run.",
                schedule.Id);
        }
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await scheduleStore.GetDueAsync(now, cancellationToken);
        foreach (var schedule in due)
        {
            await ProcessDueScheduleAsync(schedule, now, cancellationToken);
        }
    }

    private async Task ProcessDueScheduleAsync(SpeedTestSchedule schedule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var scheduledFor = schedule.NextRunAtUtc ?? now;
        var timeZone = ResolveTimeZoneOrNull(schedule.TimeZoneId);
        DateTimeOffset? nextRun = schedule.RecurrenceKind == ScheduleRecurrenceKind.OneOff || timeZone is null
            ? null
            : ScheduleRecurrenceCalculator.ComputeNextFutureRunAfter(schedule, scheduledFor, now, timeZone);

        var run = PendingRun(schedule, scheduledFor);
        if (!await scheduleStore.TryClaimRunAsync(
                schedule.Id, scheduledFor, run, nextRun, cancellationToken))
        {
            return;
        }

        if (schedule.LastJobId is { } lastJobId && jobStore.TryGet(lastJobId, out var lastJob) && !lastJob.IsTerminal)
        {
            await scheduleStore.CompleteRunAsync(
                schedule.Id, run.Id, jobId: null, ScheduleRunStatus.Skipped,
                ScheduleFailureCodes.PreviousRunActive, cancellationToken);
            return;
        }

        var request = new SpeedTestRequest(schedule.ProviderId, schedule.ServerId);
        var result = await submissionService.SubmitAsync(request, cancellationToken);
        if (result.Outcome == SpeedTestSubmissionOutcome.Created)
        {
            await scheduleStore.CompleteRunAsync(
                schedule.Id, run.Id, result.Job!.Id, ScheduleRunStatus.Queued,
                failureCode: null, cancellationToken);
            return;
        }

        var (status, failureCode) = result.Outcome switch
        {
            SpeedTestSubmissionOutcome.QueueFull => (ScheduleRunStatus.Skipped, ScheduleFailureCodes.QueueFull),
            SpeedTestSubmissionOutcome.ProviderUnavailable or SpeedTestSubmissionOutcome.ProviderNotFound =>
                (ScheduleRunStatus.Failed, ScheduleFailureCodes.ProviderUnavailable),
            SpeedTestSubmissionOutcome.PersistenceFailed =>
                (ScheduleRunStatus.Failed, ScheduleFailureCodes.PersistenceFailed),
            _ => (ScheduleRunStatus.Failed, ScheduleFailureCodes.InvalidServer)
        };
        await scheduleStore.CompleteRunAsync(
            schedule.Id, run.Id, jobId: null, status, failureCode, cancellationToken);
    }

    private ScheduleRun PendingRun(SpeedTestSchedule schedule, DateTimeOffset scheduledFor) => new(
        Guid.NewGuid(),
        schedule.Id,
        scheduledFor,
        timeProvider.GetUtcNow(),
        JobId: null,
        ScheduleRunStatus.Pending,
        FailureCode: null);

    private TimeZoneInfo? ResolveTimeZoneOrNull(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("Schedule references unknown time zone {TimeZoneId}.", timeZoneId);
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            logger.LogWarning("Schedule references an invalid time zone {TimeZoneId}.", timeZoneId);
            return null;
        }
    }
}
