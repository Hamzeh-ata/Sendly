using System.Net;
using System.Net.Sockets;

namespace Sendly.Domain.Services;

public static class SsrfGuard
{
    public static bool IsBlockedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized is "localhost" or "metadata.google.internal")
        {
            return true;
        }

        if (normalized.EndsWith(".localhost", StringComparison.Ordinal)
            || normalized.EndsWith(".local", StringComparison.Ordinal)
            || normalized.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(normalized, out var ip))
        {
            return IsBlockedAddress(ip);
        }

        return false;
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10)
            {
                return true;
            }

            if (bytes[0] == 127)
            {
                return true;
            }

            if (bytes[0] == 0)
            {
                return true;
            }

            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return true;
            }

            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return true;
            }

            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return true;
            }
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsBlockedAddress(address.MapToIPv4());
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return true;
            }

            var bytes = address.GetAddressBytes();
            if (bytes.All(b => b == 0))
            {
                return true;
            }

            if ((bytes[0] & 0xfe) == 0xfc)
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<bool> IsBlockedUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return true;
        }

        if (IsBlockedHost(uri.Host))
        {
            return true;
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return IsBlockedAddress(literal);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            foreach (var address in addresses)
            {
                if (IsBlockedAddress(address))
                {
                    return true;
                }
            }

            return addresses.Length == 0;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}
