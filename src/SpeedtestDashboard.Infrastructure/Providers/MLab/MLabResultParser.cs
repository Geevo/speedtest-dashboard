using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;
using SpeedtestDashboard.Infrastructure.Serialization;

namespace SpeedtestDashboard.Infrastructure.Providers.MLab;

public sealed class MLabResultParser
{
    public SpeedTestResult Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new MLabOutputException("M-Lab returned no result data.");
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 12
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new MLabOutputException("M-Lab returned an unexpected result document.");
            }

            var download = RequireObject(root, "Download");
            var upload = RequireObject(root, "Upload");
            var downloadMbps = RequireMeasurement(download, "Throughput", "Mbit/s");
            var uploadMbps = RequireMeasurement(upload, "Throughput", "Mbit/s");
            var downloadLatency = OptionalMeasurement(download, "Latency", "ms");
            var uploadLatency = OptionalMeasurement(upload, "Latency", "ms");
            var latency = downloadLatency ?? uploadLatency;
            var serverFqdn = OptionalBoundedString(root, "ServerFQDN", 253);
            var serverIp = OptionalBoundedString(root, "ServerIP", 64);
            var clientIp = OptionalBoundedString(root, "ClientIP", 64);
            var downloadId = OptionalBoundedString(download, "UUID", 128);
            var uploadId = OptionalBoundedString(upload, "UUID", 128);
            var retransmission = OptionalMeasurement(download, "Retransmission", "%");
            var metadata = JsonSerializer.Serialize(
                new MLabMetadata(
                    "ndt7",
                    "mlab-managed",
                    serverIp,
                    clientIp,
                    downloadId,
                    uploadId,
                    downloadLatency is not null ? "download-min-rtt" : uploadLatency is not null ? "upload-min-rtt" : null,
                    retransmission,
                    "measurement-lab-public"),
                InfrastructureJsonSerializerContext.Default.MLabMetadata);

            return new SpeedTestResult(
                MLabProviderDefinition.Id,
                ServerId: null,
                ServerName: serverFqdn ?? serverIp,
                ServerLocation: null,
                downloadMbps,
                uploadMbps,
                latency,
                JitterMilliseconds: null,
                PacketLossPercent: null,
                ResultUrl: null,
                ProviderMetadataJson: metadata);
        }
        catch (MLabOutputException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new MLabOutputException($"M-Lab returned malformed JSON: {exception.Message}");
        }
        catch (OverflowException exception)
        {
            throw new MLabOutputException($"M-Lab returned a numeric value outside the supported range: {exception.Message}");
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new MLabOutputException($"M-Lab result is missing the required '{name}' object.");
        }

        return value;
    }

    private static decimal RequireMeasurement(JsonElement parent, string name, string expectedUnit) =>
        OptionalMeasurement(parent, name, expectedUnit) ??
        throw new MLabOutputException($"M-Lab result is missing the required '{name}' measurement.");

    private static decimal? OptionalMeasurement(JsonElement parent, string name, string expectedUnit)
    {
        if (!parent.TryGetProperty(name, out var measurement) || measurement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (measurement.ValueKind != JsonValueKind.Object ||
            !measurement.TryGetProperty("Unit", out var unit) ||
            unit.ValueKind != JsonValueKind.String ||
            !string.Equals(unit.GetString(), expectedUnit, StringComparison.Ordinal) ||
            !measurement.TryGetProperty("Value", out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var number) ||
            number < 0)
        {
            throw new MLabOutputException($"M-Lab returned an invalid '{name}' measurement.");
        }

        return number;
    }

    private static string? OptionalBoundedString(JsonElement parent, string name, int maximumLength)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new MLabOutputException($"M-Lab returned an invalid '{name}' value.");
        }

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        if (result.Length > maximumLength || result.Any(char.IsControl))
        {
            throw new MLabOutputException($"M-Lab returned an invalid '{name}' value.");
        }

        return result;
    }
}
