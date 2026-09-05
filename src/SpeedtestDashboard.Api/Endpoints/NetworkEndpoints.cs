using SpeedtestDashboard.Core.Network;

namespace SpeedtestDashboard.Api.Endpoints;

public static class NetworkEndpoints
{
    public static IEndpointRouteBuilder MapNetworkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/network", GetNetworkIdentityAsync)
            .WithName("GetNetworkIdentity")
            .WithTags("Network")
            .Produces<NetworkIdentityResponse>()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return endpoints;
    }

    private static async Task<IResult> GetNetworkIdentityAsync(
        bool? refresh,
        INetworkIdentityService networkIdentityService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        try
        {
            var identity = await networkIdentityService.GetAsync(refresh is true, cancellationToken);
            return Results.Ok(NetworkIdentityResponse.From(identity));
        }
        catch (NetworkIdentityRefreshThrottledException exception)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(exception.RetryAfter.TotalSeconds));
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Network identity refresh throttled",
                detail: "Please wait before requesting another manual refresh.",
                extensions: new Dictionary<string, object?>
                {
                    ["retryAfterSeconds"] = retryAfterSeconds
                });
        }
    }
}

public sealed record NetworkIdentityResponse(
    string State,
    DateTimeOffset CheckedAtUtc,
    NetworkAddressIdentityResponse? Ipv4,
    NetworkAddressIdentityResponse? Ipv6,
    bool IsStale,
    string? Warning)
{
    public static NetworkIdentityResponse From(NetworkIdentity identity) => new(
        identity.State.ToString().ToLowerInvariant(),
        identity.CheckedAtUtc,
        NetworkAddressIdentityResponse.From(identity.IPv4),
        NetworkAddressIdentityResponse.From(identity.IPv6),
        identity.IsStale,
        identity.Warning?.ToString() switch
        {
            nameof(NetworkIdentityWarning.RefreshFailed) => "refreshFailed",
            _ => null
        });
}

public sealed record NetworkAddressIdentityResponse(
    string Address,
    string Family,
    string? Asn,
    string? AsName,
    string? Isp,
    string? CountryCode,
    string? CountryName,
    string? Region,
    string? City,
    string AddressSource,
    string? MetadataSource)
{
    public static NetworkAddressIdentityResponse? From(NetworkAddressIdentity? identity) => identity is null
        ? null
        : new NetworkAddressIdentityResponse(
            identity.Address,
            identity.Family == NetworkAddressFamily.IPv4 ? "ipv4" : "ipv6",
            identity.Asn,
            identity.AsName,
            identity.Isp,
            identity.CountryCode,
            identity.CountryName,
            identity.Region,
            identity.City,
            identity.AddressSource,
            identity.MetadataSource);
}
