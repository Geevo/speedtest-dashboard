using System.Net;
using System.Net.Http.Json;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;

namespace SpeedtestDashboard.Api.Tests.ApiKeys;

public sealed class ApiIdempotencyTests
{
    [Fact]
    public async Task SameIdempotencyKeyAndSameRequest_ReturnsTheSameJob()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), runWorker: false, queueCapacity: 4);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var first = await SendCreateAsync(client, "same-request-key", "fixture", null);
        using var second = await SendCreateAsync(client, "same-request-key", "fixture", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateTestResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.Equal(firstBody!.Id, secondBody!.Id);
    }

    [Fact]
    public async Task SameIdempotencyKeyWithADifferentRequest_Returns409()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), runWorker: false, queueCapacity: 4);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var first = await SendCreateAsync(client, "conflicting-key", "fixture", null);
        using var second = await SendCreateAsync(client, "conflicting-key", "fixture", "a-different-server");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task ExpiredIdempotencyRecord_AllowsANewJobUnderTheSameKey()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var factory = new ApiV1WebApplicationFactory(
            new FakeSpeedTestProvider(), runWorker: false, queueCapacity: 4, timeProvider: timeProvider);
        var apiKey = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, apiKey);

        using var first = await SendCreateAsync(client, "expiring-key", "fixture", null);
        var firstBody = await first.Content.ReadFromJsonAsync<CreateTestResponse>();

        timeProvider.Advance(TimeSpan.FromHours(25));

        using var second = await SendCreateAsync(client, "expiring-key", "fixture", null);
        var secondBody = await second.Content.ReadFromJsonAsync<CreateTestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.NotEqual(firstBody!.Id, secondBody!.Id);
    }

    private static Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client, string idempotencyKey, string providerId, string? serverId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tests")
        {
            Content = JsonContent.Create(new { providerId, serverId })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }
}
