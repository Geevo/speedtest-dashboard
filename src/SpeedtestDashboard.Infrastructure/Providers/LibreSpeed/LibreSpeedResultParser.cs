using System.Globalization;
using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public sealed class LibreSpeedResultParser
{
    private const int MaximumTextLength = 512;

    public SpeedTestResult Parse(
        string output,
        string? requestedServerId = null,
        IReadOnlyList<LibreSpeedServerDefinition>? catalog = null)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new LibreSpeedOutputException("LibreSpeed returned no result data.");
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 24
            });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new LibreSpeedOutputException("LibreSpeed returned an unexpected result document.");
            }

            var results = document.RootElement.EnumerateArray().ToArray();
            if (results.Length == 0)
            {
                throw new LibreSpeedOutputException("LibreSpeed returned no test results.");
            }

            if (results.Length != 1 || results[0].ValueKind != JsonValueKind.Object)
            {
                throw new LibreSpeedOutputException("LibreSpeed returned more than one logical test result.");
            }

            var root = results[0];
            var server = RequireObject(root, "server");
            var serverName = RequireText(server, "name");
            var serverUrl = RequireText(server, "url");
            if (!LibreSpeedServerCatalogParser.TryParseServerUri(serverUrl, out var resultServerUri))
            {
                throw new LibreSpeedOutputException("LibreSpeed returned an invalid result server URL.");
            }

            var downloadMbps = RequireNonNegativeDecimal(root, "download");
            var uploadMbps = RequireNonNegativeDecimal(root, "upload");
            var pingMilliseconds = RequireNonNegativeDecimal(root, "ping");
            var jitterMilliseconds = RequireNonNegativeDecimal(root, "jitter");
            var matchedServer = ResolveServer(requestedServerId, serverName, resultServerUri, catalog ?? []);
            var serverId = requestedServerId ?? matchedServer?.Server.Id;
            var client = GetOptionalObject(root, "client");

            var metadata = JsonSerializer.Serialize(new
            {
                timestamp = GetOptionalTimestamp(root),
                bytesSent = GetOptionalUnsignedInteger(root, "bytes_sent"),
                bytesReceived = GetOptionalUnsignedInteger(root, "bytes_received"),
                libreSpeedReportedClientIp = client is null ? null : GetOptionalText(client.Value, "ip"),
                libreSpeedReportedClientOrganization = client is null ? null : GetOptionalText(client.Value, "org"),
                serverUrl = resultServerUri.AbsoluteUri,
                httpPing = true
            });

            return new SpeedTestResult(
                LibreSpeedProviderDefinition.Id,
                serverId,
                serverName,
                matchedServer?.Server.Location,
                downloadMbps,
                uploadMbps,
                pingMilliseconds,
                jitterMilliseconds,
                PacketLossPercent: null,
                ResultUrl: null,
                ProviderMetadataJson: metadata);
        }
        catch (LibreSpeedOutputException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned malformed JSON: {exception.Message}");
        }
        catch (OverflowException exception)
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned a numeric value outside the supported range: {exception.Message}");
        }
    }

    private static LibreSpeedServerDefinition? ResolveServer(
        string? requestedServerId,
        string resultName,
        Uri resultUri,
        IReadOnlyList<LibreSpeedServerDefinition> catalog)
    {
        if (requestedServerId is not null)
        {
            return catalog.SingleOrDefault(server => server.Server.Id == requestedServerId);
        }

        var normalizedResultUri = NormalizeServerUri(resultUri);
        var uriMatches = catalog
            .Where(server => NormalizeServerUri(server.ServerUri) == normalizedResultUri)
            .Take(2)
            .ToArray();
        if (uriMatches.Length == 1)
        {
            return uriMatches[0];
        }

        var nameMatches = catalog
            .Where(server => string.Equals(server.CatalogName, resultName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return nameMatches.Length == 1 ? nameMatches[0] : null;
    }

    private static string NormalizeServerUri(Uri uri)
    {
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port.ToString(CultureInfo.InvariantCulture)}";
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}{port}{path}";
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new LibreSpeedOutputException($"LibreSpeed result is missing the required '{name}' object.");
        }

        return value;
    }

    private static JsonElement? GetOptionalObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static decimal RequireNonNegativeDecimal(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var number) ||
            number < 0)
        {
            throw new LibreSpeedOutputException($"LibreSpeed result is missing or has an invalid '{name}' measurement.");
        }

        return number;
    }

    private static ulong? GetOptionalUnsignedInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var number))
        {
            throw new LibreSpeedOutputException($"LibreSpeed returned an invalid '{name}' value.");
        }

        return number;
    }

    private static string RequireText(JsonElement parent, string name) =>
        GetOptionalText(parent, name) ??
        throw new LibreSpeedOutputException($"LibreSpeed result is missing the required '{name}' value.");

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

    private static DateTimeOffset? GetOptionalTimestamp(JsonElement root)
    {
        var timestamp = GetOptionalText(root, "timestamp");
        if (timestamp is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new LibreSpeedOutputException("LibreSpeed returned an invalid timestamp.");
        }

        return parsed.ToUniversalTime();
    }
}
