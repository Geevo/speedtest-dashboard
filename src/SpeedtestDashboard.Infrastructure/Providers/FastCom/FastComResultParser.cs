using System.Text.Json;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Core.Tests;

namespace SpeedtestDashboard.Infrastructure.Providers.FastCom;

public sealed class FastComResultParser
{
    private const int MaximumErrorLength = 256;

    public SpeedTestResult Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new FastComOutputException("FAST.com returned no result data.");
        }

        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FastComOutputException("FAST.com returned an unexpected result document.");
            }

            var reportedError = ReadReportedError(root);
            if (reportedError is not null)
            {
                throw new FastComOutputException(reportedError, providerReportedFailure: true);
            }

            var downloadMbps = RequireNonNegativeDecimal(root, "download_mbps");
            var uploadMbps = RequireNonNegativeDecimal(root, "upload_mbps");
            var latencyMilliseconds = GetOptionalNonNegativeDecimal(root, "ping_ms");
            var metadata = JsonSerializer.Serialize(new
            {
                latencyMethod = "http-head",
                serverSelection = "fast.com-managed"
            });

            return new SpeedTestResult(
                ProviderId.FastCom,
                ServerId: null,
                ServerName: null,
                ServerLocation: null,
                downloadMbps,
                uploadMbps,
                latencyMilliseconds,
                JitterMilliseconds: null,
                PacketLossPercent: null,
                ResultUrl: null,
                ProviderMetadataJson: metadata);
        }
        catch (FastComOutputException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new FastComOutputException($"FAST.com returned malformed JSON: {exception.Message}");
        }
        catch (OverflowException exception)
        {
            throw new FastComOutputException($"FAST.com returned a numeric value outside the supported range: {exception.Message}");
        }
    }

    private static string? ReadReportedError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var value))
        {
            throw new FastComOutputException("FAST.com result is missing the required 'error' field.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FastComOutputException("FAST.com returned an invalid 'error' value.");
        }

        var error = value.GetString();
        if (string.IsNullOrWhiteSpace(error) || error.Length > MaximumErrorLength || error.Any(char.IsControl))
        {
            throw new FastComOutputException("FAST.com returned an invalid 'error' value.");
        }

        return error;
    }

    private static decimal RequireNonNegativeDecimal(JsonElement root, string name) =>
        GetOptionalNonNegativeDecimal(root, name) ??
        throw new FastComOutputException($"FAST.com result is missing the required '{name}' measurement.");

    private static decimal? GetOptionalNonNegativeDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number) || number < 0)
        {
            throw new FastComOutputException($"FAST.com returned an invalid '{name}' measurement.");
        }

        return number;
    }
}
