namespace SpeedtestDashboard.Core.Providers;

public sealed record ServerQuery(string? Search = null, int Limit = 25);

public sealed record SpeedTestServer(
    ProviderId ProviderId,
    string Id,
    string Name,
    string? Sponsor,
    string? Location,
    string? CountryCode,
    string? Host,
    decimal? DistanceKilometres,
    decimal? LatencyMilliseconds);

