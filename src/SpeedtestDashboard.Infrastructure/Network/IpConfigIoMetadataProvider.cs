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
            if (countryCode is null || countryName is null || !IsIsoCountryCode(countryCode))
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

    // Keep validation independent of OS culture data. The NativeAOT container deliberately
    // uses invariant globalization, where enumerating specific cultures yields the invariant
    // culture and constructing RegionInfo from it throws during type initialization.
    private static bool IsIsoCountryCode(string value) => value is
        "AD" or "AE" or "AF" or "AG" or "AI" or "AL" or "AM" or "AO" or "AQ" or "AR" or "AS" or "AT" or
        "AU" or "AW" or "AX" or "AZ" or "BA" or "BB" or "BD" or "BE" or "BF" or "BG" or "BH" or "BI" or
        "BJ" or "BL" or "BM" or "BN" or "BO" or "BQ" or "BR" or "BS" or "BT" or "BV" or "BW" or "BY" or
        "BZ" or "CA" or "CC" or "CD" or "CF" or "CG" or "CH" or "CI" or "CK" or "CL" or "CM" or "CN" or
        "CO" or "CR" or "CU" or "CV" or "CW" or "CX" or "CY" or "CZ" or "DE" or "DJ" or "DK" or "DM" or
        "DO" or "DZ" or "EC" or "EE" or "EG" or "EH" or "ER" or "ES" or "ET" or "FI" or "FJ" or "FK" or
        "FM" or "FO" or "FR" or "GA" or "GB" or "GD" or "GE" or "GF" or "GG" or "GH" or "GI" or "GL" or
        "GM" or "GN" or "GP" or "GQ" or "GR" or "GS" or "GT" or "GU" or "GW" or "GY" or "HK" or "HM" or
        "HN" or "HR" or "HT" or "HU" or "ID" or "IE" or "IL" or "IM" or "IN" or "IO" or "IQ" or "IR" or
        "IS" or "IT" or "JE" or "JM" or "JO" or "JP" or "KE" or "KG" or "KH" or "KI" or "KM" or "KN" or
        "KP" or "KR" or "KW" or "KY" or "KZ" or "LA" or "LB" or "LC" or "LI" or "LK" or "LR" or "LS" or
        "LT" or "LU" or "LV" or "LY" or "MA" or "MC" or "MD" or "ME" or "MF" or "MG" or "MH" or "MK" or
        "ML" or "MM" or "MN" or "MO" or "MP" or "MQ" or "MR" or "MS" or "MT" or "MU" or "MV" or "MW" or
        "MX" or "MY" or "MZ" or "NA" or "NC" or "NE" or "NF" or "NG" or "NI" or "NL" or "NO" or "NP" or
        "NR" or "NU" or "NZ" or "OM" or "PA" or "PE" or "PF" or "PG" or "PH" or "PK" or "PL" or "PM" or
        "PN" or "PR" or "PS" or "PT" or "PW" or "PY" or "QA" or "RE" or "RO" or "RS" or "RU" or "RW" or
        "SA" or "SB" or "SC" or "SD" or "SE" or "SG" or "SH" or "SI" or "SJ" or "SK" or "SL" or "SM" or
        "SN" or "SO" or "SR" or "SS" or "ST" or "SV" or "SX" or "SY" or "SZ" or "TC" or "TD" or "TF" or
        "TG" or "TH" or "TJ" or "TK" or "TL" or "TM" or "TN" or "TO" or "TR" or "TT" or "TV" or "TW" or
        "TZ" or "UA" or "UG" or "UM" or "US" or "UY" or "UZ" or "VA" or "VC" or "VE" or "VG" or "VI" or
        "VN" or "VU" or "WF" or "WS" or "YE" or "YT" or "ZA" or "ZM" or "ZW";

    [GeneratedRegex("^AS[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AsnPattern();
}
