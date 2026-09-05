using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.Ookla;

namespace SpeedtestDashboard.Api.Tests.Ookla;

internal sealed class RecordingProcessRunner : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public Func<ProcessRequest, CancellationToken, Task<ProcessResult>> Handler { get; set; } =
        (request, _) => Task.FromResult(Result(stdout: string.Empty));

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

internal static class OoklaTestFactory
{
    public static OoklaOptions Options(bool accepted = true) => new()
    {
        ExecutablePath = "/opt/fixture/speedtest",
        AcceptLicense = accepted,
        AcceptGdpr = accepted,
        HealthTimeoutSeconds = 5,
        HealthCacheSeconds = 45,
        TestTimeoutSeconds = 180,
        ServerListTimeoutSeconds = 30,
        ServerCacheSeconds = 300,
        MaximumServers = 100
    };

    public static OoklaSpeedTestProvider Provider(
        RecordingProcessRunner runner,
        OoklaOptions? options = null,
        TimeProvider? timeProvider = null) => new(
            runner,
            new OoklaCommandFactory(Microsoft.Extensions.Options.Options.Create(options ?? Options())),
            new OoklaServerListParser(),
            new OoklaResultParser(),
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            timeProvider ?? TimeProvider.System,
            NullLogger<OoklaSpeedTestProvider>.Instance);

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "ookla",
        name));
}
