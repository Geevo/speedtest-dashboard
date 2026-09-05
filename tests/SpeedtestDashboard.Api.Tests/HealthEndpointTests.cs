using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SpeedtestDashboard.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(DashboardWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsHealthyServiceContract()
    {
        var response = await _client.GetAsync("/api/health");
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(health);
        Assert.Equal("healthy", health.Status);
        Assert.Equal("Speedtest Dashboard", health.Service);
        Assert.Equal("0.10.0", health.Version);
        Assert.True(health.CheckedAt <= DateTimeOffset.UtcNow);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemDetailsInsteadOfSpa()
    {
        var response = await _client.GetAsync("/api/not-a-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
