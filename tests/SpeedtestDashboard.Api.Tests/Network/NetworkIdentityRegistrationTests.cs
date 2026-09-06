using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Core.Network;
using SpeedtestDashboard.Infrastructure.Network;

namespace SpeedtestDashboard.Api.Tests.Network;

public sealed class NetworkIdentityRegistrationTests
{
    [Theory]
    [InlineData("ipconfig")]
    [InlineData("IPCONFIG")]
    public void IpConfigSelected_RegistersProviderWithoutToken(string selection)
    {
        using var provider = BuildProvider(selection);
        Assert.IsType<IpConfigIoMetadataProvider>(Assert.Single(provider.GetServices<IIpMetadataProvider>()));
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(IpConfigIoMetadataProvider.ClientName);
        Assert.Equal(new Uri("https://ipconfig.io/"), client.BaseAddress);
        Assert.Contains(client.DefaultRequestHeaders.Accept, value => value.MediaType == "application/json");
        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.NotNull(provider.GetRequiredService<INetworkIdentityService>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    public void MetadataNotSelected_StartsWithAddressDiscoveryOnly(string? selection)
    {
        using var provider = BuildProvider(selection);
        Assert.Empty(provider.GetServices<IIpMetadataProvider>());
        Assert.NotNull(provider.GetRequiredService<IPublicIpResolver>());
        Assert.NotNull(provider.GetRequiredService<INetworkIdentityService>());
    }

    [Theory]
    [InlineData("ipinfo")]
    [InlineData("unknown")]
    public void UnsupportedProvider_ReportsValidChoices(string selection)
    {
        using var provider = BuildProvider(selection);
        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NetworkIdentityOptions>>().Value);
        Assert.Contains("'none' or 'ipconfig'", exception.Message);
    }

    private static ServiceProvider BuildProvider(string? selection)
    {
        var values = new Dictionary<string, string?>();
        if (selection is not null) values["NetworkIdentity:MetadataProvider"] = selection;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetworkIdentity(configuration);
        return services.BuildServiceProvider();
    }
}
