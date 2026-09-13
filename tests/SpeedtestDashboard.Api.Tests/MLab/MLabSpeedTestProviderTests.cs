using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.MLab;

namespace SpeedtestDashboard.Api.Tests.MLab;

public sealed class MLabSpeedTestProviderTests
{
    [Fact]
    public async Task HealthRecognizesOfficialHelpContract()
    {
        var provider = MLabTestFactory.Provider(RunnerReturning(stderr: MLabTestFactory.HelpOutput));

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Available, health.State);
        Assert.Equal("0.10.1", health.Version);
        Assert.Equal(
            ProviderCapabilities.Download | ProviderCapabilities.Upload | ProviderCapabilities.Latency,
            provider.Descriptor.Capabilities);
    }

    [Fact]
    public async Task UnrecognizedHelpIsDegraded()
    {
        var health = await MLabTestFactory.Provider(RunnerReturning(stderr: "unknown client"))
            .CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Degraded, health.State);
        Assert.Null(health.Version);
    }

    [Fact]
    public async Task RunUsesAutomaticMlabTargetAndMapsOfficialSummary()
    {
        var runner = RunnerReturning(stdout: MLabTestFactory.Fixture("result-complete.json"));
        var result = await MLabTestFactory.Provider(runner).RunAsync(Execution(), CancellationToken.None);

        Assert.Equal(507.25m, result.DownloadMbps);
        Assert.Contains("-format=json", Assert.Single(runner.Requests).ArgumentList);
    }

    [Fact]
    public void ExplicitServerSelectionIsRejected()
    {
        var validation = MLabTestFactory.Provider(new MLabRecordingProcessRunner())
            .ValidateRequest(new SpeedTestRequest(MLabProviderDefinition.Id, "unexpected"));

        Assert.False(validation.IsValid);
        Assert.Equal(SpeedTestFailureCodes.CapabilityNotSupported, validation.Code);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, MLabFailureCodes.Timeout)]
    [InlineData(ProcessTerminationReason.OutputLimitExceeded, MLabFailureCodes.InvalidOutput)]
    [InlineData(ProcessTerminationReason.FailedToStart, MLabFailureCodes.NotInstalled)]
    public async Task RunMapsProcessTerminationToStableFailures(ProcessTerminationReason reason, string expectedCode)
    {
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            MLabTestFactory.Provider(RunnerReturning(exitCode: null, reason: reason))
                .RunAsync(Execution(), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task ConnectionFailureIsSanitized()
    {
        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            MLabTestFactory.Provider(RunnerReturning(
                    stdout: "{\"Failure\":\"dial tcp 192.0.2.1:443: connection refused\"}",
                    exitCode: 1))
                .RunAsync(Execution(), CancellationToken.None));

        Assert.Equal(MLabFailureCodes.NetworkUnavailable, exception.Code);
        Assert.DoesNotContain("192.0.2.1", exception.SafeMessage, StringComparison.Ordinal);
    }

    private static MLabRecordingProcessRunner RunnerReturning(
        string stdout = "",
        string stderr = "",
        int? exitCode = 0,
        ProcessTerminationReason reason = ProcessTerminationReason.Exited) => new()
        {
            Handler = (_, _) => Task.FromResult(MLabRecordingProcessRunner.Result(stdout, stderr, exitCode, reason))
        };

    private static SpeedTestExecution Execution() => new(
        Guid.NewGuid(),
        new SpeedTestRequest(MLabProviderDefinition.Id, null),
        new NetworkIdentity(null, null, DateTimeOffset.UtcNow, NetworkIdentityState.Unavailable));
}
