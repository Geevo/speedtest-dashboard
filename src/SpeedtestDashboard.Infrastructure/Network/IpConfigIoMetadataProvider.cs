using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SpeedtestDashboard.Core.Network;

namespace SpeedtestDashboard.Infrastructure.Network;

public sealed partial class IpConfigIoMetadataProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<IpConfigIoMetadataProvider> logger) : IIpMetadataProvider
{
    public const string ClientName = "network-identity-ipconfig-io";
    private const string SourceName = "IPConfig.io";

    private static readonly HashSet<string> IsoCountryCodes = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name).TwoLetterISORegionName)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<IpMetadata?> GetMetadataAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"json?ip={Uri.EscapeDataString(address.ToString())}");

            using var response = await httpClientFactory
                .CreateClient(ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("IP metadata lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "ip", out var returnedAddress) ||
                !IPAddress.TryParse(returnedAddress, out var parsedAddress) ||
                !parsedAddress.Equals(address))
            {
                logger.LogWarning("IP metadata lookup returned an invalid or mismatched address.");
                return null;
            }

            var asn = GetOptionalString(root, "asn");
            if (asn is not null && !AsnPattern().IsMatch(asn))
            {
                asn = null;
            }

            var countryCode = GetOptionalString(root, "country_iso")?.ToUpperInvariant();
            var countryName = GetOptionalString(root, "country");
            if (countryCode is null || countryName is null || !IsoCountryCodes.Contains(countryCode))
            {
                countryCode = null;
                countryName = null;
            }

            return new IpMetadata(
                address,
                asn,
                GetOptionalString(root, "asn_org"),
                Isp: null,
                countryCode,
                countryName,
                Region: GetOptionalString(root, "region_name"),
                City: GetOptionalString(root, "city"),
                SourceName);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "IP metadata lookup returned malformed JSON.");
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("IP metadata lookup timed out.");
            return null;
        }
        catch (HttpRequestException)
        {
            logger.LogDebug("IP metadata lookup is unavailable.");
            return null;
        }
    }

    private static string? GetOptionalString(JsonElement root, string propertyName) =>
        TryGetString(root, propertyName, out var value) ? value : null;

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var candidate = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 256)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    [GeneratedRegex("^AS[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AsnPattern();
}
