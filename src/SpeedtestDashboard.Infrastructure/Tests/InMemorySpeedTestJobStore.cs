using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SpeedtestDashboard.Core.History;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Infrastructure.Tests;

public sealed class InMemorySpeedTestJobStore(
    TimeProvider timeProvider,
    ISpeedTestPersistenceWriter? persistence = null) : ISpeedTestJobStore
{
    private const int SubscriberCapacity = 8;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, SpeedTestJob> _jobs = [];
    private readonly Dictionary<Guid, Dictionary<Guid, Channel<SpeedTestJobEvent>>> _subscribers = [];

    public SpeedTestJob Create(SpeedTestRequest request)
    {
        var job = new SpeedTestJob(
            Guid.NewGuid(),
            request,
            SpeedTestJobStatus.Queued,
            "Queued",
            Version: 1,
            timeProvider.GetUtcNow(),
            StartedAtUtc: null,
            CompletedAtUtc: null,
            EgressIdentity: null,
            Result: null,
            Failure: null);

        persistence?.PersistCreated(job);

        lock (_gate)
        {
            _jobs.Add(job.Id, job);
        }

        return job;
    }

    public bool TryGet(Guid jobId, out SpeedTestJob job)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(jobId, out job!);
        }
    }

    public JobMutationResult Transition(
        Guid jobId,
        SpeedTestJobStatus targetStatus,
        string stage,
        out SpeedTestJob? updatedJob,
        Core.Network.NetworkIdentity? egressIdentity = null,
        SpeedTestResult? result = null,
        SpeedTestFailure? failure = null)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var current))
            {
                updatedJob = null;
                return JobMutationResult.NotFound;
            }

            if (current.IsTerminal)
            {
                updatedJob = current;
                return JobMutationResult.Terminal;
            }

            if (!IsValidTransition(current.Status, targetStatus) ||
                (targetStatus == SpeedTestJobStatus.Completed && result is null) ||
                (targetStatus == SpeedTestJobStatus.Failed && failure is null))
            {
                updatedJob = current;
                return JobMutationResult.InvalidTransition;
            }

            var now = timeProvider.GetUtcNow();
            var normalizedStage = NormalizeStage(stage, targetStatus);
            var normalizedFailure = targetStatus == SpeedTestJobStatus.Cancelled
                ? failure ?? new SpeedTestFailure(SpeedTestFailureCodes.Cancelled, "The test was cancelled.")
                : failure;

            updatedJob = current with
            {
                Status = targetStatus,
                Stage = normalizedStage,
                Version = checked(current.Version + 1),
                StartedAtUtc = targetStatus == SpeedTestJobStatus.Starting
                    ? now
                    : current.StartedAtUtc,
                CompletedAtUtc = targetStatus is SpeedTestJobStatus.Completed or SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled
                    ? now
                    : current.CompletedAtUtc,
                EgressIdentity = egressIdentity ?? current.EgressIdentity,
                Result = result ?? current.Result,
                Failure = normalizedFailure ?? current.Failure
            };

            try
            {
                persistence?.PersistTransition(updatedJob);
            }
            catch (SpeedTestPersistenceException)
            {
                updatedJob = current with
                {
                    Status = SpeedTestJobStatus.Failed,
                    Stage = "Failed",
                    Version = checked(current.Version + 1),
                    CompletedAtUtc = now,
                    EgressIdentity = egressIdentity ?? current.EgressIdentity,
                    Result = null,
                    Failure = new SpeedTestFailure(
                        SpeedTestFailureCodes.PersistenceFailed,
                        "The test outcome could not be saved to durable history.")
                };
                _jobs[jobId] = updatedJob;
                PublishUnderLock(updatedJob);
                return JobMutationResult.PersistenceFailed;
            }

            _jobs[jobId] = updatedJob;
            PublishUnderLock(updatedJob);
            return JobMutationResult.Success;
        }
    }

    public bool TryRemoveQueued(Guid jobId)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.Status != SpeedTestJobStatus.Queued)
            {
                return false;
            }

            persistence?.DeleteQueued(jobId);
            _jobs.Remove(jobId);
            if (_subscribers.Remove(jobId, out var subscribers))
            {
                foreach (var subscriber in subscribers.Values)
                {
                    subscriber.Writer.TryComplete();
                }
            }

            return true;
        }
    }

    public bool TryRemoveTerminal(Guid jobId)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || !job.IsTerminal)
            {
                return false;
            }

            _jobs.Remove(jobId);
            if (_subscribers.Remove(jobId, out var subscribers))
            {
                foreach (var subscriber in subscribers.Values)
                {
                    subscriber.Writer.TryComplete();
                }
            }

            return true;
        }
    }

    public async IAsyncEnumerable<SpeedTestJobEvent> SubscribeAsync(
        Guid jobId,
        TimeSpan heartbeatInterval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (heartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval));
        }

        var subscriberId = Guid.NewGuid();
        Channel<SpeedTestJobEvent>? channel = null;
        SpeedTestJob initial;

        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out initial!))
            {
                throw new KeyNotFoundException("The speed-test job was not found.");
            }

            if (!initial.IsTerminal)
            {
                channel = Channel.CreateBounded<SpeedTestJobEvent>(new BoundedChannelOptions(SubscriberCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest
                });

                if (!_subscribers.TryGetValue(jobId, out var subscribers))
                {
                    subscribers = [];
                    _subscribers.Add(jobId, subscribers);
                }

                subscribers.Add(subscriberId, channel);
            }
        }

        yield return new SpeedTestJobEvent(
            SpeedTestJobEventType.Snapshot,
            initial,
            initial.Version,
            timeProvider.GetUtcNow());

        if (initial.IsTerminal || channel is null)
        {
            yield break;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var updateAvailable = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var heartbeat = Task.Delay(heartbeatInterval, cancellationToken);
                var completed = await Task.WhenAny(updateAvailable, heartbeat);
                cancellationToken.ThrowIfCancellationRequested();

                if (completed == heartbeat)
                {
                    if (TryGet(jobId, out var current))
                    {
                        yield return new SpeedTestJobEvent(
                            SpeedTestJobEventType.Heartbeat,
                            Job: null,
                            current.Version,
                            timeProvider.GetUtcNow());
                    }

                    continue;
                }

                if (!await updateAvailable)
                {
                    yield break;
                }

                SpeedTestJobEvent? newest = null;
                while (channel.Reader.TryRead(out var update))
                {
                    newest = update;
                }

                if (newest is not null)
                {
                    yield return newest;
                    if (newest.Job?.IsTerminal is true)
                    {
                        yield break;
                    }
                }
            }
        }
        finally
        {
            RemoveSubscriber(jobId, subscriberId);
        }
    }

    private static bool IsValidTransition(SpeedTestJobStatus source, SpeedTestJobStatus target) => source switch
    {
        SpeedTestJobStatus.Queued => target is SpeedTestJobStatus.Starting or SpeedTestJobStatus.Cancelled,
        SpeedTestJobStatus.Starting => target is SpeedTestJobStatus.Running or SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled,
        SpeedTestJobStatus.Running => target is SpeedTestJobStatus.ProcessingResult or SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled,
        SpeedTestJobStatus.ProcessingResult => target is SpeedTestJobStatus.Completed or SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled,
        _ => false
    };

    private static string NormalizeStage(string stage, SpeedTestJobStatus status)
    {
        var candidate = string.IsNullOrWhiteSpace(stage) ? status.ToString() : stage.Trim();
        return candidate.Length <= 120 ? candidate : candidate[..120];
    }

    private void PublishUnderLock(SpeedTestJob job)
    {
        if (!_subscribers.TryGetValue(job.Id, out var subscribers))
        {
            return;
        }

        var eventType = job.Status switch
        {
            SpeedTestJobStatus.Completed => SpeedTestJobEventType.Result,
            SpeedTestJobStatus.Failed or SpeedTestJobStatus.Cancelled => SpeedTestJobEventType.Error,
            _ => SpeedTestJobEventType.State
        };
        var update = new SpeedTestJobEvent(eventType, job, job.Version, timeProvider.GetUtcNow());

        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(update);
            if (job.IsTerminal)
            {
                channel.Writer.TryComplete();
            }
        }

        if (job.IsTerminal)
        {
            _subscribers.Remove(job.Id);
        }
    }

    private void RemoveSubscriber(Guid jobId, Guid subscriberId)
    {
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(jobId, out var subscribers))
            {
                return;
            }

            if (subscribers.Remove(subscriberId, out var channel))
            {
                channel.Writer.TryComplete();
            }

            if (subscribers.Count == 0)
            {
                _subscribers.Remove(jobId);
            }
        }
    }
}
