using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;

namespace SpeedtestDashboard.Api.Tests.ApiKeys;

public sealed class ApiV1EndpointTests
{
    [Fact]
    public async Task NetworkRead_ReturnsIdentityForAValidKey()
    {
        var identity = new FakeNetworkIdentityService();
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), identity);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var response = await client.GetAsync("/api/v1/network");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("8.8.8.8", body.GetProperty("ipv4").GetProperty("address").GetString());
    }

    [Fact]
    public async Task ProviderRead_ReturnsTheRegisteredProvider()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider());
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var response = await client.GetAsync("/api/v1/providers/fixture");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("fixture", body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task MissingApiKey_Returns401()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider());
        await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongApiKey_Returns401()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider());
        await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, "std_definitely-the-wrong-key");

        using var response = await client.GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DashboardCookieAuth_DoesNotSubstituteForAnApiKey()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider());
        await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);

        // "None" mode auto-authenticates every dashboard request, but must not satisfy the ApiKey scheme.
        using var client = factory.CreateClient();
        using var dashboardResponse = await client.GetAsync("/api/providers");
        using var v1Response = await client.GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, v1Response.StatusCode);
    }

    [Fact]
    public async Task CreateGetAndCancelTest_WorkThroughApiKeyAuthentication()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeSpeedTestProvider
        {
            RunHandler = async (_, cancellationToken, _) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The cancelled provider unexpectedly continued.");
            }
        };
        using var factory = new ApiV1WebApplicationFactory(provider);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var createResponse = await client.PostAsJsonAsync("/api/v1/tests", new { providerId = "fixture" });
        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.NotNull(created);
        Assert.StartsWith("/api/v1/tests/", created!.ResourceUrl);
        Assert.Equal($"/api/v1/tests/{created.Id}/events", created.EventsUrl);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var getResponse = await client.GetAsync($"/api/v1/tests/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var eventsRequest = new HttpRequestMessage(HttpMethod.Get, created.EventsUrl);
        using var eventsResponse = await client.SendAsync(eventsRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, eventsResponse.StatusCode);
        Assert.Equal("text/event-stream", eventsResponse.Content.Headers.ContentType?.MediaType);

        using var cancelResponse = await client.PostAsync($"/api/v1/tests/{created.Id}/cancel", null);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<SpeedTestJobResponse>();

        Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);
        Assert.Equal("cancelled", cancelled?.Status);
    }

    [Fact]
    public async Task HistoryListAndDetail_ReturnThePersistedRecord()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider());
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        var created = await (await client.PostAsJsonAsync("/api/v1/tests", new { providerId = "fixture" }))
            .Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.NotNull(created);
        await TestWait.UntilAsync(() =>
            client.GetFromJsonAsync<SpeedTestJobResponse>($"/api/v1/tests/{created!.Id}")
                .GetAwaiter().GetResult()?.Status == "completed");

        using var listResponse = await client.GetAsync("/api/v1/history");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        var historyId = item.GetProperty("id").GetInt64();

        using var detailResponse = await client.GetAsync($"/api/v1/history/{historyId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(created!.Id.ToString(), detail.GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task QueueSaturation_Returns429WithRetryAfterAndTheQueueFullCode()
    {
        var provider = new FakeSpeedTestProvider();
        using var factory = new ApiV1WebApplicationFactory(provider, runWorker: false, queueCapacity: 1);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var accepted = await client.PostAsJsonAsync("/api/v1/tests", new { providerId = "fixture" });
        using var rejected = await client.PostAsJsonAsync("/api/v1/tests", new { providerId = "fixture" });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("queue_full", problem.GetProperty("code").GetString());
        Assert.Equal("2", rejected.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task WriteRateLimiting_Returns429WithRetryAfterAfterTenRequestsPerMinute()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), runWorker: false, queueCapacity: 20);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        HttpResponseMessage? limited = null;
        for (var attempt = 0; attempt < 11 && limited is null; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/tests", new { providerId = "fixture" });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
            else
            {
                response.Dispose();
            }
        }

        Assert.NotNull(limited);
        Assert.NotNull(limited!.Headers.RetryAfter);
        limited.Dispose();
    }
}
