using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Endpoints;

public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/providers", GetProvidersAsync)
            .WithName("GetProviders")
            .WithTags("Providers")
            .Produces<IReadOnlyList<ProviderResponse>>();

        endpoints.MapGet("/api/providers/{providerId}", GetProviderAsync)
            .WithName("GetProvider")
            .WithTags("Providers")
            .Produces<ProviderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        endpoints.MapGet("/api/providers/{providerId}/servers", GetServersAsync)
            .WithName("GetProviderServers")
            .WithTags("Providers")
            .Produces<IReadOnlyList<SpeedTestServerResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetProvidersAsync(
        ISpeedTestProviderRegistry registry,
        CancellationToken cancellationToken)
    {
        var responses = await Task.WhenAll(registry.GetAll().Select(provider =>
            ToResponseAsync(provider, cancellationToken)));
        return Results.Ok(responses);
    }

    private static async Task<IResult> GetProviderAsync(
        string providerId,
        ISpeedTestProviderRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!TryResolveProvider(providerId, registry, out var provider))
        {
            return ProblemResponses.NotFound(
                SpeedTestFailureCodes.ProviderNotFound,
                "The requested speed-test provider is not registered.");
        }

        return Results.Ok(await ToResponseAsync(provider, cancellationToken));
    }

    private static async Task<IResult> GetServersAsync(
        string providerId,
        string? search,
        int? limit,
        ISpeedTestProviderRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!TryResolveProvider(providerId, registry, out var provider))
        {
            return ProblemResponses.NotFound(
                SpeedTestFailureCodes.ProviderNotFound,
                "The requested speed-test provider is not registered.");
        }

        if (!provider.Capabilities.HasFlag(ProviderCapabilities.ServerDiscovery))
        {
            return ProblemResponses.Conflict(
                SpeedTestFailureCodes.CapabilityNotSupported,
                "This provider does not support server discovery.");
        }

        if (search?.Length > 100 || limit is < 1 or > 100)
        {
            return ProblemResponses.BadRequest(
                SpeedTestFailureCodes.InvalidRequest,
                "Server search must be at most 100 characters and limit must be between 1 and 100.");
        }

        try
        {
            var servers = await provider.GetServersAsync(
                new ServerQuery(search?.Trim(), limit ?? 25),
                cancellationToken);
            return Results.Ok(servers.Select(SpeedTestServerResponse.From));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderExecutionException exception)
        {
            return ProblemResponses.Conflict(
                NormalizeCode(exception.Code),
                SanitizeMessage(exception.SafeMessage));
        }
    }

    private static bool TryResolveProvider(
        string value,
        ISpeedTestProviderRegistry registry,
        out ISpeedTestProvider provider)
    {
        provider = null!;
        return ProviderId.TryParse(value, out var providerId) && registry.TryGet(providerId, out provider);
    }

    private static async Task<ProviderResponse> ToResponseAsync(
        ISpeedTestProvider provider,
        CancellationToken cancellationToken)
    {
        ProviderHealth health;
        try
        {
            health = await provider.CheckHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            health = new ProviderHealth(
                provider.Id,
                ProviderHealthState.Unavailable,
                Version: null,
                DateTimeOffset.UtcNow,
                "Provider health could not be determined.");
        }

        return new ProviderResponse(
            provider.Id.Value,
            SanitizeMessage(provider.DisplayName, 80),
            GetCapabilities(provider.Capabilities),
            health.State.ToString().ToLowerInvariant(),
            SanitizeNullable(health.Version, 80),
            health.CheckedAtUtc,
            SanitizeNullable(health.Message, 240));
    }

    private static IReadOnlyList<string> GetCapabilities(ProviderCapabilities capabilities) =>
        Enum.GetValues<ProviderCapabilities>()
            .Where(capability => capability != ProviderCapabilities.None && capabilities.HasFlag(capability))
            .Select(capability => capability.ToString() switch
            {
                nameof(ProviderCapabilities.ServerDiscovery) => "serverDiscovery",
                nameof(ProviderCapabilities.ServerSelection) => "serverSelection",
                nameof(ProviderCapabilities.ResultUrl) => "resultUrl",
                nameof(ProviderCapabilities.PacketLoss) => "packetLoss",
                nameof(ProviderCapabilities.IPv6) => "ipv6",
                _ => capability.ToString().ToLowerInvariant()
            })
            .ToArray();

    private static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 64
            ? SpeedTestFailureCodes.ProviderFailed
            : code;

    private static string SanitizeMessage(string value, int maximumLength = 240)
    {
        var clean = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string? SanitizeNullable(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : SanitizeMessage(value, maximumLength);
}

public sealed record ProviderResponse(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Capabilities,
    string HealthState,
    string? Version,
    DateTimeOffset CheckedAtUtc,
    string? Message);

public sealed record SpeedTestServerResponse(
    string ProviderId,
    string Id,
    string Name,
    string? Sponsor,
    string? Location,
    string? CountryCode,
    string? Host,
    decimal? DistanceKilometres,
    decimal? LatencyMilliseconds)
{
    public static SpeedTestServerResponse From(SpeedTestServer server) => new(
        server.ProviderId.Value,
        server.Id,
        server.Name,
        server.Sponsor,
        server.Location,
        server.CountryCode,
        server.Host,
        server.DistanceKilometres,
        server.LatencyMilliseconds);
}
