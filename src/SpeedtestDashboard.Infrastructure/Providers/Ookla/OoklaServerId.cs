namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

internal static class OoklaServerId
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 10 || value[0] == '0')
        {
            return false;
        }

        return value.All(character => character is >= '0' and <= '9');
    }
}
