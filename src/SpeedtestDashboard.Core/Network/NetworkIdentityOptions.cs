namespace SpeedtestDashboard.Core.Network;

public sealed class NetworkIdentityOptions
{
    public const string SectionName = "NetworkIdentity";

    public int SuccessCacheSeconds { get; set; } = 300;

    public int FailureCacheSeconds { get; set; } = 30;

    public int RefreshThrottleSeconds { get; set; } = 10;

    public int RequestTimeoutSeconds { get; set; } = 5;

    public string MetadataProvider { get; set; } = "ipconfig";
}
