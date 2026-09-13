using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.MLab;

public static class MLabProviderDefinition
{
    public static readonly ProviderId Id = ProviderId.Parse("mlab");

    public static readonly ProviderDescriptor Descriptor = new(
        Id,
        "M-Lab",
        DisplayOrder: 300,
        ProviderCapabilities.Download |
        ProviderCapabilities.Upload |
        ProviderCapabilities.Latency,
        ServerSearchLabel: string.Empty,
        "Ensure the packaged M-Lab NDT7 client is available.",
        [
            new ProviderDisclosure(
                "privacy",
                "M-Lab publishes test data, including this connection's IP address and test time, and retains it indefinitely.",
                "https://www.measurementlab.net/privacy/")
        ]);
}

internal static class MLabFailureCodes
{
    public const string Disabled = "mlab_disabled";
    public const string NotInstalled = "mlab_not_installed";
    public const string NetworkUnavailable = "mlab_network_unavailable";
    public const string Timeout = "mlab_timeout";
    public const string Failed = "mlab_failed";
    public const string InvalidOutput = "mlab_invalid_output";
    public const string ResultIncomplete = "mlab_result_incomplete";
}
