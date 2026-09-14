using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpeedtestDashboard.Api.Tests;

public sealed class DatabaseEndpointTests
{
    [Fact]
    public async Task StorageReportsFilesAndCompactionReturnsRefreshedSize()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var initial = await client.GetFromJsonAsync<JsonElement>("/api/database/storage");
        Assert.True(initial.GetProperty("databaseBytes").GetInt64() > 0);
        Assert.Equal(
            initial.GetProperty("databaseBytes").GetInt64() +
            initial.GetProperty("writeAheadLogBytes").GetInt64() +
            initial.GetProperty("sharedMemoryBytes").GetInt64(),
            initial.GetProperty("totalBytes").GetInt64());

        using var response = await client.PostAsync("/api/database/compact", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var compacted = await response.Content.ReadFromJsonAsync<JsonElement>();
        var after = compacted.GetProperty("after");
        var refreshed = await client.GetFromJsonAsync<JsonElement>("/api/database/storage");
        Assert.Equal(after.GetProperty("databaseBytes").GetInt64(), refreshed.GetProperty("databaseBytes").GetInt64());
        Assert.Equal(after.GetProperty("totalBytes").GetInt64(), refreshed.GetProperty("totalBytes").GetInt64());
    }
}
