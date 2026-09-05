namespace SpeedtestDashboard.Core.Providers;

public sealed class ProviderExecutionException(string code, string safeMessage)
    : Exception(safeMessage)
{
    public string Code { get; } = code;

    public string SafeMessage { get; } = safeMessage;
}

