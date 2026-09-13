using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Tests;

public sealed class HistoryEndpointTests
{
    [Fact]
    public async Task EmptyHistoryAndUnknownDetailsReturnStableContracts()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/history");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, list.GetProperty("nextCursor").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/history/999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/history/999")).StatusCode);
    }

    [Fact]
    public async Task HistoryListFiltersPaginatesAndValidatesUtcAndCursor()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ISpeedTestJobStore>();
        CreateTerminal(store, ProviderId.Parse("provider-a"), SpeedTestJobStatus.Completed);
        CreateTerminal(store, ProviderId.Parse("fixture"), SpeedTestJobStatus.Failed);
        CreateTerminal(store, ProviderId.Parse("provider-a"), SpeedTestJobStatus.Cancelled);

        var first = await client.GetFromJsonAsync<JsonElement>("/api/history?limit=1");
        Assert.Single(first.GetProperty("items").EnumerateArray());
        var cursor = Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!);
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/history?limit=1&cursor={cursor}");
        Assert.Single(second.GetProperty("items").EnumerateArray());

        var completed = await client.GetFromJsonAsync<JsonElement>("/api/history?providerId=provider-a&status=completed");
        var completedItem = Assert.Single(completed.GetProperty("items").EnumerateArray());
        Assert.Equal("completed", completedItem.GetProperty("status").GetString());
        Assert.Equal("provider-a", completedItem.GetProperty("providerId").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/history?cursor=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/history?fromUtc=2026-01-01T00:00:00&toUtc=2026-01-02T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/history?fromUtc=2026-01-02T00:00:00Z&toUtc=2026-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/history?limit=201")).StatusCode);
    }

    [Fact]
    public async Task DetailReturnsNormalizedMetadataAndDeleteEvictsOperationalSnapshot()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ISpeedTestJobStore>();
        var job = CreateTerminal(store, ProviderId.Parse("provider-a"), SpeedTestJobStatus.Completed);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/history");
        var id = list.GetProperty("items")[0].GetProperty("id").GetInt64();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/history/{id}");
        Assert.Equal(job.Id, detail.GetProperty("jobId").GetGuid());
        Assert.Equal(125.5m, detail.GetProperty("result").GetProperty("downloadMbps").GetDecimal());
        Assert.Equal("Fixture ISP", detail.GetProperty("providerMetadata").GetProperty("isp").GetString());
        Assert.Equal("192.0.2.20", detail.GetProperty("egressIdentity").GetProperty("ipv4").GetProperty("address").GetString());
        Assert.Equal("2001:db8::20", detail.GetProperty("egressIdentity").GetProperty("ipv6").GetProperty("address").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/history/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/history/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tests/{job.Id}")).StatusCode);
    }

    private static SpeedTestJob CreateTerminal(ISpeedTestJobStore store, ProviderId provider, SpeedTestJobStatus status)
    {
        var identity = new NetworkIdentity(
            new NetworkAddressIdentity("192.0.2.20", NetworkAddressFamily.IPv4, "AS64500", "Fixture v4", null, "GB", "United Kingdom", null, "London", "fixture", "fixture"),
            new NetworkAddressIdentity("2001:db8::20", NetworkAddressFamily.IPv6, "AS64501", "Fixture v6", null, "GB", "United Kingdom", null, "London", "fixture", "fixture"),
            DateTimeOffset.UtcNow,
            NetworkIdentityState.Complete);
        var job = store.Create(new SpeedTestRequest(provider, "12345"));
        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out _);
        store.Transition(job.Id, SpeedTestJobStatus.Running, "Running", out _, egressIdentity: identity);
        if (status == SpeedTestJobStatus.Completed)
        {
            store.Transition(job.Id, SpeedTestJobStatus.ProcessingResult, "Processing result", out _);
            store.Transition(job.Id, status, "Completed", out var terminal, result: new SpeedTestResult(
                provider, "12345", "Fixture server", "London, United Kingdom", 125.5m, 62.25m, 9.5m, 0.5m, null,
                "https://www.speedtest.net/result/c/fixture", job.Id, identity, "{\"isp\":\"Fixture ISP\"}"));
            return terminal!;
        }

        if (status == SpeedTestJobStatus.Failed)
        {
            store.Transition(job.Id, status, "Failed", out var terminal, failure: new SpeedTestFailure("fixture_failed", "Fixture failure."));
            return terminal!;
        }

        store.Transition(job.Id, status, "Cancelled", out var cancelled);
        return cancelled!;
    }
}
