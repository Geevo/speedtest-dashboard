using System.Globalization;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.MLab;

public sealed class MLabCommandFactory(IOptions<MLabOptions> options)
{
    private const int HealthOutputLimitBytes = 32 * 1024;
    private const int ResultOutputLimitBytes = 128 * 1024;
    private const int ErrorOutputLimitBytes = 128 * 1024;

    public ProcessRequest CreateHealthCommand() => Create(
        ["-help"],
        TimeSpan.FromSeconds(options.Value.HealthTimeoutSeconds),
        HealthOutputLimitBytes);

    public ProcessRequest CreateTestCommand() => Create(
        [
            "-format=json",
            "-quiet",
            "-client-name=speedtest-dashboard",
            $"-timeout={options.Value.ClientTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}s",
            "-download=true",
            "-upload=true"
        ],
        TimeSpan.FromSeconds(options.Value.TestTimeoutSeconds),
        ResultOutputLimitBytes);

    private ProcessRequest Create(IReadOnlyList<string> arguments, TimeSpan timeout, int stdoutLimit) => new(
        options.Value.ExecutablePath,
        arguments,
        timeout,
        WorkingDirectory: "/tmp",
        MaximumStandardOutputBytes: stdoutLimit,
        MaximumStandardErrorBytes: ErrorOutputLimitBytes,
        EnvironmentVariables: new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = "/tmp",
            ["XDG_CONFIG_HOME"] = "/tmp"
        });
}
