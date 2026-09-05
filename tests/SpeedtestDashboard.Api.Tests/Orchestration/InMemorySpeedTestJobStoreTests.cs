using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Api.Tests.Orchestration;

public sealed class InMemorySpeedTestJobStoreTests
{
    [Theory]
    [InlineData(SpeedTestJobStatus.Queued, SpeedTestJobStatus.Starting)]
    [InlineData(SpeedTestJobStatus.Queued, SpeedTestJobStatus.Cancelled)]
    [InlineData(SpeedTestJobStatus.Starting, SpeedTestJobStatus.Running)]
    [InlineData(SpeedTestJobStatus.Starting, SpeedTestJobStatus.Failed)]
    [InlineData(SpeedTestJobStatus.Starting, SpeedTestJobStatus.Cancelled)]
    [InlineData(SpeedTestJobStatus.Running, SpeedTestJobStatus.ProcessingResult)]
    [InlineData(SpeedTestJobStatus.Running, SpeedTestJobStatus.Failed)]
    [InlineData(SpeedTestJobStatus.Running, SpeedTestJobStatus.Cancelled)]
    [InlineData(SpeedTestJobStatus.ProcessingResult, SpeedTestJobStatus.Completed)]
    [InlineData(SpeedTestJobStatus.ProcessingResult, SpeedTestJobStatus.Failed)]
    [InlineData(SpeedTestJobStatus.ProcessingResult, SpeedTestJobStatus.Cancelled)]
    public void ValidTransitions_AreCentralizedAndIncrementVersion(
        SpeedTestJobStatus source,
        SpeedTestJobStatus target)
    {
        var store = CreateStore();
        var job = store.Create(CreateRequest());
        MoveTo(store, job.Id, source);
        Assert.True(store.TryGet(job.Id, out var before));

        var result = Transition(store, job.Id, target, out var updated);

        Assert.Equal(JobMutationResult.Success, result);
        Assert.NotNull(updated);
        Assert.Equal(target, updated.Status);
        Assert.Equal(before.Version + 1, updated.Version);
    }

    [Theory]
    [InlineData(SpeedTestJobStatus.Queued, SpeedTestJobStatus.Running)]
    [InlineData(SpeedTestJobStatus.Starting, SpeedTestJobStatus.Completed)]
    [InlineData(SpeedTestJobStatus.Running, SpeedTestJobStatus.Completed)]
    [InlineData(SpeedTestJobStatus.ProcessingResult, SpeedTestJobStatus.Running)]
    public void InvalidTransitions_DoNotMutateJob(
        SpeedTestJobStatus source,
        SpeedTestJobStatus target)
    {
        var store = CreateStore();
        var job = store.Create(CreateRequest());
        MoveTo(store, job.Id, source);
        Assert.True(store.TryGet(job.Id, out var before));

        var result = Transition(store, job.Id, target, out var unchanged);

        Assert.Equal(JobMutationResult.InvalidTransition, result);
        Assert.Equal(before, unchanged);
    }

    [Theory]
    [InlineData(SpeedTestJobStatus.Completed)]
    [InlineData(SpeedTestJobStatus.Failed)]
    [InlineData(SpeedTestJobStatus.Cancelled)]
    public void TerminalStates_AreImmutable(SpeedTestJobStatus terminalState)
    {
        var store = CreateStore();
        var job = store.Create(CreateRequest());
        MoveTo(store, job.Id, SpeedTestJobStatus.ProcessingResult);
        Transition(store, job.Id, terminalState, out var terminal);

        var result = store.Transition(
            job.Id,
            SpeedTestJobStatus.Running,
            "Running again",
            out var unchanged);

        Assert.Equal(JobMutationResult.Terminal, result);
        Assert.Equal(terminal, unchanged);
    }

    [Fact]
    public void LifecycleTimestamps_AreSetByTheStoreClock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemorySpeedTestJobStore(clock);
        var job = store.Create(CreateRequest());
        clock.Advance(TimeSpan.FromSeconds(1));
        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out var started);
        clock.Advance(TimeSpan.FromSeconds(2));
        store.Transition(job.Id, SpeedTestJobStatus.Cancelled, "Cancelled", out var cancelled);

        Assert.Equal(new DateTimeOffset(2026, 9, 4, 10, 0, 0, TimeSpan.Zero), job.CreatedAtUtc);
        Assert.Equal(job.CreatedAtUtc.AddSeconds(1), started?.StartedAtUtc);
        Assert.Equal(job.CreatedAtUtc.AddSeconds(3), cancelled?.CompletedAtUtc);
        Assert.Equal(SpeedTestFailureCodes.Cancelled, cancelled?.Failure?.Code);
    }

    [Fact]
    public async Task CompletionAndCancellationRace_ProducesOneImmutableTerminalOutcome()
    {
        var store = CreateStore();
        var job = store.Create(CreateRequest());
        MoveTo(store, job.Id, SpeedTestJobStatus.ProcessingResult);
        using var start = new ManualResetEventSlim(false);

        var completion = Task.Run(() =>
        {
            start.Wait();
            return Transition(store, job.Id, SpeedTestJobStatus.Completed, out _);
        });
        var cancellation = Task.Run(() =>
        {
            start.Wait();
            return Transition(store, job.Id, SpeedTestJobStatus.Cancelled, out _);
        });
        start.Set();
        var outcomes = await Task.WhenAll(completion, cancellation);

        Assert.Single(outcomes, outcome => outcome == JobMutationResult.Success);
        Assert.Single(outcomes, outcome => outcome == JobMutationResult.Terminal);
        Assert.True(store.TryGet(job.Id, out var terminal));
        Assert.True(terminal.IsTerminal);
        Assert.Equal(5, terminal.Version);
    }

    [Fact]
    public void FailedEnqueueRollback_RemovesOnlyQueuedJobs()
    {
        var store = CreateStore();
        var queued = store.Create(CreateRequest());
        var started = store.Create(CreateRequest());
        store.Transition(started.Id, SpeedTestJobStatus.Starting, "Starting", out _);

        Assert.True(store.TryRemoveQueued(queued.Id));
        Assert.False(store.TryGet(queued.Id, out _));
        Assert.False(store.TryRemoveQueued(started.Id));
        Assert.True(store.TryGet(started.Id, out _));
    }

    internal static SpeedTestRequest CreateRequest() => new(
        ProviderId.Parse("fixture"),
        ServerId: null);

    internal static InMemorySpeedTestJobStore CreateStore() => new(TimeProvider.System);

    internal static JobMutationResult Transition(
        InMemorySpeedTestJobStore store,
        Guid jobId,
        SpeedTestJobStatus target,
        out SpeedTestJob? updated)
    {
        var result = target == SpeedTestJobStatus.Completed
            ? FakeSpeedTestProvider.SuccessfulResult(ProviderId.Parse("fixture"))
            : null;
        var failure = target == SpeedTestJobStatus.Failed
            ? new SpeedTestFailure(SpeedTestFailureCodes.ProviderFailed, "Provider failed.")
            : null;
        return store.Transition(jobId, target, target.ToString(), out updated, result: result, failure: failure);
    }

    internal static void MoveTo(
        InMemorySpeedTestJobStore store,
        Guid jobId,
        SpeedTestJobStatus target)
    {
        if (target == SpeedTestJobStatus.Queued)
        {
            return;
        }

        Transition(store, jobId, SpeedTestJobStatus.Starting, out _);
        if (target == SpeedTestJobStatus.Starting)
        {
            return;
        }

        Transition(store, jobId, SpeedTestJobStatus.Running, out _);
        if (target == SpeedTestJobStatus.Running)
        {
            return;
        }

        Transition(store, jobId, SpeedTestJobStatus.ProcessingResult, out _);
    }
}
