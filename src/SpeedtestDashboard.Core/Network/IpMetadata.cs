using System.Net;

namespace SpeedtestDashboard.Core.Network;

public sealed record IpMetadata(
    IPAddress Address,
    string? Asn,
    string? AsName,
    string? Isp,
    string? CountryCode,
    string? CountryName,
    string? Region,
    string? City,
    string Source);

