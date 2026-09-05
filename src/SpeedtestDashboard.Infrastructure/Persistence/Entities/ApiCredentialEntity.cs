namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class ApiCredentialEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public string ProtectedSecret { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastUsedAtUtc { get; set; }
}
