using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Core.Network;

namespace SpeedtestDashboard.Api.Tests;

public sealed class NetworkEndpointTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _factory;

    public NetworkEndpointTests(DashboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNetwork_ReturnsStableProviderNeutralJsonShape()
    {
        var fake = new FakeNetworkIdentityService((_, _) => Task.FromResult(new NetworkIdentity(
            new NetworkAddressIdentity(
                "8.8.8.8",
                NetworkAddressFamily.IPv4,
                "AS15169",
                "Google LLC",
                null,
                "US",
                "United States",
                null,
                null,
                "ipify",
                "IPinfo Lite"),
            null,
            new DateTimeOffset(2026, 9, 3, 20, 30, 0, TimeSpan.Zero),
            NetworkIdentityState.Complete)));
        using var client = CreateClient(fake);

        using var response = await client.GetAsync("/api/network");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("complete", root.GetProperty("state").GetString());
        Assert.Equal("8.8.8.8", root.GetProperty("ipv4").GetProperty("address").GetString());
        Assert.Equal("AS15169", root.GetProperty("ipv4").GetProperty("asn").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ipv4").GetProperty("isp").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("ipv6").ValueKind);
        Assert.False(root.GetProperty("isStale").GetBoolean());
        Assert.DoesNotContain("token", root.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(fake.LastForceRefresh);
    }

    [Fact]
    public async Task GetNetwork_ReturnsPartialIdentityWithoutDiscardingAddress()
    {
        var fake = new FakeNetworkIdentityService((_, _) => Task.FromResult(new NetworkIdentity(
            new NetworkAddressIdentity(
                "8.8.8.8",
                NetworkAddressFamily.IPv4,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "ipify",
                null),
            null,
            DateTimeOffset.UtcNow,
            NetworkIdentityState.Partial)));
        using var client = CreateClient(fake);

        var response = await client.GetFromJsonAsync<JsonElement>("/api/network");

        Assert.Equal("partial", response.GetProperty("state").GetString());
        Assert.Equal("8.8.8.8", response.GetProperty("ipv4").GetProperty("address").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("ipv4").GetProperty("asn").ValueKind);
    }

    [Fact]
    public async Task ForcedRefresh_PassesRefreshIntentToService()
    {
        var fake = new FakeNetworkIdentityService((_, _) => Task.FromResult(new NetworkIdentity(
            null,
            null,
            DateTimeOffset.UtcNow,
            NetworkIdentityState.Unavailable)));
        using var client = CreateClient(fake);

        var response = await client.GetAsync("/api/network?refresh=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(fake.LastForceRefresh);
    }

    [Fact]
    public async Task ThrottledRefresh_ReturnsProblemDetailsAndRetryAfter()
    {
        var fake = new FakeNetworkIdentityService((_, _) =>
            throw new NetworkIdentityRefreshThrottledException(TimeSpan.FromSeconds(7.2)));
        using var client = CreateClient(fake);

        using var response = await client.GetAsync("/api/network?refresh=true");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("8", response.Headers.RetryAfter?.ToString());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(8, problem.RootElement.GetProperty("retryAfterSeconds").GetInt32());
    }

    [Fact]
    public async Task Health_RemainsHealthyWhenNetworkIdentityProviderFails()
    {
        var fake = new FakeNetworkIdentityService((_, _) => throw new HttpRequestException("upstream failed"));
        using var client = CreateClient(fake);

        var network = await client.GetAsync("/api/network");
        var health = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.InternalServerError, network.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private HttpClient CreateClient(INetworkIdentityService service)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INetworkIdentityService>();
                services.AddSingleton(service);
            });
        });

        return factory.CreateClient();
    }

    private sealed class FakeNetworkIdentityService(
        Func<bool, CancellationToken, Task<NetworkIdentity>> handler) : INetworkIdentityService
    {
        public bool LastForceRefresh { get; private set; }

        public Task<NetworkIdentity> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            LastForceRefresh = forceRefresh;
            return handler(forceRefresh, cancellationToken);
        }
    }
}
