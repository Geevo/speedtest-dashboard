namespace SpeedtestDashboard.Api.Authentication;

internal sealed class DashboardAuthenticationState
{
    private volatile bool _enabled;
    private volatile bool _showDisabledWarning = true;

    public bool IsEnabled => _enabled;
    public bool ShowDisabledWarning => _showDisabledWarning;
    public SemaphoreSlim MutationLock { get; } = new(1, 1);

    public void Set(bool enabled, bool showDisabledWarning)
    {
        _enabled = enabled;
        _showDisabledWarning = showDisabledWarning;
    }
}
