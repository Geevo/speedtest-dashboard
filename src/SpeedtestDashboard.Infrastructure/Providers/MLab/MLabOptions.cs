namespace SpeedtestDashboard.Infrastructure.Providers.MLab;

public sealed class MLabOptions
{
    public const string SectionName = "Providers:MLab";
    public const string PinnedVersion = "0.10.1";

    public string ExecutablePath { get; set; } = "/usr/local/bin/mlab-ndt7-client";

    public bool Enabled { get; set; } = true;

    public int HealthTimeoutSeconds { get; set; } = 5;

    public int HealthCacheSeconds { get; set; } = 45;

    public int TestTimeoutSeconds { get; set; } = 75;

    public int ClientTimeoutSeconds { get; set; } = 55;
}
