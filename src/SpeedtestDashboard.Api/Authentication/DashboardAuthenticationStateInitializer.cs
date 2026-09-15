using SpeedtestDashboard.Infrastructure.Authentication;

namespace SpeedtestDashboard.Api.Authentication;

internal sealed class DashboardAuthenticationStateInitializer(
    LocalAccountService accounts,
    DashboardAuthenticationState state)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var accountCount = await accounts.GetAccountCountAsync(cancellationToken);
        if (accountCount > 1)
        {
            throw new InvalidOperationException(
                "The database contains more than one application user. Speedtest Dashboard supports exactly one local operator account.");
        }

        var settings = await accounts.GetSettingsAsync(cancellationToken);
        if (settings.AuthenticationEnabled && accountCount == 0)
        {
            throw new InvalidOperationException(
                "Dashboard authentication is enabled but the local operator account is missing.");
        }

        state.Set(settings.AuthenticationEnabled, settings.ShowDisabledWarning);
    }
}
