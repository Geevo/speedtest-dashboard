using System.Diagnostics.CodeAnalysis;

namespace SpeedtestDashboard.Core.Providers;

public readonly record struct ProviderId
{
    public static readonly ProviderId LibreSpeed = new("librespeed");
    public static readonly ProviderId Ookla = new("ookla");

    private ProviderId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static ProviderId Parse(string value)
    {
        if (!TryParse(value, out var providerId))
        {
            throw new FormatException("Provider IDs must be lowercase identifiers between 1 and 32 characters.");
        }

        return providerId;
    }

    public static bool TryParse(string? value, out ProviderId providerId)
    {
        providerId = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32 || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
            {
                return false;
            }
        }

        providerId = new ProviderId(value);
        return true;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out string? normalized, out ProviderId providerId)
    {
        normalized = null;
        if (!TryParse(value, out providerId))
        {
            return false;
        }

        normalized = providerId.Value;
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
