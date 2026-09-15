using Microsoft.AspNetCore.HttpOverrides;
using SpeedtestDashboard.Api.ApiKeys;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Hosting;
using SpeedtestDashboard.Api.Serialization;
using SpeedtestDashboard.Core;
using SpeedtestDashboard.Infrastructure;
using SpeedtestDashboard.Infrastructure.ApiKeys;
using SpeedtestDashboard.Infrastructure.Network;
using SpeedtestDashboard.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default));

var dashboardListenUrl = DashboardPortConfiguration.GetListenUrl(
    Environment.GetEnvironmentVariable(DashboardPortConfiguration.EnvironmentVariableName));
if (dashboardListenUrl is not null)
{
    builder.WebHost.UseUrls(dashboardListenUrl);
}

builder.Services.AddProblemDetails();
builder.Services.AddNetworkIdentity(builder.Configuration);
builder.Services.AddDashboardPersistence(builder.Configuration);
builder.Services.AddSpeedTestOrchestration(builder.Configuration);
builder.Services.AddDashboardAuthentication(builder.Configuration);
builder.Services.AddApiKeyPersistence();
builder.Services.AddApiKeyAuthentication();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 2;

    if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = AppConstants.MaxRequestBodySizeBytes;
});

var app = builder.Build();

await app.Services.GetRequiredService<DashboardDatabaseInitializer>().InitializeAsync();
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DashboardAuthenticationStateInitializer>().InitializeAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new HealthResponse(
        Status: "healthy",
        Service: AppConstants.ServiceName,
        Version: AppConstants.Version,
        CheckedAt: DateTimeOffset.UtcNow));
})
.WithName("GetHealth")
.WithTags("System")
.AllowAnonymous();

app.MapAuthenticationEndpoints();
app.MapApiKeyEndpoints();
app.MapNetworkEndpoints();
app.MapProviderEndpoints();
app.MapTestEndpoints();
app.MapHistoryEndpoints();
app.MapStatisticsEndpoints();
app.MapScheduleEndpoints();
app.MapDatabaseEndpoints();

app.Map("/api/{**path}", () => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "API endpoint not found"));

app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset CheckedAt);

public partial class Program;
