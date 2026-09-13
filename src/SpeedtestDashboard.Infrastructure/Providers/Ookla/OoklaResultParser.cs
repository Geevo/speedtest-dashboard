using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public sealed class OoklaResultParser
{
    private const int MaximumTextLength = 256;

    public SpeedTestResult Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new OoklaOutputException("Ookla returned no result data.");
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || GetOptionalText(root, "type") != "result")
            {
                throw new OoklaOutputException("Ookla returned an unexpected result document.");
            }

            var ping = RequireObject(root, "ping");
            var download = RequireObject(root, "download");
            var upload = RequireObject(root, "upload");
            var server = RequireObject(root, "server");
            var result = GetOptionalObject(root, "result");
            var networkInterface = GetOptionalObject(root, "interface");

            var idleLatency = RequireNonNegativeDecimal(ping, "latency");
            var jitter = RequireNonNegativeDecimal(ping, "jitter");
            var downloadBandwidth = RequireNonNegativeDecimal(download, "bandwidth");
            var uploadBandwidth = RequireNonNegativeDecimal(upload, "bandwidth");
            var serverId = RequireServerId(server);
            var serverName = RequireText(server, "name");
            var serverLocation = RequireText(server, "location");
            var serverCountry = GetOptionalText(server, "country");
            var packetLoss = GetOptionalNonNegativeDecimal(root, "packetLoss");
            if (packetLoss > 100)
            {
                throw new OoklaOutputException("Ookla returned an impossible packet-loss measurement.");
            }

            var resultUrl = result is null ? null : ValidateResultUrl(GetOptionalText(result.Value, "url"));
            var metadata = JsonSerializer.Serialize(new
            {
                timestamp = GetOptionalTimestamp(root),
                isp = GetOptionalText(root, "isp"),
                pingLow = GetOptionalNonNegativeDecimal(ping, "low"),
                pingHigh = GetOptionalNonNegativeDecimal(ping, "high"),
                downloadBytes = GetOptionalNonNegativeDecimal(download, "bytes"),
                downloadElapsedMilliseconds = GetOptionalNonNegativeDecimal(download, "elapsed"),
                downloadLatency = ParseLatency(GetOptionalObject(download, "latency")),
                uploadBytes = GetOptionalNonNegativeDecimal(upload, "bytes"),
                uploadElapsedMilliseconds = GetOptionalNonNegativeDecimal(upload, "elapsed"),
                uploadLatency = ParseLatency(GetOptionalObject(upload, "latency")),
                internalIp = networkInterface is null ? null : GetOptionalText(networkInterface.Value, "internalIp"),
                ooklaReportedExternalIp = networkInterface is null ? null : GetOptionalText(networkInterface.Value, "externalIp"),
                serverHost = GetOptionalText(server, "host"),
                serverPort = GetOptionalNonNegativeDecimal(server, "port"),
                serverIp = GetOptionalText(server, "ip"),
                resultId = result is null ? null : GetOptionalText(result.Value, "id"),
                resultPersisted = result is null ? null : GetOptionalBoolean(result.Value, "persisted")
            });

            return new SpeedTestResult(
                OoklaProviderDefinition.Id,
                serverId,
                serverName,
                CombineLocation(serverLocation, serverCountry),
                BytesPerSecondToMbps(downloadBandwidth),
                BytesPerSecondToMbps(uploadBandwidth),
                idleLatency,
                jitter,
                packetLoss,
                resultUrl,
                ProviderMetadataJson: metadata);
        }
        catch (OoklaOutputException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new OoklaOutputException($"Ookla returned malformed JSON: {exception.Message}");
        }
        catch (OverflowException exception)
        {
            throw new OoklaOutputException($"Ookla returned a numeric value outside the supported range: {exception.Message}");
        }
    }

    internal static decimal BytesPerSecondToMbps(decimal bytesPerSecond) => bytesPerSecond / 125_000m;

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new OoklaOutputException($"Ookla result is missing the required '{name}' object.");
        }

        return value;
    }

    private static JsonElement? GetOptionalObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static decimal RequireNonNegativeDecimal(JsonElement parent, string name) =>
        GetOptionalNonNegativeDecimal(parent, name) ??
        throw new OoklaOutputException($"Ookla result is missing the required '{name}' measurement.");

    private static decimal? GetOptionalNonNegativeDecimal(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number) || number < 0)
        {
            throw new OoklaOutputException($"Ookla returned an invalid '{name}' measurement.");
        }

        return number;
    }

    private static string RequireServerId(JsonElement server)
    {
        if (!server.TryGetProperty("id", out var value))
        {
            throw new OoklaOutputException("Ookla result is missing the server ID.");
        }

        var id = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        if (!OoklaServerId.IsValid(id))
        {
            throw new OoklaOutputException("Ookla returned an invalid server ID.");
        }

        return id!;
    }

    private static string RequireText(JsonElement parent, string name) =>
        GetOptionalText(parent, name) ??
        throw new OoklaOutputException($"Ookla result is missing the required '{name}' value.");

    private static string? GetOptionalText(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new OoklaOutputException($"Ookla returned an invalid '{name}' value.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Length > MaximumTextLength || text.Any(char.IsControl))
        {
            throw new OoklaOutputException($"Ookla returned an invalid '{name}' value.");
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
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            throw new OoklaOutputException("Ookla returned an invalid timestamp.");
        }

        return parsed.ToUniversalTime();
    }

    private static bool? GetOptionalBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new OoklaOutputException($"Ookla returned an invalid '{name}' value.")
        };
    }

    private static object? ParseLatency(JsonElement? latency)
    {
        if (latency is null)
        {
            return null;
        }

        return new
        {
            iqm = GetOptionalNonNegativeDecimal(latency.Value, "iqm"),
            low = GetOptionalNonNegativeDecimal(latency.Value, "low"),
            high = GetOptionalNonNegativeDecimal(latency.Value, "high"),
            jitter = GetOptionalNonNegativeDecimal(latency.Value, "jitter")
        };
    }

    private static string? ValidateResultUrl(string? value)
    {
        if (value is null || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            (uri.IdnHost != "www.speedtest.net" && uri.IdnHost != "speedtest.net") ||
            !uri.AbsolutePath.StartsWith("/result/", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static string CombineLocation(string location, string? country) =>
        country is null || location.Contains(country, StringComparison.OrdinalIgnoreCase)
            ? location
            : $"{location}, {country}";
}
