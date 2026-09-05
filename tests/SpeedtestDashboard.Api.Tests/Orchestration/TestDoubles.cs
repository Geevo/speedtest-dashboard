using System.Net;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Tests.Orchestration;

internal sealed class FakeSpeedTestProvider : ISpeedTestProvider
{
    private int _activeRuns;
    private int _maximumConcurrentRuns;
    private int _runCalls;

    public ProviderId Id { get; init; } = ProviderId.Parse("fixture");

    public string DisplayName { get; init; } = "Fixture provider";

    public ProviderCapabilities Capabilities { get; init; } =
        ProviderCapabilities.Download | ProviderCapabilities.Upload | ProviderCapabilities.Latency;

    public ProviderHealthState HealthState { get; set; } = ProviderHealthState.Available;

    public IReadOnlyList<SpeedTestServer> Servers { get; init; } = [];

    public Func<SpeedTestExecution, CancellationToken, int, Task<SpeedTestResult>>? RunHandler { get; set; }

    public int RunCalls => _runCalls;

    public int MaximumConcurrentRuns => _maximumConcurrentRuns;

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new ProviderHealth(
        Id,
        HealthState,
        "1.2.3-fixture",
        DateTimeOffset.UtcNow,
        HealthState == ProviderHealthState.Available ? "Ready" : "Unavailable"));

    public Task<IReadOnlyList<SpeedTestServer>> GetServersAsync(
        ServerQuery query,
        CancellationToken cancellationToken) => Task.FromResult(Servers);

    public async Task<SpeedTestResult> RunAsync(
        SpeedTestExecution execution,
        CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _runCalls);
        var active = Interlocked.Increment(ref _activeRuns);
        UpdateMaximum(active);
        try
        {
            if (RunHandler is not null)
            {
                return await RunHandler(execution, cancellationToken, call);
            }

            return SuccessfulResult(Id);
        }
        finally
        {
            Interlocked.Decrement(ref _activeRuns);
        }
    }

    public static SpeedTestResult SuccessfulResult(ProviderId providerId) => new(
        providerId,
        ServerId: "fixture-server",
        ServerName: "Fixture server",
        ServerLocation: "Test lab",
        DownloadMbps: 100,
        UploadMbps: 50,
        LatencyMilliseconds: 10,
        JitterMilliseconds: 1,
        PacketLossPercent: 0,
        ResultUrl: null);

    private void UpdateMaximum(int active)
    {
        int observed;
        do
        {
            observed = _maximumConcurrentRuns;
            if (active <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _maximumConcurrentRuns, active, observed) != observed);
    }
}

internal sealed class FakeNetworkIdentityService : INetworkIdentityService
{
    public int Calls { get; private set; }

    public Task<NetworkIdentity> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new NetworkIdentity(
            new NetworkAddressIdentity(
                "8.8.8.8",
                NetworkAddressFamily.IPv4,
                "AS15169",
                "Fixture Network",
                null,
                "US",
                "United States",
                null,
                null,
                "fixture",
                "fixture"),
            null,
            DateTimeOffset.UtcNow,
            NetworkIdentityState.Complete));
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now += duration;
}

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        while (!predicate())
        {
            await Task.Delay(5, cancellation.Token);
        }
    }
}

