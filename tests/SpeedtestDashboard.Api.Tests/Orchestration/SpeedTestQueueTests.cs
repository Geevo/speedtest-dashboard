using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Tests;

namespace SpeedtestDashboard.Api.Tests.Orchestration;

public sealed class SpeedTestQueueTests
{
    [Fact]
    public async Task FullQueue_ReturnsFalseImmediatelyWithoutDroppingAcceptedJob()
    {
        var queue = CreateQueue(capacity: 1);
        var accepted = Guid.NewGuid();

        Assert.True(queue.TryEnqueue(accepted));
        Assert.False(queue.TryEnqueue(Guid.NewGuid()));

        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(accepted, reader.Current);
    }

    [Fact]
    public async Task AcceptedJobs_AreReadInAdmissionOrder()
    {
        var queue = CreateQueue(capacity: 3);
        var accepted = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var jobId in accepted)
        {
            Assert.True(queue.TryEnqueue(jobId));
        }

        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        var actual = new List<Guid>();
        for (var index = 0; index < accepted.Length; index++)
        {
            Assert.True(await reader.MoveNextAsync());
            actual.Add(reader.Current);
        }

        Assert.Equal(accepted, actual);
    }

    internal static SpeedTestQueue CreateQueue(int capacity = 4) => new(Options.Create(new SpeedTestOptions
    {
        QueueCapacity = capacity
    }));
}

