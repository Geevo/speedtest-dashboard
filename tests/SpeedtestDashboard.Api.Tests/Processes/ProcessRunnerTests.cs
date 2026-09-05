using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.ProcessFixture;

namespace SpeedtestDashboard.Api.Tests.Processes;

public sealed class ProcessRunnerTests
{
    [Fact]
    public void StartInfo_UsesRedirectedStreamsAndLiteralArgumentList()
    {
        var arguments = new[] { "echo-args", "foo; echo injected", "value with spaces" };
        var request = new ProcessRunner.ValidatedProcessRequest(
            FixturePath,
            arguments,
            TimeSpan.FromSeconds(5),
            WorkingDirectory: null,
            MaximumStandardOutputBytes: 1024,
            MaximumStandardErrorBytes: 1024,
            EnvironmentVariables: new Dictionary<string, string?>());

        var startInfo = ProcessRunner.CreateStartInfo(request);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Empty(startInfo.Arguments);
        Assert.Equal(arguments, startInfo.ArgumentList);
    }

    [Fact]
    public async Task ArgumentsIncludingShellMetacharacters_ArriveLiterally()
    {
        var expected = new[] { "foo; echo injected", "$(touch nope)", "space value", "\"quoted\"" };

        var result = await RunFixtureAsync(["echo-args", .. expected]);
        var actual = JsonSerializer.Deserialize<string[]>(result.StandardOutput);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task StandardOutputAndError_AreCapturedConcurrently()
    {
        var result = await RunFixtureAsync(["write-both", "200000"]);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(200000, result.StandardOutput.Length);
        Assert.Equal(200000, result.StandardError.Length);
    }

    [Fact]
    public async Task NonZeroExitCode_RemainsANormalExit()
    {
        var result = await RunFixtureAsync(["exit", "17"]);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Equal(17, result.ExitCode);
    }

    [Fact]
    public async Task Timeout_TerminatesTheProcess()
    {
        var result = await RunFixtureAsync(["sleep", "30000"], timeout: TimeSpan.FromMilliseconds(80));

        Assert.Equal(ProcessTerminationReason.TimedOut, result.TerminationReason);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
    }

    [Fact]
    public async Task Timeout_TerminatesTheEntireProcessTree()
    {
        var result = await RunFixtureAsync(["spawn-child"], timeout: TimeSpan.FromMilliseconds(250));

        Assert.Equal(ProcessTerminationReason.TimedOut, result.TerminationReason);
        Assert.True(int.TryParse(result.StandardOutput.Trim(), out var childProcessId));
        await AssertProcessExitedAsync(childProcessId);
    }

    [Fact]
    public async Task CallerCancellation_TerminatesTheProcessWithDistinctReason()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var result = await RunFixtureAsync(
            ["sleep", "30000"],
            timeout: TimeSpan.FromSeconds(5),
            cancellationToken: cancellation.Token);

        Assert.Equal(ProcessTerminationReason.Cancelled, result.TerminationReason);
    }

    [Fact]
    public async Task OutputLimit_TerminatesProcessAndRetainsOnlyBoundedBytes()
    {
        var result = await RunFixtureAsync(
            ["write-stdout", "100000"],
            stdoutLimit: 1024);

        Assert.Equal(ProcessTerminationReason.OutputLimitExceeded, result.TerminationReason);
        Assert.Equal(1024, result.StandardOutput.Length);
    }

    [Fact]
    public async Task WorkingDirectory_IsHonored()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"speedtest-dashboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        try
        {
            var result = await RunFixtureAsync(["working-directory"], workingDirectory: workingDirectory);

            Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
            Assert.Equal(Path.GetFullPath(workingDirectory), Path.GetFullPath(result.StandardOutput));
        }
        finally
        {
            Directory.Delete(workingDirectory);
        }
    }

    [Fact]
    public async Task MissingExecutable_ReturnsFailedToStartWithoutExceptionDetails()
    {
        var runner = CreateRunner();
        var request = new ProcessRequest(
            $"definitely-not-a-real-executable-{Guid.NewGuid():N}",
            [],
            TimeSpan.FromSeconds(1));

        var result = await runner.RunAsync(request, CancellationToken.None);

        Assert.Equal(ProcessTerminationReason.FailedToStart, result.TerminationReason);
        Assert.Null(result.ExitCode);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task MalformedUtf8_IsReplacedWithoutCrashing()
    {
        var result = await RunFixtureAsync(["invalid-utf8"]);

        Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
        Assert.Contains('\ufffd', result.StandardOutput);
    }

    [Theory]
    [InlineData("/bin/sh")]
    [InlineData("bash")]
    [InlineData("cmd.exe")]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ShellExecutables_AreRejected(string executable)
    {
        var runner = CreateRunner();

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(
            new ProcessRequest(executable, [], TimeSpan.FromSeconds(1)),
            CancellationToken.None));
    }

    [Fact]
    public async Task InfiniteAndExcessiveTimeouts_AreRejected()
    {
        var runner = CreateRunner();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            new ProcessRequest(FixturePath, ["sleep", "1"], Timeout.InfiniteTimeSpan),
            CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            new ProcessRequest(FixturePath, ["sleep", "1"], TimeSpan.FromMinutes(11)),
            CancellationToken.None));
    }

    private static string FixturePath
    {
        get
        {
            var directory = Path.GetDirectoryName(typeof(FixtureMarker).Assembly.Location)!;
            var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "SpeedtestDashboard.ProcessFixture.exe"
                : "SpeedtestDashboard.ProcessFixture";
            return Path.Combine(directory, executable);
        }
    }

    private static Task<ProcessResult> RunFixtureAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        string? workingDirectory = null,
        int? stdoutLimit = null,
        CancellationToken cancellationToken = default)
    {
        return CreateRunner().RunAsync(
            new ProcessRequest(
                FixturePath,
                arguments,
                timeout ?? TimeSpan.FromSeconds(5),
                workingDirectory,
                stdoutLimit,
                MaximumStandardErrorBytes: 1024 * 1024),
            cancellationToken);
    }

    private static ProcessRunner CreateRunner() => new(
        Options.Create(new ProcessOptions()),
        TimeProvider.System,
        NullLogger<ProcessRunner>.Instance);

    private static async Task AssertProcessExitedAsync(int processId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(20, timeout.Token);
        }

        Assert.Fail($"Child process {processId} was still running after the process tree was terminated.");
    }
}
