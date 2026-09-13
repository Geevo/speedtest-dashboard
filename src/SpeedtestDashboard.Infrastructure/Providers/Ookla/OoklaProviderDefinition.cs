using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public static class OoklaProviderDefinition
{
    public static readonly ProviderId Id = ProviderId.Parse("ookla");

    public static readonly ProviderDescriptor Descriptor = new(
        Id,
        "Ookla",
        DisplayOrder: 500,
        ProviderCapabilities.ServerDiscovery |
        ProviderCapabilities.ServerSelection |
        ProviderCapabilities.Download |
        ProviderCapabilities.Upload |
        ProviderCapabilities.Latency |
        ProviderCapabilities.Jitter |
        ProviderCapabilities.PacketLoss |
        ProviderCapabilities.ResultUrl,
        "Search ID, sponsor, city, or country",
        "Check the Ookla settings and license acceptance.",
        []);
}

internal static class OoklaFailureCodes
{
    public const string Disabled = "ookla_disabled";
    public const string NotInstalled = "ookla_not_installed";
    public const string LicenseNotAccepted = "ookla_license_not_accepted";
    public const string ServerNotFound = "ookla_server_not_found";
    public const string NetworkUnavailable = "ookla_network_unavailable";
    public const string Timeout = "ookla_timeout";
    public const string Failed = "ookla_failed";
    public const string InvalidOutput = "ookla_invalid_output";
    public const string ResultIncomplete = "ookla_result_incomplete";
}
