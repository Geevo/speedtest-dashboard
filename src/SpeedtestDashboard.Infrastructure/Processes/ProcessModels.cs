namespace SpeedtestDashboard.Infrastructure.Processes;

public enum ProcessTerminationReason
{
    Exited,
    TimedOut,
    Cancelled,
    OutputLimitExceeded,
    FailedToStart
}

public sealed record ProcessRequest(
    string Executable,
    IReadOnlyList<string> ArgumentList,
    TimeSpan? Timeout = null,
    string? WorkingDirectory = null,
    int? MaximumStandardOutputBytes = null,
    int? MaximumStandardErrorBytes = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null);

public sealed record ProcessResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    ProcessTerminationReason TerminationReason);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed class ProcessOptions
{
    public const string SectionName = "Processes";

    public int DefaultTimeoutSeconds { get; set; } = 120;

    public int MaxTimeoutSeconds { get; set; } = 600;

    public int DefaultStdoutLimitBytes { get; set; } = 2 * 1024 * 1024;

    public int DefaultStderrLimitBytes { get; set; } = 1024 * 1024;

    public int AbsoluteOutputLimitBytes { get; set; } = 8 * 1024 * 1024;
}
