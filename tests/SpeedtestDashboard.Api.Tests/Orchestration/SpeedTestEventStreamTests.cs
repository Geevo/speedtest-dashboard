using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Api.Tests.Orchestration;

public sealed class SpeedTestEventStreamTests
{
    [Fact]
    public async Task FirstEvent_IsAlwaysACompleteSnapshot()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        var job = store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());

        await using var events = store
            .SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await events.MoveNextAsync());
        Assert.Equal(SpeedTestJobEventType.Snapshot, events.Current.Type);
        Assert.Equal(job, events.Current.Job);
        Assert.Equal(1, events.Current.Version);
    }

    [Fact]
    public async Task StateTransitions_AreDeliveredWithMonotonicVersions()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        var job = store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
        await using var events = store
            .SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None)
            .GetAsyncEnumerator();
        await events.MoveNextAsync();

        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out _);
        Assert.True(await events.MoveNextAsync());

        Assert.Equal(SpeedTestJobEventType.State, events.Current.Type);
        Assert.Equal(SpeedTestJobStatus.Starting, events.Current.Job?.Status);
        Assert.Equal(2, events.Current.Version);
    }

    [Fact]
    public async Task TerminalResult_IsDeliveredAndThenStreamEnds()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        var job = store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
        InMemorySpeedTestJobStoreTests.MoveTo(store, job.Id, SpeedTestJobStatus.ProcessingResult);
        await using var events = store
            .SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None)
            .GetAsyncEnumerator();
        await events.MoveNextAsync();

        InMemorySpeedTestJobStoreTests.Transition(store, job.Id, SpeedTestJobStatus.Completed, out _);
        Assert.True(await events.MoveNextAsync());
        var terminal = events.Current;

        Assert.Equal(SpeedTestJobEventType.Result, terminal.Type);
        Assert.Equal(SpeedTestJobStatus.Completed, terminal.Job?.Status);
        Assert.NotNull(terminal.Job?.Result);
        Assert.False(await events.MoveNextAsync());
    }

    [Fact]
    public async Task MultipleSubscribers_ReceiveTheSameUpdate()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        var job = store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
        await using var first = store.SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None).GetAsyncEnumerator();
        await using var second = store.SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None).GetAsyncEnumerator();
        await first.MoveNextAsync();
        await second.MoveNextAsync();

        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out _);

        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(first.Current.Version, second.Current.Version);
    }

    [Fact]
    public async Task DisconnectingOneSubscriber_DoesNotAffectJobOrOtherSubscribers()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        var job = store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
        var disconnected = store.SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None).GetAsyncEnumerator();
        await using var remaining = store.SubscribeAsync(job.Id, TimeSpan.FromSeconds(1), CancellationToken.None).GetAsyncEnumerator();
        await disconnected.MoveNextAsync();
        await remaining.MoveNextAsync();
        await disconnected.DisposeAsync();

        store.Transition(job.Id, SpeedTestJobStatus.Starting, "Starting", out _);

        Assert.True(await remaining.MoveNextAsync());
        Assert.Equal(SpeedTestJobStatus.Starting, remaining.Current.Job?.Status);
        Assert.True(store.TryGet(job.Id, out var current));
        Assert.Equal(SpeedTestJobStatus.Starting, current.Status);
    }

    [Fact]
    public async Task ActiveJob_EmitsHeartbeatWithoutFakeProgress()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        var job = store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
        await using var events = store
            .SubscribeAsync(job.Id, TimeSpan.FromMilliseconds(20), CancellationToken.None)
            .GetAsyncEnumerator();
        await events.MoveNextAsync();

        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(SpeedTestJobEventType.Heartbeat, events.Current.Type);
        Assert.Null(events.Current.Job);
        Assert.Equal(job.Version, events.Current.Version);
    }

    [Fact]
    public async Task UnknownJob_SubscriptionFailsWithoutCreatingState()
    {
        var store = InMemorySpeedTestJobStoreTests.CreateStore();
        await using var events = store
            .SubscribeAsync(Guid.NewGuid(), TimeSpan.FromSeconds(1), CancellationToken.None)
            .GetAsyncEnumerator();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => events.MoveNextAsync().AsTask());
    }
}
