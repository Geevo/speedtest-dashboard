using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;

namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public sealed class OoklaCommandFactory(IOptions<OoklaOptions> options)
{
    private const int VersionOutputLimitBytes = 16 * 1024;
    private const int ServerOutputLimitBytes = 1024 * 1024;
    private const int ErrorOutputLimitBytes = 128 * 1024;
    private const int ResultOutputLimitBytes = 2 * 1024 * 1024;

    public ProcessRequest CreateVersionCommand() => Create(
        ["--version"],
        TimeSpan.FromSeconds(options.Value.HealthTimeoutSeconds),
        VersionOutputLimitBytes);

    public ProcessRequest CreateServerListCommand()
    {
        var arguments = AcceptanceArguments();
        arguments.Add("--servers");
        return Create(
            arguments,
            TimeSpan.FromSeconds(options.Value.ServerListTimeoutSeconds),
            ServerOutputLimitBytes);
    }

    public ProcessRequest CreateTestCommand(string? serverId)
    {
        if (serverId is not null && !OoklaServerId.IsValid(serverId))
        {
            throw new ArgumentException("Ookla server IDs must contain 1 to 10 decimal digits and cannot start with zero.", nameof(serverId));
        }

        var arguments = AcceptanceArguments();
        arguments.Add("--format=json");
        arguments.Add("--progress=no");
        if (serverId is not null)
        {
            arguments.Add($"--server-id={serverId}");
        }

        return Create(
            arguments,
            TimeSpan.FromSeconds(options.Value.TestTimeoutSeconds),
            ResultOutputLimitBytes);
    }

    private List<string> AcceptanceArguments()
    {
        var arguments = new List<string>();
        if (options.Value.AcceptLicense)
        {
            arguments.Add("--accept-license");
        }

        if (options.Value.AcceptGdpr)
        {
            arguments.Add("--accept-gdpr");
        }

        return arguments;
    }

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
