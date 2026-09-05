namespace SpeedtestDashboard.Core.Schedules;

public static class ScheduleRecurrenceCalculator
{
    private const int MaximumSearchDays = 8;

    public static DateTimeOffset? ComputeInitialNextRun(ScheduleDraft draft, DateTimeOffset nowUtc, TimeZoneInfo timeZone) =>
        draft.RecurrenceKind switch
        {
            ScheduleRecurrenceKind.OneOff => draft.RunAtUtc,
            ScheduleRecurrenceKind.Interval => nowUtc.AddMinutes(draft.IntervalMinutes!.Value),
            ScheduleRecurrenceKind.Daily => NextDailyOrWeekly(nowUtc, timeZone, draft.TimeOfDayMinutes!.Value, dayOfWeek: null),
            ScheduleRecurrenceKind.Weekly => NextDailyOrWeekly(nowUtc, timeZone, draft.TimeOfDayMinutes!.Value, draft.DayOfWeek),
            _ => throw new ArgumentOutOfRangeException(nameof(draft), "Unsupported recurrence kind."),
        };

    public static DateTimeOffset? ComputeNextRunAfter(SpeedTestSchedule schedule, DateTimeOffset afterUtc, TimeZoneInfo timeZone) =>
        schedule.RecurrenceKind switch
        {
            ScheduleRecurrenceKind.OneOff => null,
            ScheduleRecurrenceKind.Interval => afterUtc.AddMinutes(schedule.IntervalMinutes!.Value),
            ScheduleRecurrenceKind.Daily => NextDailyOrWeekly(afterUtc, timeZone, schedule.TimeOfDayMinutes!.Value, dayOfWeek: null),
            ScheduleRecurrenceKind.Weekly => NextDailyOrWeekly(afterUtc, timeZone, schedule.TimeOfDayMinutes!.Value, schedule.DayOfWeek),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule), "Unsupported recurrence kind."),
        };

    public static DateTimeOffset? ComputeNextFutureRunAfter(
        SpeedTestSchedule schedule,
        DateTimeOffset handledOccurrenceUtc,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone) => schedule.RecurrenceKind switch
        {
            ScheduleRecurrenceKind.OneOff => null,
            ScheduleRecurrenceKind.Interval => NextFutureInterval(
                handledOccurrenceUtc, nowUtc, schedule.IntervalMinutes!.Value),
            ScheduleRecurrenceKind.Daily => NextDailyOrWeekly(
                nowUtc, timeZone, schedule.TimeOfDayMinutes!.Value, dayOfWeek: null),
            ScheduleRecurrenceKind.Weekly => NextDailyOrWeekly(
                nowUtc, timeZone, schedule.TimeOfDayMinutes!.Value, schedule.DayOfWeek),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule), "Unsupported recurrence kind."),
        };

    private static DateTimeOffset NextDailyOrWeekly(
        DateTimeOffset fromUtc,
        TimeZoneInfo timeZone,
        int timeOfDayMinutes,
        DayOfWeek? dayOfWeek)
    {
        var localToday = TimeZoneInfo.ConvertTime(fromUtc, timeZone).Date;
        for (var offset = 0; offset < MaximumSearchDays; offset++)
        {
            var date = localToday.AddDays(offset);
            if (dayOfWeek is not null && date.DayOfWeek != dayOfWeek.Value)
            {
                continue;
            }

            var candidateLocal = DateTime.SpecifyKind(date.AddMinutes(timeOfDayMinutes), DateTimeKind.Unspecified);
            candidateLocal = AdvanceToFirstValidMinute(candidateLocal, timeZone);

            var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidateLocal, timeZone);
            if (candidateUtc > fromUtc.UtcDateTime)
            {
                return new DateTimeOffset(candidateUtc, TimeSpan.Zero);
            }
        }

        throw new InvalidOperationException("Could not compute the next scheduled occurrence.");
    }

    private static DateTimeOffset NextFutureInterval(
        DateTimeOffset handledOccurrenceUtc,
        DateTimeOffset nowUtc,
        int intervalMinutes)
    {
        var intervalTicks = TimeSpan.FromMinutes(intervalMinutes).Ticks;
        var elapsedTicks = Math.Max(0, (nowUtc - handledOccurrenceUtc).Ticks);
        var intervalsToAdvance = (elapsedTicks / intervalTicks) + 1;
        return handledOccurrenceUtc.AddTicks(checked(intervalsToAdvance * intervalTicks));
    }

    private static DateTime AdvanceToFirstValidMinute(DateTime candidateLocal, TimeZoneInfo timeZone)
    {
        const int maximumMinutesInSkippedDay = 24 * 60;
        for (var minute = 0; minute <= maximumMinutesInSkippedDay; minute++)
        {
            if (!timeZone.IsInvalidTime(candidateLocal))
            {
                return candidateLocal;
            }

            candidateLocal = candidateLocal.AddMinutes(1);
        }

        throw new InvalidOperationException("Could not resolve a valid local time after a daylight-saving gap.");
    }
}
