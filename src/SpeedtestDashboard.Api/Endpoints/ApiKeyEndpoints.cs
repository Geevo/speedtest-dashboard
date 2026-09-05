using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Infrastructure.ApiKeys;

namespace SpeedtestDashboard.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/api-key", GetApiKeyAsync)
            .WithName("GetApiKey")
            .WithTags("ApiKey")
            .Produces<ApiKeyResponse>();

        endpoints.MapPost("/api/api-key/regenerate", RegenerateApiKeyAsync)
            .RequireCsrf()
            .WithName("RegenerateApiKey")
            .WithTags("ApiKey")
            .Produces<ApiKeyResponse>();

        endpoints.MapDelete("/api/api-key", RevokeApiKeyAsync)
            .RequireCsrf()
            .WithName("RevokeApiKey")
            .WithTags("ApiKey")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetApiKeyAsync(
        IApiCredentialService credentialService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var snapshot = await credentialService.GetAsync(cancellationToken);
        return Results.Ok(ApiKeyResponse.From(snapshot));
    }

    private static async Task<IResult> RegenerateApiKeyAsync(
        IApiCredentialService credentialService,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var snapshot = await credentialService.RegenerateAsync(cancellationToken);
        return Results.Ok(ApiKeyResponse.From(snapshot));
    }

    private static async Task<IResult> RevokeApiKeyAsync(
        IApiCredentialService credentialService,
        CancellationToken cancellationToken)
    {
        var revoked = await credentialService.RevokeAsync(cancellationToken);
        return revoked
            ? Results.NoContent()
            : ProblemResponses.NotFound("api_key_not_found", "No API key exists to revoke.");
    }
}

public sealed record ApiKeyResponse(bool Enabled, string? Key, DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastUsedAtUtc)
{
    public static ApiKeyResponse From(ApiCredentialSnapshot? snapshot) => snapshot is null
        ? new ApiKeyResponse(false, null, null, null)
        : new ApiKeyResponse(true, snapshot.Key, snapshot.CreatedAtUtc, snapshot.LastUsedAtUtc);
}
