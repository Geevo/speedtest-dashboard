using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public static class FastComProviderDefinition
{
    public static readonly ProviderId Id = ProviderId.Parse("fastcom");

    public static readonly ProviderDescriptor Descriptor = new(
        Id,
        "FAST.com",
        DisplayOrder: 200,
        ProviderCapabilities.Download |
        ProviderCapabilities.Upload |
        ProviderCapabilities.Latency,
        ServerSearchLabel: string.Empty,
        "Ensure the packaged fast-cli binary is available.",
        []);
}

internal static class FastComFailureCodes
{
    public const string Disabled = "fastcom_disabled";
    public const string NotInstalled = "fastcom_not_installed";
    public const string NetworkUnavailable = "fastcom_network_unavailable";
    public const string Timeout = "fastcom_timeout";
    public const string Failed = "fastcom_failed";
    public const string InvalidOutput = "fastcom_invalid_output";
    public const string ResultIncomplete = "fastcom_result_incomplete";
}
