using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Core;
using SpeedtestDashboard.Core.Network;

namespace SpeedtestDashboard.Infrastructure.Network;

public static class NetworkIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NetworkIdentityOptions.SectionName);
        services.AddOptions<NetworkIdentityOptions>()
            .Bind(section)
            .Validate(options => options.SuccessCacheSeconds is >= 1 and <= 86400, "Success cache TTL must be between 1 and 86400 seconds.")
            .Validate(options => options.FailureCacheSeconds is >= 1 and <= 3600, "Failure cache TTL must be between 1 and 3600 seconds.")
            .Validate(options => options.RefreshThrottleSeconds is >= 1 and <= 3600, "Refresh throttle must be between 1 and 3600 seconds.")
            .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 60, "Request timeout must be between 1 and 60 seconds.")
            .Validate(options => options.MetadataProvider.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                options.MetadataProvider.Equals("ipconfig", StringComparison.OrdinalIgnoreCase),
                "Metadata provider must be 'none' or 'ipconfig'.")
            .ValidateOnStart();

        AddHttpClient(services, IpifyPublicIpResolver.IPv4ClientName, "https://api.ipify.org/", "text/plain");
        AddHttpClient(services, IpifyPublicIpResolver.IPv6ClientName, "https://api6.ipify.org/", "text/plain");
        AddHttpClient(services, IpConfigIoMetadataProvider.ClientName, "https://ipconfig.io/", "application/json");

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPublicIpResolver, IpifyPublicIpResolver>();

        var configuredOptions = section.Get<NetworkIdentityOptions>() ?? new NetworkIdentityOptions();
        if (configuredOptions.MetadataProvider.Equals("ipconfig", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IIpMetadataProvider, IpConfigIoMetadataProvider>();
        }

        services.AddSingleton<INetworkIdentityService, NetworkIdentityService>();
        return services;
    }

    private static void AddHttpClient(
        IServiceCollection services,
        string name,
        string baseAddress,
        string acceptedMediaType)
    {
        services.AddHttpClient(name, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NetworkIdentityOptions>>().Value;
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SpeedtestDashboard", AppConstants.Version));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptedMediaType));
        });
    }
}
