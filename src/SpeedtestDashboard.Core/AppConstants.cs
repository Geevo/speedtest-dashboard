using System.Reflection;

namespace SpeedtestDashboard.Core;

public static class AppConstants
{
    public const string ServiceName = "Speedtest Dashboard";
    public const long MaxRequestBodySizeBytes = 1024 * 1024;

    public static string Version { get; } = GetVersion();

    private static string GetVersion()
    {
        var informationalVersion = typeof(AppConstants).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return informationalVersion?.Split('+', 2)[0] ?? "0.0.0";
    }
}
