using System.Globalization;

namespace SpeedtestDashboard.Api.Hosting;

internal static class DashboardPortConfiguration
{
    public const string EnvironmentVariableName = "DASHBOARD_PORT";

    public static string? GetListenUrl(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        var candidate = configuredValue.Trim();
        if (!int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"{EnvironmentVariableName} must be a whole number between 1 and 65535.");
        }

        return $"http://*:{port.ToString(CultureInfo.InvariantCulture)}";
    }
}
