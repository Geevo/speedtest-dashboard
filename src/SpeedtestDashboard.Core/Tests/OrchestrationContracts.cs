namespace SpeedtestDashboard.Core.Tests;

public interface ISpeedTestJobStore
{
    SpeedTestJob Create(SpeedTestRequest request);

    bool TryGet(Guid jobId, out SpeedTestJob job);

    JobMutationResult Transition(
        Guid jobId,
        SpeedTestJobStatus targetStatus,
        string stage,
        out SpeedTestJob? updatedJob,
        Network.NetworkIdentity? egressIdentity = null,
        SpeedTestResult? result = null,
        SpeedTestFailure? failure = null);

    bool TryRemoveQueued(Guid jobId);

    bool TryRemoveTerminal(Guid jobId);

    IAsyncEnumerable<SpeedTestJobEvent> SubscribeAsync(
        Guid jobId,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken);
}

public interface ISpeedTestQueue
{
    bool TryEnqueue(Guid jobId);

    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}

public interface ISpeedTestCancellationLease : IDisposable
{
    CancellationToken Token { get; }
}

public interface ISpeedTestCancellationRegistry
{
    ISpeedTestCancellationLease Register(Guid jobId, CancellationToken applicationStopping);

    bool TryCancel(Guid jobId);
}
