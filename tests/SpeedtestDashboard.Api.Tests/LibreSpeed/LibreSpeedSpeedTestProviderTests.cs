using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

public sealed class LibreSpeedSpeedTestProviderTests
{
    [Fact]
    public async Task Health_ReportsPinnedVersionAndCachesProbe()
    {
        var runner = new LibreSpeedRecordingProcessRunner
        {
            Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(
                stdout: "librespeed-cli v1.0.13 (built on fixture)\nhttps://github.com/librespeed/speedtest-cli\nLicensed under GNU Lesser General Public License v3.0\n"))
        };
        var provider = LibreSpeedTestFactory.Provider(runner);

        var first = await provider.CheckHealthAsync(CancellationToken.None);
        var second = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Available, first.State);
        Assert.Equal("1.0.13", first.Version);
        Assert.Equal("Ready", first.Message);
        Assert.Same(first, second);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task Health_DisabledDoesNotStartAProcess()
    {
        var runner = VersionRunner();
        var options = LibreSpeedTestFactory.Options();
        options.Enabled = false;

        var health = await LibreSpeedTestFactory.Provider(runner, options).CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.Contains("disabled", health.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.FailedToStart, "not installed")]
    [InlineData(ProcessTerminationReason.TimedOut, "timed out")]
    public async Task Health_MapsUnavailableProcessStates(ProcessTerminationReason reason, string message)
    {
        var runner = new LibreSpeedRecordingProcessRunner
        {
            Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(exitCode: null, reason: reason))
        };

        var health = await LibreSpeedTestFactory.Provider(runner).CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.Contains(message, health.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unexpected", null)]
    [InlineData("librespeed-cli v1.0.12 (built on fixture)\n", "1.0.12")]
    public async Task Health_MalformedOrDifferentVersionIsDegraded(string output, string? expectedVersion)
    {
        var runner = new LibreSpeedRecordingProcessRunner
        {
            Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(stdout: output))
        };

        var health = await LibreSpeedTestFactory.Provider(runner).CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Degraded, health.State);
        Assert.Equal(expectedVersion, health.Version);
    }

    [Fact]
    public async Task ServerDiscovery_IsCachedSearchableAndNormalized()
    {
        var provider = LibreSpeedTestFactory.Provider(VersionRunner());

        var first = await provider.GetServersAsync(new ServerQuery("Tōkyō", 100), CancellationToken.None);
        var second = await provider.GetServersAsync(new ServerQuery("82", 100), CancellationToken.None);

        Assert.Equal("82", Assert.Single(first).Id);
        Assert.Equal("82", Assert.Single(second).Id);
    }

    [Fact]
    public void RequestValidation_RejectsHostileServerId()
    {
        var provider = LibreSpeedTestFactory.Provider(VersionRunner());

        var hostile = provider.ValidateRequest(new SpeedTestRequest(
            ProviderId.LibreSpeed,
            "49;touch /tmp/pwned"));

        Assert.False(hostile.IsValid);
    }

    [Fact]
    public async Task AutomaticRun_MapsResultAndResolvesActualServer()
    {
        var runner = StandardRunner();
        var provider = LibreSpeedTestFactory.Provider(runner);

        var result = await provider.RunAsync(Execution(null), CancellationToken.None);

        Assert.Equal(934.25m, result.DownloadMbps);
        Assert.Equal("49", result.ServerId);
        Assert.Null(result.PacketLossPercent);
        Assert.Null(result.ResultUrl);
        var command = Assert.Single(runner.Requests);
        Assert.Equal(["--json", "--no-icmp", "--secure"], command.ArgumentList);
    }

    [Fact]
    public async Task ExplicitRun_UsesRequestedServerAsOneLiteralArgument()
    {
        var runner = StandardRunner();
        var provider = LibreSpeedTestFactory.Provider(runner);

        var result = await provider.RunAsync(Execution("49"), CancellationToken.None);

        Assert.Equal("49", result.ServerId);
        Assert.Equal(["--json", "--no-icmp", "--secure", "--server", "49"], Assert.Single(runner.Requests).ArgumentList);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, "librespeed_timeout")]
    [InlineData(ProcessTerminationReason.OutputLimitExceeded, "librespeed_invalid_output")]
    [InlineData(ProcessTerminationReason.FailedToStart, "librespeed_not_installed")]
    public async Task ProcessFailures_AreSanitized(ProcessTerminationReason reason, string code)
    {
        var runner = new LibreSpeedRecordingProcessRunner
        {
            Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(
                stderr: "private host path /secret",
                exitCode: null,
                reason: reason))
        };

        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            LibreSpeedTestFactory.Provider(runner).RunAsync(Execution(null), CancellationToken.None));

        Assert.Equal(code, exception.Code);
        Assert.DoesNotContain("secret", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedOutput_UsesStableFailureCode()
    {
        var runner = new LibreSpeedRecordingProcessRunner
        {
            Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(stdout: "not-json"))
        };

        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            LibreSpeedTestFactory.Provider(runner).RunAsync(Execution(null), CancellationToken.None));

        Assert.Equal(SpeedTestFailureCodes.LibreSpeedInvalidOutput, exception.Code);
        Assert.Equal("LibreSpeed CLI returned an invalid result.", exception.SafeMessage);
    }

    private static LibreSpeedRecordingProcessRunner VersionRunner() => new()
    {
        Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(
            stdout: "librespeed-cli v1.0.13 (built on fixture)\n"))
    };

    private static LibreSpeedRecordingProcessRunner StandardRunner() => new()
    {
        Handler = (_, _) => Task.FromResult(LibreSpeedRecordingProcessRunner.Result(
            stdout: LibreSpeedTestFactory.Fixture("result-complete.json")))
    };

    private static SpeedTestExecution Execution(string? serverId) => new(
        Guid.NewGuid(),
        new SpeedTestRequest(ProviderId.LibreSpeed, serverId),
        new NetworkIdentity(null, null, DateTimeOffset.UtcNow, NetworkIdentityState.Unavailable));
}
