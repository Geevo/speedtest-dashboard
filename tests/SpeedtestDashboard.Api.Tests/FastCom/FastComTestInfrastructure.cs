using Microsoft.Extensions.Logging.Abstractions;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.FastCom;

namespace SpeedtestDashboard.Api.Tests.FastCom;

internal sealed class FastComRecordingProcessRunner : IProcessRunner
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

internal static class FastComTestFactory
{
    public static FastComOptions Options() => new()
    {
        ExecutablePath = "/opt/fixture/fast-cli",
        Enabled = true,
        HealthTimeoutSeconds = 5,
        HealthCacheSeconds = 45,
        TestTimeoutSeconds = 90,
        DurationSeconds = 30
    };

    public static FastComSpeedTestProvider Provider(
        FastComRecordingProcessRunner runner,
        FastComOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        options ??= Options();
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options);
        return new FastComSpeedTestProvider(
            runner,
            new FastComCommandFactory(optionsWrapper),
            new FastComResultParser(),
            optionsWrapper,
            timeProvider ?? TimeProvider.System,
            NullLogger<FastComSpeedTestProvider>.Instance);
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "fastcom",
        name));
}
