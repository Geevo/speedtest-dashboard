using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Infrastructure.Network;

namespace SpeedtestDashboard.Api.Tests.Network;

public sealed class NetworkIdentityRegistrationTests
{
    [Fact]
    public void IpinfoSelectedWithoutToken_StartsWithAddressDiscoveryOnly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NetworkIdentity:MetadataProvider"] = "ipinfo",
                ["NetworkIdentity:Ipinfo:Token"] = string.Empty
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetworkIdentity(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetServices<IIpMetadataProvider>());
        Assert.NotNull(provider.GetRequiredService<IPublicIpResolver>());
        Assert.NotNull(provider.GetRequiredService<INetworkIdentityService>());
    }
}
