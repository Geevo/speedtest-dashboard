namespace SpeedtestDashboard.Infrastructure.Schedules;

public sealed class ScheduleWorkerOptions
{
    public const string SectionName = "Scheduler";

    public int PollIntervalSeconds { get; set; } = 30;
}
