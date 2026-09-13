using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpeedtestDashboard.Api.Tests.Statistics;

public sealed class StatisticsEndpointTests
{
    [Fact]
    public async Task DashboardEndpointReturnsTypedEmptyStatisticsAndValidatesFilters()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<JsonElement>("/api/statistics?range=90d&provider=ookla");

        Assert.Equal("90d", response.GetProperty("range").GetString());
        Assert.Equal("ookla", response.GetProperty("provider").GetString());
        Assert.Equal(0, response.GetProperty("tests").GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("download").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/statistics?range=year")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/statistics?provider=fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/statistics?provider=bad_id")).StatusCode);
    }

    [Fact]
    public async Task ApiV1StatisticsRequiresTheInstanceApiKey()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/statistics")).StatusCode);
    }
}
