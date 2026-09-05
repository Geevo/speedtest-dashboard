using Microsoft.AspNetCore.HttpOverrides;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Core;
using SpeedtestDashboard.Infrastructure;
using SpeedtestDashboard.Infrastructure.Network;
using SpeedtestDashboard.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddNetworkIdentity(builder.Configuration);
builder.Services.AddDashboardPersistence(builder.Configuration);
builder.Services.AddSpeedTestOrchestration(builder.Configuration);
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

app.UseForwardedHeaders();
app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

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
.WithTags("System");

app.MapNetworkEndpoints();
app.MapProviderEndpoints();
app.MapTestEndpoints();
app.MapHistoryEndpoints();

app.Map("/api/{**path}", () => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: "API endpoint not found"));

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset CheckedAt);

public partial class Program;
