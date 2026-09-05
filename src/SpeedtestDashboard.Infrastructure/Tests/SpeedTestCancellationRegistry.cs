using System.Collections.Concurrent;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Tests;

public sealed class SpeedTestCancellationRegistry : ISpeedTestCancellationRegistry, IDisposable
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _registrations = new();

    public ISpeedTestCancellationLease Register(Guid jobId, CancellationToken applicationStopping)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        if (!_registrations.TryAdd(jobId, cancellation))
        {
            cancellation.Dispose();
            throw new InvalidOperationException("A cancellation token is already registered for this job.");
        }

        return new Lease(this, jobId, cancellation);
    }

    public bool TryCancel(Guid jobId)
    {
        if (!_registrations.TryGetValue(jobId, out var cancellation))
        {
            return false;
        }

        try
        {
            cancellation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var cancellation in _registrations.Values)
        {
            cancellation.Dispose();
        }

        _registrations.Clear();
    }

    private void Remove(Guid jobId, CancellationTokenSource cancellation)
    {
        _registrations.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(jobId, cancellation));
        cancellation.Dispose();
    }

    private sealed class Lease(
        SpeedTestCancellationRegistry owner,
        Guid jobId,
        CancellationTokenSource cancellation) : ISpeedTestCancellationLease
    {
        private int _disposed;

        public CancellationToken Token => cancellation.Token;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Remove(jobId, cancellation);
            }
        }
    }
}

