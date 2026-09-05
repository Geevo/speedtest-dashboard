namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class SpeedTestScheduleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string? ServerId { get; set; }
    public string RecurrenceKind { get; set; } = string.Empty;

    public DateTime? RunAtUtc { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? TimeOfDayMinutes { get; set; }
    public int? DayOfWeek { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;

    public bool Enabled { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastRunAtUtc { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
    public Guid? LastJobId { get; set; }
    public string? LastRunStatus { get; set; }
}
