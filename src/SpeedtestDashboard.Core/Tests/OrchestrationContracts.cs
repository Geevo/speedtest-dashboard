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

public enum SpeedTestSubmissionOutcome
{
    Created,
    InvalidRequest,
    ProviderNotFound,
    CapabilityNotSupported,
    ProviderUnavailable,
    PersistenceFailed,
    QueueFull
}

public sealed record SpeedTestSubmissionResult(
    SpeedTestSubmissionOutcome Outcome,
    SpeedTestJob? Job,
    string? FailureCode,
    string? FailureMessage,
    int? RetryAfterSeconds)
{
    public static SpeedTestSubmissionResult Created(SpeedTestJob job) =>
        new(SpeedTestSubmissionOutcome.Created, job, null, null, null);

    public static SpeedTestSubmissionResult Failed(
        SpeedTestSubmissionOutcome outcome, string code, string message, int? retryAfterSeconds = null) =>
        new(outcome, null, code, message, retryAfterSeconds);
}

/// <summary>
/// The single admission path onto the bounded <see cref="ISpeedTestQueue"/>. Manual dashboard
/// creation, the machine API, and the schedule worker all submit through this service so a
/// speed test is only ever started by the existing queue and worker, never directly.
/// </summary>
public interface ISpeedTestSubmissionService
{
    Task<SpeedTestSubmissionResult> SubmitAsync(SpeedTestRequest request, CancellationToken cancellationToken);
}
