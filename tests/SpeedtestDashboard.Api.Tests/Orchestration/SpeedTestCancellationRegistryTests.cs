using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Api.Tests.Orchestration;

public sealed class SpeedTestCancellationRegistryTests
{
    [Fact]
    public void RegisteredJob_CanBeCancelledAndRemovalIsIdempotent()
    {
        using var registry = new SpeedTestCancellationRegistry();
        var jobId = Guid.NewGuid();
        var lease = registry.Register(jobId, CancellationToken.None);

        Assert.True(registry.TryCancel(jobId));
        Assert.True(lease.Token.IsCancellationRequested);
        lease.Dispose();
        lease.Dispose();
        Assert.False(registry.TryCancel(jobId));
    }

    [Fact]
    public void UnknownJob_DoesNotReportCancellation()
    {
        using var registry = new SpeedTestCancellationRegistry();

        Assert.False(registry.TryCancel(Guid.NewGuid()));
    }
}
