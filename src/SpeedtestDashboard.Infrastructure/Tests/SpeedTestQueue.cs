using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Tests;

public sealed class SpeedTestQueue : ISpeedTestQueue
{
    private readonly Channel<Guid> _channel;

    public SpeedTestQueue(IOptions<SpeedTestOptions> options)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool TryEnqueue(Guid jobId) => _channel.Writer.TryWrite(jobId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

