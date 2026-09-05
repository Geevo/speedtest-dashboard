using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SpeedtestDashboard.Core.Network;

namespace SpeedtestDashboard.Infrastructure.Network;

public sealed class IpifyPublicIpResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<IpifyPublicIpResolver> logger) : IPublicIpResolver
{
    public const string IPv4ClientName = "network-identity-ipify-v4";
    public const string IPv6ClientName = "network-identity-ipify-v6";

    public Task<IPAddress?> ResolveIPv4Async(CancellationToken cancellationToken) =>
        ResolveAsync(IPv4ClientName, AddressFamily.InterNetwork, "IPv4", cancellationToken);

    public Task<IPAddress?> ResolveIPv6Async(CancellationToken cancellationToken) =>
        ResolveAsync(IPv6ClientName, AddressFamily.InterNetworkV6, "IPv6", cancellationToken);

    private async Task<IPAddress?> ResolveAsync(
        string clientName,
        AddressFamily expectedFamily,
        string familyLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClientFactory
                .CreateClient(clientName)
                .GetAsync(string.Empty, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Public {AddressFamily} lookup returned HTTP {StatusCode}.",
                    familyLabel,
                    (int)response.StatusCode);
                return null;
            }

            var rawAddress = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (rawAddress.Length is 0 or > 64 ||
                !IPAddress.TryParse(rawAddress, out var address) ||
                address.AddressFamily != expectedFamily ||
                !PublicIpAddressValidator.IsPublic(address))
            {
                logger.LogWarning("Public {AddressFamily} lookup returned an invalid address.", familyLabel);
                return null;
            }

            return address;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Public {AddressFamily} lookup timed out.", familyLabel);
            return null;
        }
        catch (HttpRequestException)
        {
            logger.LogDebug("Public {AddressFamily} lookup is unavailable.", familyLabel);
            return null;
        }
    }
}
