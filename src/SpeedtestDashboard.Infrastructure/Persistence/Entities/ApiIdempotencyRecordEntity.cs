namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class ApiIdempotencyRecordEntity
{
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
