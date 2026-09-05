using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Schedules;

namespace SpeedtestDashboard.Api.Tests.Schedules;

public sealed class ScheduleRecurrenceCalculatorTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [Fact]
    public void Interval_AdvancesByExactMinutesFromTheAnchor()
    {
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Interval, intervalMinutes: 45);
        var from = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        Assert.Equal(from.AddMinutes(45), next);
    }

    [Fact]
    public void Interval_LatePollKeepsTheOriginalCadenceAndSkipsPastOccurrences()
    {
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Interval, intervalMinutes: 10);
        var handledOccurrence = new DateTimeOffset(2026, 6, 1, 10, 10, 0, TimeSpan.Zero);
        var polledAt = new DateTimeOffset(2026, 6, 1, 10, 36, 0, TimeSpan.Zero);

        var next = ScheduleRecurrenceCalculator.ComputeNextFutureRunAfter(
            schedule, handledOccurrence, polledAt, London);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 10, 40, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Daily_ReturnsTodayWhenTheTimeOfDayHasNotPassedYet()
    {
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Daily, timeOfDayMinutes: 3 * 60);
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero); // midnight UTC = 01:00 BST

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 2, 0, 0, TimeSpan.Zero), next); // 03:00 BST = 02:00 UTC
    }

    [Fact]
    public void Daily_RollsOverToTomorrowWhenTodaysTimeHasAlreadyPassed()
    {
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Daily, timeOfDayMinutes: 3 * 60);
        var from = new DateTimeOffset(2026, 6, 1, 5, 0, 0, TimeSpan.Zero); // after 03:00 BST already

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        Assert.Equal(new DateTimeOffset(2026, 6, 2, 2, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekly_ReturnsTheSameDayWhenItMatchesAndTheTimeHasNotPassed()
    {
        // 1 June 2026 is a Monday.
        var schedule = CreateSchedule(
            ScheduleRecurrenceKind.Weekly, timeOfDayMinutes: 2 * 60, dayOfWeek: DayOfWeek.Monday);
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero); // 01:00 BST, before the 02:00 target

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        Assert.Equal(new DateTimeOffset(2026, 6, 1, 1, 0, 0, TimeSpan.Zero), next); // 02:00 BST same day
    }

    [Fact]
    public void Weekly_RollsOverToNextWeekWhenTodaysOccurrenceHasPassed()
    {
        // 1 June 2026 is a Monday.
        var schedule = CreateSchedule(
            ScheduleRecurrenceKind.Weekly, timeOfDayMinutes: 2 * 60, dayOfWeek: DayOfWeek.Monday);
        var from = new DateTimeOffset(2026, 6, 1, 5, 0, 0, TimeSpan.Zero); // 06:00 BST, after the 02:00 target

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        Assert.Equal(DayOfWeek.Monday, TimeZoneInfo.ConvertTime(next!.Value, London).DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 1, 0, 0, TimeSpan.Zero), next); // next Monday, 02:00 BST
    }

    [Fact]
    public void Daily_SkipsForwardPastASpringForwardGap()
    {
        // Europe/London moves from GMT to BST at 01:00 UTC on 2026-03-29; 01:00-02:00 local does not exist.
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Daily, timeOfDayMinutes: 90); // 01:30 local
        var from = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        // 01:30 is invalid on the transition day; the first valid minute is 02:00 BST == 01:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Daily_HandlesANonHourSpringForwardGap()
    {
        var lordHowe = TimeZoneInfo.FindSystemTimeZoneById("Australia/Lord_Howe");
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Daily, timeOfDayMinutes: 2 * 60 + 15);
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, lordHowe);

        // 02:00-02:29 local is skipped; 02:30 daylight time is the first valid minute.
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 15, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Daily_ResolvesAnAmbiguousFallBackTimeToStandardTime()
    {
        // Europe/London moves from BST to GMT at 02:00 BST on 2026-10-25; 01:00-02:00 local occurs twice.
        var schedule = CreateSchedule(ScheduleRecurrenceKind.Daily, timeOfDayMinutes: 90); // 01:30 local
        var from = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, from, London);

        // .NET resolves an ambiguous local time to the standard-time (GMT) interpretation.
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void OneOff_NeverProducesAnotherOccurrence()
    {
        var runAt = new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule(ScheduleRecurrenceKind.OneOff, runAtUtc: runAt);

        var next = ScheduleRecurrenceCalculator.ComputeNextRunAfter(schedule, runAt, London);

        Assert.Null(next);
    }

    [Fact]
    public void OneOff_InitialNextRunIsTheConfiguredInstant()
    {
        var runAt = new DateTimeOffset(2026, 6, 1, 3, 0, 0, TimeSpan.Zero);
        var draft = new ScheduleDraft(
            "Test", ProviderId.Parse("librespeed"), null, ScheduleRecurrenceKind.OneOff,
            runAt, null, null, null, "Europe/London", true);

        var next = ScheduleRecurrenceCalculator.ComputeInitialNextRun(draft, DateTimeOffset.UtcNow, London);

        Assert.Equal(runAt, next);
    }

    private static SpeedTestSchedule CreateSchedule(
        ScheduleRecurrenceKind kind,
        int? intervalMinutes = null,
        int? timeOfDayMinutes = null,
        DayOfWeek? dayOfWeek = null,
        DateTimeOffset? runAtUtc = null) => new(
        Guid.NewGuid(),
        "Test schedule",
        ProviderId.Parse("librespeed"),
        null,
        kind,
        runAtUtc,
        intervalMinutes,
        timeOfDayMinutes,
        dayOfWeek,
        "Europe/London",
        true,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        null,
        null,
        null);
}
