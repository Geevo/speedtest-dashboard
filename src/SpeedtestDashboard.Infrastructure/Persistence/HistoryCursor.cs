using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedtestDashboard.Infrastructure.Persistence;

internal readonly partial record struct HistoryCursor(DateTime CompletedAtUtc, long Id)
{
    private const int MaximumEncodedLength = 256;

    public string Encode()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(CompletedAtUtc.ToString("O"), Id),
            CursorJsonSerializerContext.Default.CursorPayload);
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? encoded, out HistoryCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > MaximumEncodedLength ||
            encoded.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            return false;
        }

        try
        {
            var normalized = encoded.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var bytes = Convert.FromBase64String(normalized);
            var payload = JsonSerializer.Deserialize(
                bytes,
                CursorJsonSerializerContext.Default.CursorPayload);
            if (payload is null || payload.Id <= 0 ||
                !DateTime.TryParseExact(
                    payload.CompletedAtUtc,
                    "O",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var completedAt) || completedAt.Kind != DateTimeKind.Utc)
            {
                return false;
            }

            cursor = new HistoryCursor(completedAt, payload.Id);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private sealed record CursorPayload(string CompletedAtUtc, long Id);

    [JsonSerializable(typeof(CursorPayload))]
    private sealed partial class CursorJsonSerializerContext : JsonSerializerContext;
}
