using Microsoft.Extensions.DependencyInjection;

namespace SpeedtestDashboard.Infrastructure.ApiKeys;

public static class ApiKeyServiceCollectionExtensions
{
    public static IServiceCollection AddApiKeyPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IApiCredentialService, ApiCredentialService>();
        services.AddSingleton<IApiIdempotencyStore, ApiIdempotencyStore>();
        return services;
    }
}
