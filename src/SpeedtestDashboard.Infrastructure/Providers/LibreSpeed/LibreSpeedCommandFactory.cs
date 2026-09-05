using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public sealed class LibreSpeedCommandFactory(IOptions<LibreSpeedOptions> options)
{
    private const int VersionOutputLimitBytes = 16 * 1024;
    private const int ErrorOutputLimitBytes = 128 * 1024;
    private const int ResultOutputLimitBytes = 2 * 1024 * 1024;

    public ProcessRequest CreateVersionCommand() => Create(
        ["--version"],
        TimeSpan.FromSeconds(options.Value.HealthTimeoutSeconds),
        VersionOutputLimitBytes);

    public ProcessRequest CreateTestCommand(string? serverId)
    {
        if (serverId is not null && !LibreSpeedServerId.IsValid(serverId))
        {
            throw new ArgumentException(
                "LibreSpeed server IDs must be positive 32-bit integers without leading zeroes.",
                nameof(serverId));
        }

        var arguments = new List<string> { "--json" };
        if (options.Value.DisableIcmp)
        {
            arguments.Add("--no-icmp");
        }

        if (options.Value.PreferHttps)
        {
            arguments.Add("--secure");
        }

        if (serverId is not null)
        {
            arguments.Add("--server");
            arguments.Add(serverId);
        }

        return Create(
            arguments,
            TimeSpan.FromSeconds(options.Value.TestTimeoutSeconds),
            ResultOutputLimitBytes);
    }

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
