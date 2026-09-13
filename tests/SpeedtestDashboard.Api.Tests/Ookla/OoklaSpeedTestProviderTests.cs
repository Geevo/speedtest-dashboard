using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Processes;
using SpeedtestDashboard.Infrastructure.Providers.Ookla;

namespace SpeedtestDashboard.Api.Tests.Ookla;

public sealed class OoklaSpeedTestProviderTests
{
    [Fact]
    public async Task DisabledProvider_IsUnavailableWithoutStartingAProcess()
    {
        var runner = new RecordingProcessRunner();
        var options = OoklaTestFactory.Options();
        options.Enabled = false;
        var provider = OoklaTestFactory.Provider(runner, options);

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.Contains("disabled", health.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task MissingExecutable_IsReportedWithoutRawExceptionDetails()
    {
        var runner = RunnerReturning(reason: ProcessTerminationReason.FailedToStart, exitCode: null);
        var provider = OoklaTestFactory.Provider(runner);

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.Equal("Speedtest CLI is not installed.", health.Message);
        Assert.Null(health.Version);
    }

    [Fact]
    public async Task InstalledCliWithoutConfiguredAcceptance_IsDistinctlyUnavailable()
    {
        var runner = RunnerReturning(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n");
        var provider = OoklaTestFactory.Provider(runner, OoklaTestFactory.Options(accepted: false));

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Unavailable, health.State);
        Assert.Equal("1.2.0.84", health.Version);
        Assert.Contains("acceptance", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinnedAcceptedCli_IsHealthyAndCapabilitiesAreHonest()
    {
        var runner = RunnerReturning(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n");
        var provider = OoklaTestFactory.Provider(runner);

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Available, health.State);
        Assert.Equal("1.2.0.84", health.Version);
        Assert.True(provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.ServerDiscovery));
        Assert.True(provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.ResultUrl));
        Assert.False(provider.Descriptor.Capabilities.HasFlag(ProviderCapabilities.IPv6));
    }

    [Fact]
    public async Task UnexpectedVersion_IsDegradedRatherThanMisreported()
    {
        var runner = RunnerReturning(stdout: "Speedtest by Ookla 1.3.0.1 (future)\n");
        var provider = OoklaTestFactory.Provider(runner);

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Degraded, health.State);
        Assert.Equal("1.3.0.1", health.Version);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, "timed out")]
    [InlineData(ProcessTerminationReason.OutputLimitExceeded, "failed")]
    public async Task HealthProcessFailures_AreSanitized(ProcessTerminationReason reason, string expectedMessage)
    {
        var provider = OoklaTestFactory.Provider(RunnerReturning(reason: reason, exitCode: null));

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.NotEqual(ProviderHealthState.Available, health.State);
        Assert.Contains(expectedMessage, health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedVersion_IsDegraded()
    {
        var provider = OoklaTestFactory.Provider(RunnerReturning(stdout: "mystery binary 1.2.0.84"));

        var health = await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(ProviderHealthState.Degraded, health.State);
        Assert.Null(health.Version);
    }

    [Fact]
    public async Task HealthIsCachedForConfiguredDuration()
    {
        var runner = RunnerReturning(stdout: "Speedtest by Ookla 1.2.0.84 (ea6b6773cf)\n");
        var time = new AdvancingTimeProvider(new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero));
        var provider = OoklaTestFactory.Provider(runner, timeProvider: time);

        await provider.CheckHealthAsync(CancellationToken.None);
        await provider.CheckHealthAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(46));
        await provider.CheckHealthAsync(CancellationToken.None);

        Assert.Equal(2, runner.Requests.Count);
    }

    [Fact]
    public async Task ServerList_IsCachedThenFilteredInMemory()
    {
        var runner = RunnerReturning(stdout: OoklaTestFactory.Fixture("servers-normal.txt"));
        var provider = OoklaTestFactory.Provider(runner);

        var london = await provider.GetServersAsync(new ServerQuery("London", 25), CancellationToken.None);
        var byId = await provider.GetServersAsync(new ServerQuery("23456", 25), CancellationToken.None);

        Assert.Equal("12345", Assert.Single(london).Id);
        Assert.Equal("23456", Assert.Single(byId).Id);
        Assert.Single(runner.Requests);
        Assert.Contains("--servers", runner.Requests[0].ArgumentList);
    }

    [Fact]
    public async Task AutomaticAndExplicitRunsUseExistingProcessContract()
    {
        var runner = RunnerReturning(stdout: OoklaTestFactory.Fixture("result-complete.json"));
        var provider = OoklaTestFactory.Provider(runner);

        var automatic = await provider.RunAsync(Execution(serverId: null), CancellationToken.None);
        var explicitResult = await provider.RunAsync(Execution(serverId: "12345"), CancellationToken.None);

        Assert.Equal(1000m, automatic.DownloadMbps);
        Assert.Equal("12345", explicitResult.ServerId);
        Assert.DoesNotContain(runner.Requests[0].ArgumentList, argument => argument.StartsWith("--server-id", StringComparison.Ordinal));
        Assert.Contains("--server-id=12345", runner.Requests[1].ArgumentList);
    }

    [Fact]
    public void ProviderRequestValidationRejectsHostileServerId()
    {
        var provider = OoklaTestFactory.Provider(new RecordingProcessRunner());

        var hostile = provider.ValidateRequest(new SpeedTestRequest(
            OoklaProviderDefinition.Id, "123;touch /tmp/pwned"));

        Assert.False(hostile.IsValid);
    }

    [Theory]
    [InlineData(ProcessTerminationReason.TimedOut, OoklaFailureCodes.Timeout)]
    [InlineData(ProcessTerminationReason.OutputLimitExceeded, OoklaFailureCodes.InvalidOutput)]
    [InlineData(ProcessTerminationReason.FailedToStart, OoklaFailureCodes.NotInstalled)]
    public async Task RunMapsProcessTerminationToStableFailures(
        ProcessTerminationReason reason,
        string expectedCode)
    {
        var provider = OoklaTestFactory.Provider(RunnerReturning(reason: reason, exitCode: null));

        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            provider.RunAsync(Execution(null), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("stderr", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Server id not found", OoklaFailureCodes.ServerNotFound)]
    [InlineData("Configuration - Couldn't connect to server (Network is unreachable)", OoklaFailureCodes.NetworkUnavailable)]
    [InlineData("unclassified internal failure", OoklaFailureCodes.Failed)]
    public async Task NonZeroExitMapsKnownConditionsWithoutExposingStderr(string stderr, string expectedCode)
    {
        var provider = OoklaTestFactory.Provider(RunnerReturning(stderr: stderr, exitCode: 2));

        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            provider.RunAsync(Execution(null), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain(stderr, exception.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedResultMapsToSanitizedProviderFailure()
    {
        var provider = OoklaTestFactory.Provider(RunnerReturning(stdout: "not json"));

        var exception = await Assert.ThrowsAsync<ProviderExecutionException>(() =>
            provider.RunAsync(Execution(null), CancellationToken.None));

        Assert.Equal(OoklaFailureCodes.InvalidOutput, exception.Code);
        Assert.Equal("Speedtest CLI returned an invalid result.", exception.SafeMessage);
    }

    [Fact]
    public async Task CancellationPropagatesThroughProvider()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new RecordingProcessRunner
        {
            Handler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return RecordingProcessRunner.Result();
            }
        };
        var provider = OoklaTestFactory.Provider(runner);
        var run = provider.RunAsync(Execution(null), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private static RecordingProcessRunner RunnerReturning(
        string stdout = "",
        string stderr = "",
        int? exitCode = 0,
        ProcessTerminationReason reason = ProcessTerminationReason.Exited) => new()
        {
            Handler = (_, _) => Task.FromResult(RecordingProcessRunner.Result(stdout, stderr, exitCode, reason))
        };

    private static SpeedTestExecution Execution(string? serverId) => new(
        Guid.NewGuid(),
        new SpeedTestRequest(OoklaProviderDefinition.Id, serverId),
        new NetworkIdentity(null, null, DateTimeOffset.UtcNow, NetworkIdentityState.Unavailable));

    private sealed class AdvancingTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
