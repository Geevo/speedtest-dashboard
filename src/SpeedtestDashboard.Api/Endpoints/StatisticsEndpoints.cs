using SpeedtestDashboard.Api.ApiKeys;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Statistics;

namespace SpeedtestDashboard.Api.Endpoints;

public static class StatisticsEndpoints
{
    public static IEndpointRouteBuilder MapStatisticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/statistics", GetAsync)
            .WithName("GetSpeedTestStatistics")
            .WithTags("Statistics")
            .Produces<SpeedTestStatistics>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        endpoints.MapGet("/api/v1/statistics", GetAsync)
            .RequireAuthorization(ApiKeyAuthenticationDefaults.PolicyName)
            .RequireRateLimiting(ApiKeyAuthenticationDefaults.ReadRateLimiterPolicy)
            .WithName("GetSpeedTestStatisticsV1")
            .WithTags("API v1")
            .Produces<SpeedTestStatistics>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        HttpRequest request,
        ISpeedTestStatisticsService statistics,
        CancellationToken cancellationToken)
    {
        if (!TryParseQuery(request.Query, out var query, out var error))
        {
            return ProblemResponses.BadRequest("invalid_statistics_query", error);
        }

        return Results.Ok(await statistics.GetAsync(query, cancellationToken));
    }

    private static bool TryParseQuery(IQueryCollection values, out StatisticsQuery query, out string error)
    {
        query = null!;
        error = string.Empty;
        var rangeValue = values.TryGetValue("range", out var suppliedRange) && !string.IsNullOrWhiteSpace(suppliedRange)
            ? suppliedRange.ToString().ToLowerInvariant()
            : "7d";
        var range = rangeValue switch
        {
            "24h" => StatisticsRange.Last24Hours,
            "7d" => StatisticsRange.Last7Days,
            "30d" => StatisticsRange.Last30Days,
            "90d" => StatisticsRange.Last90Days,
            "all" => StatisticsRange.AllTime,
            _ => (StatisticsRange?)null
        };
        if (range is null)
        {
            error = "Range must be 24h, 7d, 30d, 90d, or all.";
            return false;
        }

        ProviderId? provider = null;
        if (values.TryGetValue("provider", out var suppliedProvider) && !string.IsNullOrWhiteSpace(suppliedProvider))
        {
            var providerValue = suppliedProvider.ToString().ToLowerInvariant();
            if (!ProviderId.TryParse(providerValue, out var parsedProvider))
            {
                error = "Provider must be a lowercase identifier between 1 and 32 characters.";
                return false;
            }

            provider = parsedProvider;
        }

        query = new StatisticsQuery(range.Value, provider);
        return true;
    }
}
