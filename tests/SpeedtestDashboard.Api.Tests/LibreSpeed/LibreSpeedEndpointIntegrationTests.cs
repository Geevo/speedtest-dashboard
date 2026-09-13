using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

public sealed class LibreSpeedEndpointIntegrationTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _baseFactory;

    public LibreSpeedEndpointIntegrationTests(DashboardWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task ProviderAndServerEndpointsExposeSecondProductionProvider()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var providers = await client.GetFromJsonAsync<JsonElement>("/api/providers");
        var provider = await client.GetFromJsonAsync<JsonElement>("/api/providers/librespeed");
        var servers = await client.GetFromJsonAsync<JsonElement>("/api/providers/librespeed/servers?search=T%C5%8Dky%C5%8D&limit=100");

        Assert.Equal(["librespeed", "fastcom", "mlab", "ookla"], providers.EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("available", provider.GetProperty("healthState").GetString());
        Assert.Equal("1.0.13", provider.GetProperty("version").GetString());
        var capabilities = provider.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("serverDiscovery", capabilities);
        Assert.Contains("jitter", capabilities);
        Assert.DoesNotContain("packetLoss", capabilities);
        Assert.DoesNotContain("resultUrl", capabilities);
        Assert.Equal("82", Assert.Single(servers.EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task CompletedTestUsesExistingWorkerPersistenceAndHistoryPipeline()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var created = await CreateAsync(client, null);
        var completed = await WaitForTerminalAsync(client, created);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/history?providerId=librespeed");

        Assert.Equal("completed", completed.Status);
        Assert.Equal(934.25m, completed.Result?.DownloadMbps);
        Assert.Equal(104.2m, completed.Result?.UploadMbps);
        Assert.Null(completed.Result?.PacketLossPercent);
        Assert.Null(completed.Result?.ResultUrl);
        var item = Assert.Single(
            history.GetProperty("items").EnumerateArray(),
            entry => entry.GetProperty("jobId").GetGuid() == created.Id);
        Assert.Equal(created.Id, item.GetProperty("jobId").GetGuid());
        Assert.Equal("librespeed", item.GetProperty("providerId").GetString());
        Assert.NotNull(item.GetProperty("ipv4Address").GetString());
        var command = Assert.Single(runner.Requests, request => request.ArgumentList.Contains("--json"));
        Assert.Equal(["--json", "--no-icmp", "--secure"], command.ArgumentList);
    }

    [Fact]
    public async Task ExplicitServerUsesSeparateArgumentAndPersistsRequestedSelection()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var completed = await WaitForTerminalAsync(client, await CreateAsync(client, "49"));

        Assert.Equal("49", completed.Result?.ServerId);
        var command = Assert.Single(runner.Requests, request => request.ArgumentList.Contains("--json"));
        Assert.Equal(["--json", "--no-icmp", "--secure", "--server", "49"], command.ArgumentList);
    }

    [Fact]
    public async Task CompletedLibreSpeedResultRemainsVisibleAfterApplicationRestart()
    {
        Guid jobId;
        using (var firstFactory = CreateFactory(StandardRunner()))
        using (var firstClient = firstFactory.CreateClient())
        {
            var created = await CreateAsync(firstClient, null);
            await WaitForTerminalAsync(firstClient, created);
            jobId = created.Id;
        }

        using var replacementFactory = CreateFactory(StandardRunner());
        using var replacementClient = replacementFactory.CreateClient();
        var restoredJob = await replacementClient.GetFromJsonAsync<SpeedTestJobResponse>($"/api/tests/{jobId}");
        var history = await replacementClient.GetFromJsonAsync<JsonElement>("/api/history?providerId=librespeed");

        Assert.Equal("completed", restoredJob?.Status);
        Assert.Equal(934.25m, restoredJob?.Result?.DownloadMbps);
        Assert.Contains(
            history.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("jobId").GetGuid() == jobId);
    }

    [Fact]
    public async Task HostileServerIdIsRejectedBeforeLibreSpeedStarts()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/tests",
            new { providerId = "librespeed", serverId = "49;touch /tmp/pwned" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", problem.GetProperty("code").GetString());
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task CancellationReachesLibreSpeedAndPersistsCancelledHistory()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = StandardRunner();
        runner.Handler = async (request, token) =>
        {
            if (request.ArgumentList.Contains("--version"))
            {
                return VersionResult(request);
            }

            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                return LibreSpeedRecordingProcessRunner.Result(exitCode: null, reason: ProcessTerminationReason.Cancelled);
            }

            throw new InvalidOperationException();
        };
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var created = await CreateAsync(client, null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var response = await client.PostAsync($"/api/tests/{created.Id}/cancel", null);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var job = await client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/history?providerId=librespeed&status=cancelled");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("cancelled", job?.Status);
        Assert.Contains(
            history.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("jobId").GetGuid() == created.Id);
    }

    [Fact]
    public async Task SseDeliversLibreSpeedTerminalResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = StandardRunner();
        runner.Handler = async (request, token) =>
        {
            if (request.ArgumentList.Contains("--version"))
            {
                return VersionResult(request);
            }

            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return LibreSpeedRecordingProcessRunner.Result(stdout: LibreSpeedTestFactory.Fixture("result-complete.json"));
        };
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var created = await CreateAsync(client, null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var streamResponse = await client.GetAsync(created.EventsUrl, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        release.TrySetResult();

        JsonElement terminal;
        do
        {
            terminal = await ReadEventDataAsync(reader);
        }
        while (!terminal.TryGetProperty("job", out var job) || job.GetProperty("status").GetString() != "completed");

        Assert.Equal(934.25m, terminal.GetProperty("job").GetProperty("result").GetProperty("downloadMbps").GetDecimal());
    }

    private WebApplicationFactory<Program> CreateFactory(LibreSpeedRecordingProcessRunner runner)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Providers:LibreSpeed:Enabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcessRunner>();
                services.AddSingleton<IProcessRunner>(runner);
                services.RemoveAll<INetworkIdentityService>();
                services.AddSingleton<INetworkIdentityService>(new FakeNetworkIdentityService());
                services.RemoveAll<LibreSpeedServerCatalogClient>();
                services.AddSingleton(serviceProvider => new LibreSpeedServerCatalogClient(
                    new HttpClient(new LibreSpeedTestFactory.StubHandler((_, _) =>
                        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(LibreSpeedTestFactory.Fixture("servers-normal.json"))
                        })))
                    { Timeout = Timeout.InfiniteTimeSpan },
                    serviceProvider.GetRequiredService<LibreSpeedServerCatalogParser>(),
                    Options.Create(LibreSpeedTestFactory.Options()),
                    NullLogger<LibreSpeedServerCatalogClient>.Instance));
            });
        });
    }

    private static LibreSpeedRecordingProcessRunner StandardRunner() => new()
    {
        Handler = (request, _) => Task.FromResult(
            request.ArgumentList.Contains("--version")
                ? VersionResult(request)
                : LibreSpeedRecordingProcessRunner.Result(stdout: LibreSpeedTestFactory.Fixture("result-complete.json")))
    };

    private static ProcessResult VersionResult(ProcessRequest request) =>
        request.Executable.Contains("librespeed", StringComparison.OrdinalIgnoreCase)
            ? LibreSpeedRecordingProcessRunner.Result(stdout: "librespeed-cli v1.0.13 (built on fixture)\n")
            : LibreSpeedRecordingProcessRunner.Result(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n");

    private static async Task<CreateTestResponse> CreateAsync(HttpClient client, string? serverId)
    {
        using var response = await client.PostAsJsonAsync("/api/tests", new { providerId = "librespeed", serverId });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateTestResponse>())!;
    }

    private static async Task<SpeedTestJobResponse> WaitForTerminalAsync(
        HttpClient client,
        CreateTestResponse created)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            var job = await client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl, timeout.Token);
            if (job is not null && job.Status is "completed" or "failed" or "cancelled")
            {
                return job;
            }

            await Task.Delay(10, timeout.Token);
        }

        throw new TimeoutException($"Job {created.Id} did not become terminal.");
    }

    private static async Task<JsonElement> ReadEventDataAsync(StreamReader reader)
    {
        string? data = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0 && data is not null)
            {
                return JsonDocument.Parse(data).RootElement.Clone();
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = data is null ? line[6..] : data + "\n" + line[6..];
            }
        }

        throw new EndOfStreamException();
    }
}
