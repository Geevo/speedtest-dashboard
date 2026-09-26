namespace SpeedtestDashboard.Api.Authentication;

public sealed class DashboardAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string DataProtectionPath { get; set; } = "/data/dataprotection";
}
