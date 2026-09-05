using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Providers;
using SpeedtestDashboard.Infrastructure.Schedules;
using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Api.Tests.Schedules;

internal sealed class ScheduleWorkerHarness
{
    public ScheduleWorkerHarness(
        FakeSpeedTestProvider provider,
        ManualTimeProvider timeProvider,
        int queueCapacity = 4)
    {
        TimeProvider = timeProvider;
        JobStore = new InMemorySpeedTestJobStore(timeProvider);
        Queue = SpeedTestQueueTests.CreateQueue(queueCapacity);
        var registry = new SpeedTestProviderRegistry([provider]);
        var submissionService = new SpeedTestSubmissionService(
            registry, JobStore, Queue, Options.Create(new SpeedTestOptions { QueueFullRetryAfterSeconds = 5 }));
        Store = new FakeScheduleStore();
        Worker = new ScheduleWorker(
            Store,
            submissionService,
            JobStore,
            Options.Create(new ScheduleWorkerOptions()),
            timeProvider,
            NullLogger<ScheduleWorker>.Instance);
    }

    public ManualTimeProvider TimeProvider { get; }

    public InMemorySpeedTestJobStore JobStore { get; }

    public SpeedTestQueue Queue { get; }

    public FakeScheduleStore Store { get; }

    public ScheduleWorker Worker { get; }
}
