using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Tests;

public sealed class ProviderAndTestEndpointTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _baseFactory;

    public ProviderAndTestEndpointTests(DashboardWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task ProductionRegistration_ReportsBothProvidersEvenWhenUnavailable()
    {
        using var client = _baseFactory.CreateClient();

        var providers = await client.GetFromJsonAsync<JsonElement>("/api/providers");

        Assert.Equal(JsonValueKind.Array, providers.ValueKind);
        var registered = providers.EnumerateArray().ToArray();
        Assert.Equal(
            ["librespeed", "ookla"],
            registered.Select(provider => provider.GetProperty("id").GetString()));
        Assert.All(registered, provider =>
            Assert.Equal("unavailable", provider.GetProperty("healthState").GetString()));
    }

    [Fact]
    public async Task ProviderEndpoints_ExposeHealthCapabilitiesAndServers()
    {
        var provider = new FakeSpeedTestProvider
        {
            Capabilities = ProviderCapabilities.Download |
                           ProviderCapabilities.ServerDiscovery |
                           ProviderCapabilities.ServerSelection,
            Servers =
            [
                new SpeedTestServer(
                    ProviderId.Parse("fixture"), "server-1", "Lab server", "Fixture ISP",
                    "London", "GB", "speed.example.test:443", 4.2m, 8.1m)
            ]
        };
        using var factory = CreateFactory(provider);
        using var client = factory.CreateClient();

        var list = await client.GetFromJsonAsync<JsonElement>("/api/providers");
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/providers/fixture");
        var servers = await client.GetFromJsonAsync<JsonElement>("/api/providers/fixture/servers?limit=10");

        var listed = Assert.Single(list.EnumerateArray());
        Assert.Equal("fixture", listed.GetProperty("id").GetString());
        Assert.Equal("available", detail.GetProperty("healthState").GetString());
        Assert.Contains("serverDiscovery", detail.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("server-1", Assert.Single(servers.EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ProviderEndpoints_ReturnStableProblemsForUnknownAndUnsupportedRequests()
    {
        using var factory = CreateFactory(new FakeSpeedTestProvider());
        using var client = factory.CreateClient();

        using var unknown = await client.GetAsync("/api/providers/missing");
        using var unsupported = await client.GetAsync("/api/providers/fixture/servers");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("provider_not_found", await ReadProblemCodeAsync(unknown));
        Assert.Equal(HttpStatusCode.Conflict, unsupported.StatusCode);
        Assert.Equal("capability_not_supported", await ReadProblemCodeAsync(unsupported));
    }

    [Fact]
    public async Task ProviderFailure_DoesNotChangeApplicationHealth()
    {
        var provider = new FakeSpeedTestProvider { HealthState = ProviderHealthState.Unavailable };
        using var factory = CreateFactory(provider);
        using var client = factory.CreateClient();

        using var providerResponse = await client.GetAsync("/api/providers/fixture");
        using var healthResponse = await client.GetAsync("/api/health");
        var detail = await providerResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("unavailable", detail.GetProperty("healthState").GetString());
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    [Fact]
    public async Task CreateAndGetTest_RunThroughWorkerAndCaptureEgressIdentity()
    {
        var identity = new FakeNetworkIdentityService();
        var provider = new FakeSpeedTestProvider();
        using var factory = CreateFactory(provider, identity);
        using var client = factory.CreateClient();

        using var createResponse = await client.PostAsJsonAsync("/api/tests", new { providerId = "fixture" });
        var created = await createResponse.Content.ReadFromJsonAsync<CreateTestResponse>();

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.Equal($"/api/tests/{created.Id}", createResponse.Headers.Location?.ToString());

        SpeedTestJobResponse? job = null;
        await TestWait.UntilAsync(() =>
        {
            job = client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl).GetAwaiter().GetResult();
            return job?.Status == "completed";
        });

        Assert.Equal(1, provider.RunCalls);
        Assert.Equal(1, identity.Calls);
        Assert.Equal("8.8.8.8", job!.EgressIdentity?.Ipv4?.Address);
        Assert.Equal(100, job.Result?.DownloadMbps);
        Assert.True(job.Version >= 5);
    }

    [Fact]
    public async Task CreateTest_ValidatesProviderCapabilityAndAvailability()
    {
        var unavailable = new FakeSpeedTestProvider { HealthState = ProviderHealthState.Unavailable };
        using var unavailableFactory = CreateFactory(unavailable);
        using var unavailableClient = unavailableFactory.CreateClient();
        using var unavailableResponse = await unavailableClient.PostAsJsonAsync(
            "/api/tests", new { providerId = "fixture" });

        var basic = new FakeSpeedTestProvider();
        using var basicFactory = CreateFactory(basic);
        using var basicClient = basicFactory.CreateClient();
        using var unsupportedResponse = await basicClient.PostAsJsonAsync(
            "/api/tests", new { providerId = "fixture", serverId = "server-1" });
        using var invalidResponse = await basicClient.PostAsJsonAsync(
            "/api/tests", new { providerId = "Fixture" });

        Assert.Equal(HttpStatusCode.Conflict, unavailableResponse.StatusCode);
        Assert.Equal("provider_unavailable", await ReadProblemCodeAsync(unavailableResponse));
        Assert.Equal(HttpStatusCode.Conflict, unsupportedResponse.StatusCode);
        Assert.Equal("capability_not_supported", await ReadProblemCodeAsync(unsupportedResponse));
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("invalid_request", await ReadProblemCodeAsync(invalidResponse));
    }

    [Fact]
    public async Task FullQueue_Returns429WithRetryAfterWithoutStartingAnotherJob()
    {
        var provider = new FakeSpeedTestProvider();
        using var factory = CreateFactory(provider, runWorker: false, queueCapacity: 1);
        using var client = factory.CreateClient();

        using var accepted = await client.PostAsJsonAsync("/api/tests", new { providerId = "fixture" });
        using var rejected = await client.PostAsJsonAsync("/api/tests", new { providerId = "fixture" });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("queue_full", await ReadProblemCodeAsync(rejected));
        Assert.Equal("2", rejected.Headers.RetryAfter?.ToString());
        Assert.Equal(0, provider.RunCalls);
    }

    [Fact]
    public async Task RunningTest_CanBeCancelledAndProviderReceivesCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeSpeedTestProvider
        {
            RunHandler = async (_, cancellationToken, _) =>
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }

                throw new InvalidOperationException("The cancelled provider unexpectedly continued.");
            }
        };
        using var factory = CreateFactory(provider);
        using var client = factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/api/tests", new { providerId = "fixture" }))
            .Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.NotNull(created);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var cancelResponse = await client.PostAsync($"/api/tests/{created.Id}/cancel", null);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var job = await client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl);

        Assert.Equal(HttpStatusCode.Accepted, cancelResponse.StatusCode);
        Assert.Equal("cancelled", job?.Status);
        Assert.Equal("cancelled", job?.Failure?.Code);
    }

    [Fact]
    public async Task UnknownAndTerminalJobs_ReturnStableProblems()
    {
        using var factory = CreateFactory(new FakeSpeedTestProvider());
        using var client = factory.CreateClient();
        using var unknown = await client.GetAsync($"/api/tests/{Guid.NewGuid()}");
        var created = await (await client.PostAsJsonAsync("/api/tests", new { providerId = "fixture" }))
            .Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.NotNull(created);
        await TestWait.UntilAsync(() =>
            client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl).GetAwaiter().GetResult()?.Status == "completed");

        using var terminalCancel = await client.PostAsync($"/api/tests/{created.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("job_not_found", await ReadProblemCodeAsync(unknown));
        Assert.Equal(HttpStatusCode.Conflict, terminalCancel.StatusCode);
        Assert.Equal("job_terminal", await ReadProblemCodeAsync(terminalCancel));
    }

    [Fact]
    public async Task EventStream_SendsTypedVersionedSnapshotAndStateEvents()
    {
        using var factory = CreateFactory(new FakeSpeedTestProvider(), runWorker: false);
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ISpeedTestJobStore>();
        var job = store.Create(new SpeedTestRequest(ProviderId.Parse("fixture"), null));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tests/{job.Id}/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var snapshot = await ReadSseEventAsync(reader);

        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out var starting);
        var state = await ReadSseEventAsync(reader);
        store.Transition(job.Id, SpeedTestJobStatus.Cancelled, "Cancelled", out _);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("snapshot", snapshot.Event);
        Assert.Equal("1", snapshot.Id);
        Assert.Equal(1, snapshot.Data.GetProperty("version").GetInt64());
        Assert.Equal("queued", snapshot.Data.GetProperty("job").GetProperty("status").GetString());
        Assert.Equal("state", state.Event);
        Assert.Equal(starting!.Version.ToString(), state.Id);
        Assert.Equal("starting", state.Data.GetProperty("job").GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnknownEventStream_Returns404ProblemDetails()
    {
        using var factory = CreateFactory(new FakeSpeedTestProvider());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/tests/{Guid.NewGuid()}/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("job_not_found", await ReadProblemCodeAsync(response));
    }

    private WebApplicationFactory<Program> CreateFactory(
        FakeSpeedTestProvider provider,
        FakeNetworkIdentityService? identity = null,
        bool runWorker = true,
        int queueCapacity = 4)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("SpeedTests:QueueCapacity", queueCapacity.ToString());
            builder.UseSetting("SpeedTests:QueueFullRetryAfterSeconds", "2");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INetworkIdentityService>();
                services.AddSingleton<INetworkIdentityService>(identity ?? new FakeNetworkIdentityService());
                services.RemoveAll<ISpeedTestProvider>();
                services.AddSingleton<ISpeedTestProvider>(provider);
                if (!runWorker)
                {
                    services.RemoveAll<IHostedService>();
                }
            });
        });
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private static async Task<(string? Event, string? Id, JsonElement Data)> ReadSseEventAsync(StreamReader reader)
    {
        string? eventType = null;
        string? id = null;
        string? data = null;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0 && data is not null)
            {
                return (eventType, id, JsonDocument.Parse(data).RootElement.Clone());
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line[4..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = data is null ? line[6..] : data + "\n" + line[6..];
            }
        }

        throw new EndOfStreamException("The SSE stream ended before a complete event arrived.");
    }
}
