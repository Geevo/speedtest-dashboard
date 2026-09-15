using System.Globalization;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public sealed class FastComCommandFactory(IOptions<FastComOptions> options)
{
    private const int VersionOutputLimitBytes = 16 * 1024;
    private const int ResultOutputLimitBytes = 64 * 1024;
    private const int ErrorOutputLimitBytes = 128 * 1024;

    public ProcessRequest CreateVersionCommand() => Create(
        ["--help"],
        TimeSpan.FromSeconds(options.Value.HealthTimeoutSeconds),
        VersionOutputLimitBytes);

    public ProcessRequest CreateTestCommand() => Create(
        [
            "--https",
            "--upload",
            "--json",
            "--duration",
            options.Value.DurationSeconds.ToString(CultureInfo.InvariantCulture)
        ],
        TimeSpan.FromSeconds(options.Value.TestTimeoutSeconds),
        ResultOutputLimitBytes);

    private ProcessRequest Create(IReadOnlyList<string> arguments, TimeSpan timeout, int stdoutLimit) => new(
        options.Value.ExecutablePath,
        arguments,
        timeout,
        WorkingDirectory: Path.GetTempPath(),
        MaximumStandardOutputBytes: stdoutLimit,
        MaximumStandardErrorBytes: ErrorOutputLimitBytes,
        EnvironmentVariables: new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = Path.GetTempPath(),
            ["XDG_CONFIG_HOME"] = Path.GetTempPath()
        });
}
