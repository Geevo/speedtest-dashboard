namespace SpeedtestDashboard.Api.ApiKeys;

internal static class ApiKeyAuthenticationDefaults
{
    public const string SchemeName = "ApiKey";
    public const string PolicyName = "ApiKey";
    public const string ReadRateLimiterPolicy = "apiv1-read";
    public const string WriteRateLimiterPolicy = "apiv1-write";
}
