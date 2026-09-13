using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Infrastructure.Providers.FastCom;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;
using SpeedtestDashboard.Infrastructure.Providers.MLab;
using SpeedtestDashboard.Infrastructure.Providers.Ookla;

namespace SpeedtestDashboard.Infrastructure.Providers;

public static class BuiltInProviderServiceCollectionExtensions
{
    public static IServiceCollection AddBuiltInSpeedTestProviders(
        this IServiceCollection services,
        IConfiguration configuration) => services
            .AddLibreSpeedProvider(configuration)
            .AddFastComProvider(configuration)
            .AddMLabProvider(configuration)
            .AddOoklaProvider(configuration);
}
