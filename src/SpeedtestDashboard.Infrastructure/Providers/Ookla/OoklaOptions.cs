namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public sealed class OoklaOptions
{
    public const string SectionName = "Providers:Ookla";
    public const string PinnedVersion = "1.2.0.84";

    public string ExecutablePath { get; set; } = "/usr/bin/speedtest";

    public bool Enabled { get; set; } = true;

    public bool AcceptLicense { get; set; }

    public bool AcceptGdpr { get; set; }

    public int HealthTimeoutSeconds { get; set; } = 5;

    public int HealthCacheSeconds { get; set; } = 45;

    public int TestTimeoutSeconds { get; set; } = 180;

    public int ServerListTimeoutSeconds { get; set; } = 30;

    public int ServerCacheSeconds { get; set; } = 300;

    public int MaximumServers { get; set; } = 100;
}
