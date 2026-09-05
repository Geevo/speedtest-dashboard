using System.Net;

namespace SpeedtestDashboard.Core.Network;

public interface IIpMetadataProvider
{
    Task<IpMetadata?> GetMetadataAsync(IPAddress address, CancellationToken cancellationToken);
}

