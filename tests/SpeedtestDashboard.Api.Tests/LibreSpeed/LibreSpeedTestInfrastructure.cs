using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

internal sealed class LibreSpeedRecordingProcessRunner : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public Func<ProcessRequest, CancellationToken, Task<ProcessResult>> Handler { get; set; } =
        (_, _) => Task.FromResult(Result());

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Handler(request, cancellationToken);
    }

    public static ProcessResult Result(
        string stdout = "",
        string stderr = "",
        int? exitCode = 0,
        ProcessTerminationReason reason = ProcessTerminationReason.Exited) => new(
            exitCode,
            stdout,
            stderr,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            reason);
}

internal static class LibreSpeedTestFactory
{
    public static LibreSpeedOptions Options() => new()
    {
        ExecutablePath = "/opt/fixture/librespeed-cli",
        Enabled = true,
        HealthTimeoutSeconds = 5,
        HealthCacheSeconds = 45,
        TestTimeoutSeconds = 180,
        ServerListTimeoutSeconds = 20,
        ServerCacheSeconds = 300,
        MaximumServers = 250,
        DisableIcmp = true,
        PreferHttps = true
    };

    public static LibreSpeedSpeedTestProvider Provider(
        LibreSpeedRecordingProcessRunner runner,
        LibreSpeedOptions? options = null,
        string? catalog = null,
        TimeProvider? timeProvider = null)
    {
        options ??= Options();
        var httpClient = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(catalog ?? Fixture("servers-normal.json"))
        })))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options);
        var parser = new LibreSpeedServerCatalogParser();
        var catalogClient = new LibreSpeedServerCatalogClient(
            httpClient,
            parser,
            optionsWrapper,
            NullLogger<LibreSpeedServerCatalogClient>.Instance);
        return new LibreSpeedSpeedTestProvider(
            runner,
            new LibreSpeedCommandFactory(optionsWrapper),
            catalogClient,
            new LibreSpeedResultParser(),
            optionsWrapper,
            timeProvider ?? TimeProvider.System,
            NullLogger<LibreSpeedSpeedTestProvider>.Instance);
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "librespeed",
        name));

    internal sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
