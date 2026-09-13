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

namespace SpeedtestDashboard.Api.Tests.MLab;

public sealed class MLabEndpointIntegrationTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _baseFactory;

    public MLabEndpointIntegrationTests(DashboardWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task ProviderEndpointExposesCapabilitiesWithoutServerDiscovery()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        var provider = await client.GetFromJsonAsync<JsonElement>("/api/providers/mlab");
        using var servers = await client.GetAsync("/api/providers/mlab/servers");

        Assert.Equal("available", provider.GetProperty("healthState").GetString());
        Assert.Equal("0.10.1", provider.GetProperty("version").GetString());
        Assert.Equal(
            ["download", "upload", "latency"],
            provider.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
        var disclosure = Assert.Single(provider.GetProperty("disclosures").EnumerateArray());
        Assert.Equal("privacy", disclosure.GetProperty("kind").GetString());
        Assert.Equal("https://www.measurementlab.net/privacy/", disclosure.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.Conflict, servers.StatusCode);
    }

    [Fact]
    public async Task AutomaticTestCompletesThroughWorkerUsingOfficialContract()
    {
        var runner = StandardRunner();
        using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/tests", new { providerId = "mlab" });
        var created = await response.Content.ReadFromJsonAsync<CreateTestResponse>();
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        SpeedTestJobResponse? completed = null;
        await TestWait.UntilAsync(() =>
        {
            completed = client.GetFromJsonAsync<SpeedTestJobResponse>(created!.ResourceUrl).GetAwaiter().GetResult();
            return completed?.Status == "completed";
        });

        Assert.Equal(507.25m, completed!.Result?.DownloadMbps);
        Assert.Equal(314.4m, completed.Result?.UploadMbps);
        Assert.Equal(8.75m, completed.Result?.LatencyMilliseconds);
        var command = Assert.Single(runner.Requests, request => request.ArgumentList.Contains("-format=json"));
        Assert.Equal(
            ["-format=json", "-quiet", "-client-name=speedtest-dashboard", "-timeout=55s", "-download=true", "-upload=true"],
            command.ArgumentList);
    }

    private WebApplicationFactory<Program> CreateFactory(MLabRecordingProcessRunner runner) =>
        _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Providers:MLab:Enabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProcessRunner>();
                services.AddSingleton<IProcessRunner>(runner);
            });
        });

    private static MLabRecordingProcessRunner StandardRunner() => new()
    {
        Handler = (request, _) => Task.FromResult(request.ArgumentList.Contains("-help")
            ? MLabRecordingProcessRunner.Result(stderr: MLabTestFactory.HelpOutput)
            : MLabRecordingProcessRunner.Result(stdout: MLabTestFactory.Fixture("result-complete.json")))
    };
}
