using Microsoft.Extensions.Logging.Abstractions;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.MLab;

namespace SpeedtestDashboard.Api.Tests.MLab;

internal sealed class MLabRecordingProcessRunner : IProcessRunner
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

internal static class MLabTestFactory
{
    public const string HelpOutput = "Usage of mlab-ndt7-client:\n  -client-name string\n  -format string\n  -quiet\n";

    public static MLabOptions Options() => new()
    {
        ExecutablePath = "/opt/fixture/mlab-ndt7-client",
        Enabled = true,
        HealthTimeoutSeconds = 5,
        HealthCacheSeconds = 45,
        TestTimeoutSeconds = 75,
        ClientTimeoutSeconds = 55
    };

    public static MLabSpeedTestProvider Provider(
        MLabRecordingProcessRunner runner,
        MLabOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        options ??= Options();
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options);
        return new MLabSpeedTestProvider(
            runner,
            new MLabCommandFactory(optionsWrapper),
            new MLabResultParser(),
            optionsWrapper,
            timeProvider ?? TimeProvider.System,
            NullLogger<MLabSpeedTestProvider>.Instance);
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "mlab",
        name));
}
