namespace SpeedtestDashboard.Core.Providers;

public sealed record ProviderDisclosure(string Kind, string Message, string? Url = null);

public sealed record ProviderDescriptor(
    ProviderId Id,
    string DisplayName,
    int DisplayOrder,
    ProviderCapabilities Capabilities,
    string ServerSearchLabel,
    string UnavailableGuidance,
    IReadOnlyList<ProviderDisclosure> Disclosures);
