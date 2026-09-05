namespace SpeedtestDashboard.Core.Network;

public enum NetworkIdentityState
{
    Complete,
    Partial,
    Unavailable
}

public enum NetworkAddressFamily
{
    IPv4,
    IPv6
}

public enum NetworkIdentityWarning
{
    RefreshFailed
}

public sealed record NetworkIdentity(
    NetworkAddressIdentity? IPv4,
    NetworkAddressIdentity? IPv6,
    DateTimeOffset CheckedAtUtc,
    NetworkIdentityState State,
    bool IsStale = false,
    NetworkIdentityWarning? Warning = null);

public sealed record NetworkAddressIdentity(
    string Address,
    NetworkAddressFamily Family,
    string? Asn,
    string? AsName,
    string? Isp,
    string? CountryCode,
    string? CountryName,
    string? Region,
    string? City,
    string AddressSource,
    string? MetadataSource);

