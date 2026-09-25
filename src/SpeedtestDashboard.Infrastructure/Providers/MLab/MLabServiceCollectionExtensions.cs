using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.MLab;

public static class MLabServiceCollectionExtensions
{
    public static IServiceCollection AddMLabProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var processMaximumTimeout = configuration.GetValue<int?>("Processes:MaxTimeoutSeconds") ?? 600;
        services.AddOptions<MLabOptions>()
            .Bind(configuration.GetSection(MLabOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ExecutablePath) &&
                                 options.ExecutablePath.IndexOfAny(['\r', '\n', '\0']) < 0,
                "M-Lab executable path is invalid.")
            .Validate(options => options.HealthTimeoutSeconds is >= 1 and <= 30,
                "M-Lab health timeout must be between 1 and 30 seconds.")
            .Validate(options => options.HealthCacheSeconds is >= 10 and <= 300,
                "M-Lab health cache duration must be between 10 and 300 seconds.")
            .Validate(options => options.ClientTimeoutSeconds is >= 30 and <= 120,
                "M-Lab client timeout must be between 30 and 120 seconds.")
            .Validate(options => options.TestTimeoutSeconds >= 35 && options.TestTimeoutSeconds <= processMaximumTimeout,
                "M-Lab test timeout must be between 35 seconds and the configured process maximum.")
            .Validate(options => options.TestTimeoutSeconds >= options.ClientTimeoutSeconds + 5,
                "M-Lab test timeout must exceed the client timeout by at least five seconds.")
            .ValidateOnStart();

        services.AddSingleton<MLabCommandFactory>();
        services.AddSingleton<MLabResultParser>();
        services.AddSingleton<MLabSpeedTestProvider>();
        services.AddSingleton<ISpeedTestProvider>(provider => provider.GetRequiredService<MLabSpeedTestProvider>());
        return services;
    }
}
