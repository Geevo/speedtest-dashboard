namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class SpeedTestJobEntity
{
    public Guid Id { get; set; }
    public required string ProviderId { get; set; }
    public required string Status { get; set; }
    public long Version { get; set; }
    public required string Stage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? RequestedServerId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public SpeedTestResultEntity? Result { get; set; }
}
