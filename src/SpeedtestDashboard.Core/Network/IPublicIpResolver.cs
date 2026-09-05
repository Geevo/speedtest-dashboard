using System.Net;

namespace SpeedtestDashboard.Core.Network;

public interface IPublicIpResolver
{
    Task<IPAddress?> ResolveIPv4Async(CancellationToken cancellationToken);

    Task<IPAddress?> ResolveIPv6Async(CancellationToken cancellationToken);
}

