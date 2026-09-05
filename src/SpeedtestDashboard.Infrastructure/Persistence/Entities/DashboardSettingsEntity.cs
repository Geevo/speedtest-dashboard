namespace SpeedtestDashboard.Infrastructure.Persistence.Entities;

public sealed class DashboardSettingsEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool AuthenticationEnabled { get; set; }
    public bool ShowAuthenticationDisabledWarning { get; set; } = true;
}
