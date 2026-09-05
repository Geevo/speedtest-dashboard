using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Processes;

public sealed class ProcessRunner(
    IOptions<ProcessOptions> options,
    TimeProvider timeProvider,
    ILogger<ProcessRunner> logger) : IProcessRunner
{
    private static readonly HashSet<string> ProhibitedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "sh",
        "bash",
        "cmd",
        "powershell",
        "pwsh"
    };

    private static readonly Encoding SafeUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var validated = Validate(request);
        var startedAtUtc = timeProvider.GetUtcNow();
        using var process = new Process { StartInfo = CreateStartInfo(validated) };

        try
        {
            if (!process.Start())
            {
                return FailedToStart(startedAtUtc);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(
                "Process {ExecutableName} failed to start.",
                Path.GetFileName(validated.Executable));
            return FailedToStart(startedAtUtc);
        }

        using var timeout = new CancellationTokenSource(validated.Timeout);
        using var outputLimit = new CancellationTokenSource();
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token,
            outputLimit.Token);

        var stdoutTask = ReadBoundedAsync(
            process.StandardOutput.BaseStream,
            validated.MaximumStandardOutputBytes,
            outputLimit);
        var stderrTask = ReadBoundedAsync(
            process.StandardError.BaseStream,
            validated.MaximumStandardErrorBytes,
            outputLimit);

        var terminationReason = ProcessTerminationReason.Exited;
        try
        {
            await process.WaitForExitAsync(execution.Token);
        }
        catch (OperationCanceledException)
        {
            terminationReason = cancellationToken.IsCancellationRequested
                ? ProcessTerminationReason.Cancelled
                : timeout.IsCancellationRequested
                    ? ProcessTerminationReason.TimedOut
                    : ProcessTerminationReason.OutputLimitExceeded;
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
        }

        var stdout = await AwaitCaptureAsync(stdoutTask);
        var stderr = await AwaitCaptureAsync(stderrTask);
        if (terminationReason == ProcessTerminationReason.Exited && (stdout.LimitExceeded || stderr.LimitExceeded))
        {
            terminationReason = ProcessTerminationReason.OutputLimitExceeded;
        }

        var completedAtUtc = timeProvider.GetUtcNow();
        var result = new ProcessResult(
            process.HasExited ? process.ExitCode : null,
            stdout.Text,
            stderr.Text,
            startedAtUtc,
            completedAtUtc,
            terminationReason);

        logger.LogInformation(
            "Process {ExecutableName} completed in {DurationMilliseconds} ms with reason {TerminationReason}, exit code {ExitCode}, stdout bytes {StandardOutputBytes}, and stderr bytes {StandardErrorBytes}.",
            Path.GetFileName(validated.Executable),
            (completedAtUtc - startedAtUtc).TotalMilliseconds,
            result.TerminationReason,
            result.ExitCode,
            stdout.BytesCaptured,
            stderr.BytesCaptured);

        return result;
    }

    internal static ProcessStartInfo CreateStartInfo(ValidatedProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = SafeUtf8,
            StandardErrorEncoding = SafeUtf8,
            CreateNoWindow = true
        };

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.ArgumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.EnvironmentVariables)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    private ValidatedProcessRequest Validate(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Executable) || request.Executable.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException("A valid executable name is required.", nameof(request));
        }

        var executableName = Path.GetFileNameWithoutExtension(request.Executable);
        if (ProhibitedExecutables.Contains(executableName))
        {
            throw new ArgumentException("Shell executables are not permitted.", nameof(request));
        }

        if (request.ArgumentList.Any(argument => argument is null || argument.IndexOf('\0') >= 0))
        {
            throw new ArgumentException("Process arguments cannot be null or contain null characters.", nameof(request));
        }

        if (request.WorkingDirectory is not null && !Directory.Exists(request.WorkingDirectory))
        {
            throw new ArgumentException("The process working directory does not exist.", nameof(request));
        }


        var environmentVariables = request.EnvironmentVariables is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(request.EnvironmentVariables, StringComparer.Ordinal);
        if (environmentVariables.Any(variable =>
                string.IsNullOrWhiteSpace(variable.Key) ||
                variable.Key.Contains('=') ||
                variable.Key.Any(char.IsControl) ||
                variable.Value?.Contains('\0') is true))
        {
            throw new ArgumentException("Process environment overrides contain an invalid name or value.", nameof(request));
        }

        var timeout = request.Timeout ?? TimeSpan.FromSeconds(options.Value.DefaultTimeoutSeconds);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(options.Value.MaxTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Process timeout is outside the permitted range.");
        }

        var stdoutLimit = request.MaximumStandardOutputBytes ?? options.Value.DefaultStdoutLimitBytes;
        var stderrLimit = request.MaximumStandardErrorBytes ?? options.Value.DefaultStderrLimitBytes;
        ValidateOutputLimit(stdoutLimit, nameof(request.MaximumStandardOutputBytes));
        ValidateOutputLimit(stderrLimit, nameof(request.MaximumStandardErrorBytes));

        return new ValidatedProcessRequest(
            request.Executable,
            request.ArgumentList.ToArray(),
            timeout,
            request.WorkingDirectory,
            stdoutLimit,
            stderrLimit,
            environmentVariables);
    }

    private void ValidateOutputLimit(int limit, string parameterName)
    {
        if (limit <= 0 || limit > options.Value.AbsoluteOutputLimitBytes)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Process output limit is outside the permitted range.");
        }
    }

    private ProcessResult FailedToStart(DateTimeOffset startedAtUtc) => new(
        ExitCode: null,
        StandardOutput: string.Empty,
        StandardError: string.Empty,
        startedAtUtc,
        timeProvider.GetUtcNow(),
        ProcessTerminationReason.FailedToStart);

    private static async Task<CaptureResult> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationTokenSource outputLimit)
    {
        var buffer = new byte[8192];
        using var capture = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var exceeded = false;

        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                var remaining = maximumBytes - (int)capture.Length;
                if (read > remaining)
                {
                    if (remaining > 0)
                    {
                        capture.Write(buffer, 0, remaining);
                    }

                    exceeded = true;
                    outputLimit.Cancel();
                    break;
                }

                capture.Write(buffer, 0, read);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Process termination can close redirected pipes while a read is pending.
        }

        return new CaptureResult(
            SafeUtf8.GetString(capture.GetBuffer(), 0, (int)capture.Length),
            (int)capture.Length,
            exceeded);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process may have exited between the state check and Kill.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException)
        {
            // Cleanup is bounded and best-effort; disposing Process closes local handles.
        }
    }

    private static async Task<CaptureResult> AwaitCaptureAsync(Task<CaptureResult> captureTask)
    {
        try
        {
            return await captureTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            return new CaptureResult(string.Empty, 0, LimitExceeded: false);
        }
    }

    internal sealed record ValidatedProcessRequest(
        string Executable,
        IReadOnlyList<string> ArgumentList,
        TimeSpan Timeout,
        string? WorkingDirectory,
        int MaximumStandardOutputBytes,
        int MaximumStandardErrorBytes,
        IReadOnlyDictionary<string, string?> EnvironmentVariables);

    private sealed record CaptureResult(string Text, int BytesCaptured, bool LimitExceeded);
}
