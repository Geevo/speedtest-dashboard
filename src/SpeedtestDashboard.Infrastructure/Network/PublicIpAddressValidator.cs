using System.Net;
using System.Net.Sockets;

namespace SpeedtestDashboard.Infrastructure.Network;

internal static class PublicIpAddressValidator
{
    public static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIPv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicIPv6(address),
            _ => false
        };
    }

    private static bool IsPublicIPv4(byte[] bytes)
    {
        var first = bytes[0];
        var second = bytes[1];
        var third = bytes[2];

        return first != 0 &&
            first != 10 &&
            first != 127 &&
            !(first == 100 && second is >= 64 and <= 127) &&
            !(first == 169 && second == 254) &&
            !(first == 172 && second is >= 16 and <= 31) &&
            !(first == 192 && second == 0 && third == 0) &&
            !(first == 192 && second == 0 && third == 2) &&
            !(first == 192 && second == 88 && third == 99) &&
            !(first == 192 && second == 168) &&
            !(first == 198 && second is 18 or 19) &&
            !(first == 198 && second == 51 && third == 100) &&
            !(first == 203 && second == 0 && third == 113) &&
            first < 224;
    }

    private static bool IsPublicIPv6(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6 || address.IsIPv6LinkLocal || address.IsIPv6Multicast ||
            address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        var isGlobalUnicast = (bytes[0] & 0xe0) == 0x20;
        var isDocumentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;

        return isGlobalUnicast && !isDocumentation;
    }
}

