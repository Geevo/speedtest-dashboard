namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class ScheduleRunEntity
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public DateTime ScheduledForUtc { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
    public Guid? JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? FailureCode { get; set; }
}
