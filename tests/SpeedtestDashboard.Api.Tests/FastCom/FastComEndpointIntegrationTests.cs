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
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Api.Tests.FastCom;

public sealed class FastComEndpointIntegrationTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _baseFactory;

    public FastComEndpointIntegrationTests(DashboardWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task ProviderEndpointExposesCapabilitiesWithoutServerDiscovery()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var provider = await client.GetFromJsonAsync<JsonElement>("/api/providers/fastcom");
        using var servers = await client.GetAsync("/api/providers/fastcom/servers");

        Assert.Equal("available", provider.GetProperty("healthState").GetString());
        Assert.Equal("0.3.5", provider.GetProperty("version").GetString());
        Assert.Equal(
            ["download", "upload", "latency"],
            provider.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, servers.StatusCode);
    }

    [Fact]
    public async Task AutomaticTestCompletesThroughWorkerUsingUpstreamContract()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/tests", new { providerId = "fastcom" });
        var created = await response.Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        SpeedTestJobResponse? completed = null;
        await TestWait.UntilAsync(() =>
        {
            completed = client.GetFromJsonAsync<SpeedTestJobResponse>(created!.ResourceUrl).GetAwaiter().GetResult();
            return completed?.Status == "completed";
        });

        Assert.Equal(131m, completed!.Result?.DownloadMbps);
        Assert.Equal(42m, completed.Result?.UploadMbps);
        Assert.Equal(20.8m, completed.Result?.LatencyMilliseconds);
        var command = Assert.Single(runner.Requests, request => request.ArgumentList.Contains("--json"));
        Assert.Equal(["--https", "--upload", "--json", "--duration", "30"], command.ArgumentList);
    }

    [Fact]
    public async Task UpstreamJsonErrorWithSuccessfulExitBecomesSanitizedFailedJob()
    {
        var runner = StandardRunner();
        runner.Handler = (request, _) => Task.FromResult(request.ArgumentList.Contains("--help")
            ? HelpResult()
            : FastComRecordingProcessRunner.Result(stdout: FastComTestFactory.Fixture("result-error.json")));
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/tests", new { providerId = "fastcom" });
        var created = await response.Content.ReadFromJsonAsync<CreateTestResponse>();
        SpeedTestJobResponse? failed = null;
        await TestWait.UntilAsync(() =>
        {
            failed = client.GetFromJsonAsync<SpeedTestJobResponse>(created!.ResourceUrl).GetAwaiter().GetResult();
            return failed?.Status == "failed";
        });

        Assert.Equal("fastcom_network_unavailable", failed!.Failure?.Code);
        Assert.DoesNotContain("contact fast.com", failed.Failure?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private WebApplicationFactory<Program> CreateFactory(FastComRecordingProcessRunner runner) =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Providers:FastCom:Enabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcessRunner>();
                services.AddSingleton<IProcessRunner>(runner);
            });
        });

    private static FastComRecordingProcessRunner StandardRunner() => new()
    {
        Handler = (request, _) => Task.FromResult(request.ArgumentList.Contains("--help")
            ? HelpResult()
            : FastComRecordingProcessRunner.Result(stdout: FastComTestFactory.Fixture("result-complete.json")))
    };

    private static ProcessResult HelpResult() => FastComRecordingProcessRunner.Result(
        stderr: "\u001b[1mfast-cli\u001b[0m v0.3.5 - Estimate connection speed using fast.com\n");
}
