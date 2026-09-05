using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers;

public sealed class SpeedTestProviderRegistry : ISpeedTestProviderRegistry
{
    private readonly IReadOnlyDictionary<ProviderId, ISpeedTestProvider> _providers;
    private readonly IReadOnlyCollection<ISpeedTestProvider> _all;

    public SpeedTestProviderRegistry(IEnumerable<ISpeedTestProvider> providers)
    {
        var providerList = providers.OrderBy(provider => provider.Id.Value, StringComparer.Ordinal).ToArray();
        var duplicate = providerList
            .GroupBy(provider => provider.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate speed-test provider ID '{duplicate.Key.Value}' was registered.");
        }

        _providers = providerList.ToDictionary(provider => provider.Id);
        _all = Array.AsReadOnly(providerList);
    }

    public IReadOnlyCollection<ISpeedTestProvider> GetAll() => _all;

    public bool TryGet(ProviderId providerId, out ISpeedTestProvider provider) =>
        _providers.TryGetValue(providerId, out provider!);
}

