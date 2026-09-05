namespace SpeedtestDashboard.Core.Providers;

public interface ISpeedTestProviderRegistry
{
    IReadOnlyCollection<ISpeedTestProvider> GetAll();

    bool TryGet(ProviderId providerId, out ISpeedTestProvider provider);
}

