namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public sealed class LibreSpeedOptions
{
    public const string SectionName = "Providers:LibreSpeed";
    public const string PinnedVersion = "1.0.13";
    public const string PinnedCommit = "2f2408764d88e9601aa64a03b340f8e3151003e4";
    public const string ServerCatalogUrl = "https://librespeed.org/backend-servers/servers.php";

    public string ExecutablePath { get; set; } = "/usr/local/bin/librespeed-cli";

    public bool Enabled { get; set; } = true;

    public int HealthTimeoutSeconds { get; set; } = 5;

    public int HealthCacheSeconds { get; set; } = 45;

    public int TestTimeoutSeconds { get; set; } = 180;

    public int ServerListTimeoutSeconds { get; set; } = 20;

    public int ServerCacheSeconds { get; set; } = 300;

    public int MaximumServers { get; set; } = 250;

    public bool DisableIcmp { get; set; } = true;

    public bool PreferHttps { get; set; } = true;
}
