using System.Globalization;
using System.Text.Json;
using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public sealed record LibreSpeedServerDefinition(SpeedTestServer Server, string CatalogName, Uri ServerUri);

public sealed class LibreSpeedServerCatalogParser
{
    private const int MaximumTextLength = 512;

    public IReadOnlyList<LibreSpeedServerDefinition> Parse(string json, int maximumServers)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new LibreSpeedOutputException("LibreSpeed returned an empty server catalogue.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new LibreSpeedOutputException("LibreSpeed returned an unexpected server catalogue document.");
            }

            var results = new List<LibreSpeedServerDefinition>();
            var identifiers = new HashSet<int>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new LibreSpeedOutputException("LibreSpeed returned an invalid server catalogue entry.");
                }

                var id = RequirePositiveInteger(element, "id");
                if (!identifiers.Add(id))
                {
                    throw new LibreSpeedOutputException("LibreSpeed returned duplicate server IDs.");
                }

                var catalogName = RequireText(element, "name");
                var serverValue = RequireText(element, "server");
                if (!TryParseServerUri(serverValue, out var serverUri))
                {
                    throw new LibreSpeedOutputException("LibreSpeed returned an invalid server URL.");
                }

                var sponsor = GetOptionalText(element, "sponsorName");
                var displayName = sponsor ?? catalogName;
                results.Add(new LibreSpeedServerDefinition(
                    new SpeedTestServer(
                        LibreSpeedProviderDefinition.Id,
                        id.ToString(CultureInfo.InvariantCulture),
                        displayName,
                        sponsor,
                        catalogName,
                        CountryCode: null,
                        serverUri.IdnHost,
                        DistanceKilometres: null,
                        LatencyMilliseconds: null),
                    catalogName,
                    serverUri));

                if (results.Count == maximumServers)
                {
                    break;
                }
            }

            return results;
        }
        catch (LibreSpeedOutputException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned malformed server JSON: {exception.Message}");
        }
        catch (OverflowException exception)
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned a server value outside the supported range: {exception.Message}");
        }
    }

    private static int RequirePositiveInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var number) ||
            number <= 0)
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned an invalid '{name}' value.");
        }

        return number;
    }

    private static string RequireText(JsonElement parent, string name) =>
        GetOptionalText(parent, name) ??
        throw new LibreSpeedOutputException($"LibreSpeed catalogue is missing the required '{name}' value.");

    private static string? GetOptionalText(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned an invalid '{name}' value.");
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > MaximumTextLength || text.Any(char.IsControl))
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned an invalid '{name}' value.");
        }

        return text;
    }

    internal static bool TryParseServerUri(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsed.IdnHost) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
