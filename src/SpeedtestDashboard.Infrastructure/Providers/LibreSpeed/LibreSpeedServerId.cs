using System.Globalization;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public static class LibreSpeedServerId
{
    public static bool IsValid(string? value) =>
        value is not null &&
        value.Length is >= 1 and <= 10 &&
        value[0] is >= '1' and <= '9' &&
        value.All(character => character is >= '0' and <= '9') &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
        parsed > 0;
}
