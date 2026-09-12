using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.FastCom;

namespace SpeedtestDashboard.Api.Tests.FastCom;

public sealed class FastComSpeedTestProviderTests
{
    [Fact]
    public async Task HealthReadsPinnedVersionFromAnsiHelpOnStandardError()
    {
        var provider = FastComTestFactory.Provider(RunnerReturning(
            stderr: "\u001b[1mfast-cli\u001b[0m v0.3.5 - Estimate connection speed using fast.com\n"));

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Available, health.State);
        Assert.Equal("0.3.5", health.Version);
        Assert.Equal(
            ProviderCapabilities.Download | ProviderCapabilities.Upload | ProviderCapabilities.Latency,
            provider.Capabilities);
    }

    [Fact]
    public async Task UnexpectedVersion_IsDegraded()
    {
        var provider = FastComTestFactory.Provider(RunnerReturning(
            stderr: "fast-cli v0.4.0 - Estimate connection speed using fast.com\n"));

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Degraded, health.State);
        Assert.Equal("0.4.0", health.Version);
    }

    [Fact]
    public async Task RunUsesAutomaticFastComTargetsAndMapsResult()
    {
        var runner = RunnerReturning(stdout: FastComTestFactory.Fixture("result-complete.json"));
        var result = await FastComTestFactory.Provider(runner).RunAsync(Execution(), CancellationToken.None);

        Assert.Equal(131m, result.DownloadMbps);
        Assert.Contains("--json", Assert.Single(runner.Requests).ArgumentList);
    }

    [Fact]
    public void ExplicitServerSelectionIsRejected()
    {
        var validation = FastComTestFactory.Provider(new FastComRecordingProcessRunner())
            .ValidateRequest(new SpeedTestRequest(ProviderId.FastCom, "unexpected"));

        Assert.False(validation.IsValid);
        Assert.Equal(SpeedTestFailureCodes.CapabilityNotSupported, validation.Code);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, SpeedTestFailureCodes.FastComTimeout)]
    [InlineData(ProcessTerminationReason.OutputLimitExceeded, SpeedTestFailureCodes.FastComInvalidOutput)]
    [InlineData(ProcessTerminationReason.FailedToStart, SpeedTestFailureCodes.FastComNotInstalled)]
    public async Task RunMapsProcessTerminationToStableFailures(
        ProcessTerminationReason reason,
        string expectedCode)
    {
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            FastComTestFactory.Provider(RunnerReturning(exitCode: null, reason: reason))
                .RunAsync(Execution(), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task JsonErrorWithZeroExitMapsNetworkFailureWithoutExposingProviderText()
    {
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            FastComTestFactory.Provider(RunnerReturning(stdout: FastComTestFactory.Fixture("result-error.json")))
                .RunAsync(Execution(), CancellationToken.None));

        Assert.Equal(SpeedTestFailureCodes.FastComNetworkUnavailable, exception.Code);
        Assert.Equal("FAST.com could not complete the speed test.", exception.SafeMessage);
    }

    [Fact]
    public async Task CancellationPropagatesThroughProvider()
    {
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FastComTestFactory.Provider(RunnerReturning(
                    exitCode: null,
                    reason: ProcessTerminationReason.Cancelled))
                .RunAsync(Execution(), new CancellationToken(canceled: true)));

        Assert.True(exception.CancellationToken.IsCancellationRequested);
    }

    private static FastComRecordingProcessRunner RunnerReturning(
        string stdout = "",
        string stderr = "",
        int? exitCode = 0,
        ProcessTerminationReason reason = ProcessTerminationReason.Exited) => new()
        {
            Handler = (_, _) => Task.FromResult(FastComRecordingProcessRunner.Result(stdout, stderr, exitCode, reason))
        };

    private static SpeedTestExecution Execution() => new(
        Guid.NewGuid(),
        new SpeedTestRequest(ProviderId.FastCom, null),
        new NetworkIdentity(null, null, DateTimeOffset.UtcNow, NetworkIdentityState.Unavailable));
}
