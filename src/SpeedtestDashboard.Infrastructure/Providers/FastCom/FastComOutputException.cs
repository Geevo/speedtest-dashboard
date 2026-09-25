namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public sealed class FastComOutputException(string message, bool providerReportedFailure = false) : Exception(message)
{
    public bool ProviderReportedFailure { get; } = providerReportedFailure;
}
