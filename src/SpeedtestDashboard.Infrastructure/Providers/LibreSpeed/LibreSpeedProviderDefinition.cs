using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public static class LibreSpeedProviderDefinition
{
    public static readonly ProviderId Id = ProviderId.Parse("librespeed");

    public static readonly ProviderDescriptor Descriptor = new(
        Id,
        "LibreSpeed",
        DisplayOrder: 100,
        ProviderCapabilities.ServerDiscovery |
        ProviderCapabilities.ServerSelection |
        ProviderCapabilities.Download |
        ProviderCapabilities.Upload |
        ProviderCapabilities.Latency |
        ProviderCapabilities.Jitter,
        "Search ID, sponsor, location, or host",
        "Include the LibreSpeed CLI when building the image.",
        []);
}

internal static class LibreSpeedFailureCodes
{
    public const string Disabled = "librespeed_disabled";
    public const string NotInstalled = "librespeed_not_installed";
    public const string ServerListFailed = "librespeed_server_list_failed";
    public const string ServerNotFound = "librespeed_server_not_found";
    public const string NetworkUnavailable = "librespeed_network_unavailable";
    public const string Timeout = "librespeed_timeout";
    public const string Failed = "librespeed_failed";
    public const string InvalidOutput = "librespeed_invalid_output";
    public const string ResultIncomplete = "librespeed_result_incomplete";
}
