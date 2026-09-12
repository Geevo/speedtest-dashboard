namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public sealed class FastComOptions
{
    public const string SectionName = "Providers:FastCom";
    public const string PinnedVersion = "0.3.5";

    public string ExecutablePath { get; set; } = "/usr/local/bin/fast-cli";

    public bool Enabled { get; set; } = true;

    public int HealthTimeoutSeconds { get; set; } = 5;

    public int HealthCacheSeconds { get; set; } = 45;

    public int TestTimeoutSeconds { get; set; } = 90;

    public int DurationSeconds { get; set; } = 30;
}
