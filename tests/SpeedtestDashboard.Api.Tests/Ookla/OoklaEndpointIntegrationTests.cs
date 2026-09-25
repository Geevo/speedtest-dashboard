using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Api.Tests.Ookla;

public sealed class OoklaEndpointIntegrationTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _baseFactory;

    public OoklaEndpointIntegrationTests(DashboardWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task ProviderAndServerEndpointsExposeRealOoklaRegistration()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var providers = await client.GetFromJsonAsync<JsonElement>("/api/providers");
        var provider = await client.GetFromJsonAsync<JsonElement>("/api/providers/ookla");
        var servers = await client.GetFromJsonAsync<JsonElement>("/api/providers/ookla/servers?search=London");

        Assert.Contains(
            providers.EnumerateArray(),
            item => item.GetProperty("id").GetString() == "ookla");
        Assert.Equal("available", provider.GetProperty("healthState").GetString());
        Assert.Equal("1.2.0.84", provider.GetProperty("version").GetString());
        Assert.Contains("packetLoss", provider.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain("ipv6", provider.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("12345", Assert.Single(servers.EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task AutomaticTestCompletesThroughExistingWorkerWithNormalizedResult()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var created = await CreateAsync(client, serverId: null);
        var completed = await WaitForTerminalAsync(client, created);

        Assert.Equal("completed", completed.Status);
        Assert.Equal(1000m, completed.Result?.DownloadMbps);
        Assert.Equal(104.2m, completed.Result?.UploadMbps);
        Assert.Equal("12345", completed.Result?.ServerId);
        var command = Assert.Single(runner.Requests, request => request.ArgumentList.Contains("--format=json"));
        Assert.DoesNotContain(command.ArgumentList, argument => argument.StartsWith("--server-id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExplicitServerTestUsesOnlyValidatedServerSelector()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var completed = await WaitForTerminalAsync(client, await CreateAsync(client, "12345"));

        Assert.Equal("completed", completed.Status);
        var command = Assert.Single(runner.Requests, request => request.ArgumentList.Contains("--format=json"));
        Assert.Contains("--server-id=12345", command.ArgumentList);
    }

    [Fact]
    public async Task HostileServerIdIsRejectedBeforeAnyOoklaProcessStarts()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/tests",
            new { providerId = "ookla", serverId = "123;touch /tmp/pwned" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", problem.GetProperty("code").GetString());
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("malformed", "ookla_invalid_output")]
    [InlineData("timeout", "ookla_timeout")]
    [InlineData("nonzero", "ookla_failed")]
    public async Task ProviderFailuresBecomeSanitizedTerminalJobs(string mode, string expectedCode)
    {
        var runner = StandardRunner();
        runner.Handler = (request, _) =>
        {
            if (request.ArgumentList.Contains("--version"))
            {
                return Task.FromResult(RecordingProcessRunner.Result(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n"));
            }

            return Task.FromResult(mode switch
            {
                "malformed" => RecordingProcessRunner.Result(stdout: "not-json"),
                "timeout" => RecordingProcessRunner.Result(exitCode: null, reason: ProcessTerminationReason.TimedOut),
                _ => RecordingProcessRunner.Result(stderr: "sensitive provider details", exitCode: 9)
            });
        };
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var failed = await WaitForTerminalAsync(client, await CreateAsync(client, null));

        Assert.Equal("failed", failed.Status);
        Assert.Equal(expectedCode, failed.Failure?.Code);
        Assert.DoesNotContain("sensitive", failed.Failure?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SseDeliversFinalOoklaResult()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = StandardRunner();
        runner.Handler = async (request, token) =>
        {
            if (request.ArgumentList.Contains("--version"))
            {
                return RecordingProcessRunner.Result(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n");
            }

            if (request.ArgumentList.Contains("--format=json"))
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                return RecordingProcessRunner.Result(stdout: OoklaTestFactory.Fixture("result-complete.json"));
            }

            return RecordingProcessRunner.Result(stdout: OoklaTestFactory.Fixture("servers-normal.txt"));
        };
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var created = await CreateAsync(client, null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var streamResponse = await client.GetAsync(created.EventsUrl, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        var first = await ReadEventAsync(reader);
        release.TrySetResult();
        SseRecord terminal;
        do
        {
            terminal = await ReadEventAsync(reader);
        }
        while (terminal.Event != "result");

        Assert.Equal("snapshot", first.Event);
        Assert.Equal("result", terminal.Event);
        Assert.Equal("completed", terminal.Data.GetProperty("job").GetProperty("status").GetString());
        Assert.Equal(1000m, terminal.Data.GetProperty("job").GetProperty("result").GetProperty("downloadMbps").GetDecimal());
    }

    [Fact]
    public async Task ApiCancellationReachesOoklaProviderAndEndsCancelled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = StandardRunner();
        runner.Handler = async (request, token) =>
        {
            if (request.ArgumentList.Contains("--version"))
            {
                return RecordingProcessRunner.Result(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n");
            }

            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                observedCancellation.TrySetResult();
                return RecordingProcessRunner.Result(exitCode: null, reason: ProcessTerminationReason.Cancelled);
            }

            throw new InvalidOperationException();
        };
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var created = await CreateAsync(client, null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var response = await client.PostAsync($"/api/tests/{created.Id}/cancel", null);
        await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var job = await client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("cancelled", job?.Status);
    }

    private WebApplicationFactory<Program> CreateFactory(RecordingProcessRunner runner)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Providers:Ookla:Enabled", "true");
            builder.UseSetting("Providers:Ookla:AcceptLicense", "true");
            builder.UseSetting("Providers:Ookla:AcceptGdpr", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcessRunner>();
                services.AddSingleton<IProcessRunner>(runner);
                services.RemoveAll<INetworkIdentityService>();
                services.AddSingleton<INetworkIdentityService>(new FakeNetworkIdentityService());
            });
        });
    }

    private static RecordingProcessRunner StandardRunner() => new()
    {
        Handler = (request, _) => Task.FromResult(request.ArgumentList switch
        {
            var arguments when arguments.Contains("--version") => RecordingProcessRunner.Result(
                stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n"),
            var arguments when arguments.Contains("--servers") => RecordingProcessRunner.Result(
                stdout: OoklaTestFactory.Fixture("servers-normal.txt")),
            _ => RecordingProcessRunner.Result(stdout: OoklaTestFactory.Fixture("result-complete.json"))
        })
    };

    private static async Task<CreateTestResponse> CreateAsync(HttpClient client, string? serverId)
    {
        using var response = await client.PostAsJsonAsync("/api/tests", new { providerId = "ookla", serverId });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateTestResponse>())!;
    }

    private static async Task<SpeedTestJobResponse> WaitForTerminalAsync(HttpClient client, CreateTestResponse created)
    {
        SpeedTestJobResponse? job = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            job = await client.GetFromJsonAsync<SpeedTestJobResponse>(created.ResourceUrl, timeout.Token);
            if (job is not null && job.Status is "completed" or "failed" or "cancelled")
            {
                return job;
            }

            await Task.Delay(10, timeout.Token);
        }

        throw new TimeoutException($"Job {created.Id} did not reach a terminal state.");
    }

    private static async Task<SseRecord> ReadEventAsync(StreamReader reader)
    {
        string? eventType = null;
        string? id = null;
        string? data = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.Length == 0 && data is not null)
            {
                return new SseRecord(eventType, id, JsonDocument.Parse(data).RootElement.Clone());
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

        throw new EndOfStreamException();
    }

    private sealed record SseRecord(string? Event, string? Id, JsonElement Data);
}
