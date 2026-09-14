namespace SpeedtestDashboard.Core.Providers;

public enum ProviderHealthState
{
    Available,
    Unavailable,
    Degraded
}

public sealed record ProviderHealth(
    ProviderId ProviderId,
    ProviderHealthState State,
    string? Version,
    DateTimeOffset CheckedAtUtc,
    string? Message,
    bool Installed);
