using Microsoft.Extensions.Logging.Abstractions;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Providers;
using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Api.Tests.Orchestration;

public sealed class SpeedTestWorkerTests
{
    [Fact]
    public async Task CompletedJob_CapturesEgressAndNormalizedResult()
    {
        var provider = new FakeSpeedTestProvider();
        await using var harness = new WorkerHarness(provider);
        var job = harness.CreateAndEnqueue();

        await harness.StartAsync();
        await TestWait.UntilAsync(() => harness.Store.TryGet(job.Id, out var current) && current.IsTerminal);
        Assert.True(harness.Store.TryGet(job.Id, out var completed));

        Assert.Equal(SpeedTestJobStatus.Completed, completed.Status);
        Assert.Equal(5, completed.Version);
        Assert.Equal("8.8.8.8", completed.EgressIdentity?.IPv4?.Address);
        Assert.Equal(job.Id, completed.Result?.JobId);
        Assert.Equal("8.8.8.8", completed.Result?.EgressIdentity?.IPv4?.Address);
    }

    [Fact]
    public async Task TwoAcceptedJobs_NeverExecuteConcurrently()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeSpeedTestProvider
        {
            RunHandler = async (_, token, call) =>
            {
                if (call == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                }

                return FakeSpeedTestProvider.SuccessfulResult(ProviderId.Parse("fixture"));
            }
        };
        await using var harness = new WorkerHarness(provider);
        var first = harness.CreateAndEnqueue();
        var second = harness.CreateAndEnqueue();

        await harness.StartAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, provider.RunCalls);
        Assert.Equal(1, provider.MaximumConcurrentRuns);
        releaseFirst.SetResult();
        await TestWait.UntilAsync(() =>
            harness.Store.TryGet(first.Id, out var firstJob) && firstJob.IsTerminal &&
            harness.Store.TryGet(second.Id, out var secondJob) && secondJob.IsTerminal);

        Assert.Equal(2, provider.RunCalls);
        Assert.Equal(1, provider.MaximumConcurrentRuns);
    }

    [Fact]
    public async Task CancelledQueuedJob_IsSkippedAndNextJobRuns()
    {
        var provider = new FakeSpeedTestProvider();
        await using var harness = new WorkerHarness(provider);
        var cancelled = harness.Store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
        harness.Store.Transition(cancelled.Id, SpeedTestJobStatus.Cancelled, "Cancelled", out _);
        Assert.True(harness.Queue.TryEnqueue(cancelled.Id));
        var next = harness.CreateAndEnqueue();

        await harness.StartAsync();
        await TestWait.UntilAsync(() => harness.Store.TryGet(next.Id, out var job) && job.IsTerminal);

        Assert.Equal(1, provider.RunCalls);
        Assert.True(harness.Store.TryGet(cancelled.Id, out var cancelledJob));
        Assert.Equal(SpeedTestJobStatus.Cancelled, cancelledJob.Status);
    }

    [Fact]
    public async Task WorkerContinuesAfterProviderFailure()
    {
        var provider = new FakeSpeedTestProvider
        {
            RunHandler = (_, _, call) => call == 1
                ? throw new ProviderExecutionException(SpeedTestFailureCodes.ProviderFailed, "Provider could not complete the test.")
                : Task.FromResult(FakeSpeedTestProvider.SuccessfulResult(ProviderId.Parse("fixture")))
        };
        await using var harness = new WorkerHarness(provider);
        var first = harness.CreateAndEnqueue();
        var second = harness.CreateAndEnqueue();

        await harness.StartAsync();
        await TestWait.UntilAsync(() =>
            harness.Store.TryGet(first.Id, out var firstJob) && firstJob.IsTerminal &&
            harness.Store.TryGet(second.Id, out var secondJob) && secondJob.IsTerminal);

        Assert.True(harness.Store.TryGet(first.Id, out var failed));
        Assert.True(harness.Store.TryGet(second.Id, out var completed));
        Assert.Equal(SpeedTestJobStatus.Failed, failed.Status);
        Assert.Equal(SpeedTestFailureCodes.ProviderFailed, failed.Failure?.Code);
        Assert.Equal(SpeedTestJobStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task RunningCancellation_ReachesProviderAndEndsCancelled()
    {
        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeSpeedTestProvider
        {
            RunHandler = async (_, token, _) =>
            {
                providerStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    providerCancelled.SetResult();
                    throw;
                }

                return FakeSpeedTestProvider.SuccessfulResult(ProviderId.Parse("fixture"));
            }
        };
        await using var harness = new WorkerHarness(provider);
        var job = harness.CreateAndEnqueue();
        await harness.StartAsync();
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            JobMutationResult.Success,
            harness.Store.Transition(job.Id, SpeedTestJobStatus.Cancelled, "Cancelled", out _));
        Assert.True(harness.CancellationRegistry.TryCancel(job.Id));
        await providerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(harness.Store.TryGet(job.Id, out var cancelled));
        Assert.Equal(SpeedTestJobStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task StartingCancellation_StopsNetworkIdentityCapture()
    {
        var identity = new BlockingNetworkIdentityService();
        var provider = new FakeSpeedTestProvider();
        await using var harness = new WorkerHarness(provider, identity);
        var job = harness.CreateAndEnqueue();
        await harness.StartAsync();
        await identity.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            JobMutationResult.Success,
            harness.Store.Transition(job.Id, SpeedTestJobStatus.Cancelled, "Cancelled", out _));
        Assert.True(harness.CancellationRegistry.TryCancel(job.Id));
        await identity.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, provider.RunCalls);
        Assert.True(harness.Store.TryGet(job.Id, out var cancelled));
        Assert.Equal(SpeedTestJobStatus.Cancelled, cancelled.Status);
    }

    private sealed class WorkerHarness : IAsyncDisposable
    {
        private readonly SpeedTestWorker _worker;

        public WorkerHarness(FakeSpeedTestProvider provider, INetworkIdentityService? networkIdentityService = null)
        {
            Store = new InMemorySpeedTestJobStore(TimeProvider.System);
            Queue = SpeedTestQueueTests.CreateQueue();
            CancellationRegistry = new SpeedTestCancellationRegistry();
            _worker = new SpeedTestWorker(
                Queue,
                Store,
                new SpeedTestProviderRegistry([provider]),
                CancellationRegistry,
                networkIdentityService ?? new FakeNetworkIdentityService(),
                NullLogger<SpeedTestWorker>.Instance);
        }

        public InMemorySpeedTestJobStore Store { get; }

        public SpeedTestQueue Queue { get; }

        public SpeedTestCancellationRegistry CancellationRegistry { get; }

        public SpeedTestJob CreateAndEnqueue()
        {
            var job = Store.Create(InMemorySpeedTestJobStoreTests.CreateRequest());
            Assert.True(Queue.TryEnqueue(job.Id));
            return job;
        }

        public Task StartAsync() => _worker.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
            CancellationRegistry.Dispose();
        }
    }

    private sealed class BlockingNetworkIdentityService : INetworkIdentityService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<NetworkIdentity> GetAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.SetResult();
                throw;
            }

            throw new InvalidOperationException();
        }
    }
}
