using System.Net;
using System.Net.Sockets;

namespace Sendly.Application.Services;

public static class SearXngUrlSafety
{
    public static Uri ValidateAndNormalizeBaseUrl(string? baseUrl, bool allowPrivateNetworkHosts)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("SearXNG base URL is required.");
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("SearXNG base URL is invalid.");
        }

        EnsureSafeAbsoluteUri(uri, allowPrivateNetworkHosts);
        return new Uri(uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/");
    }

    public static async Task EnsureSafeDestinationAsync(
        Uri uri,
        bool allowPrivateNetworkHosts,
        CancellationToken cancellationToken)
    {
        EnsureSafeAbsoluteUri(uri, allowPrivateNetworkHosts);
        cancellationToken.ThrowIfCancellationRequested();

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Unable to resolve SearXNG host.");
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("Unable to resolve SearXNG host.");
        }

        foreach (var address in addresses)
        {
            if (!IsAddressAllowed(address, allowPrivateNetworkHosts))
            {
                throw new InvalidOperationException("SearXNG host resolves to a blocked network destination.");
            }
        }
    }

    public static void EnsureSafeAbsoluteUri(Uri uri, bool allowPrivateNetworkHosts)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("SearXNG URL must use http or https.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("SearXNG URL must not include credentials.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("SearXNG URL host is required.");
        }

        if (uri.IsLoopback || IsLocalHostName(uri.Host))
        {
            if (!allowPrivateNetworkHosts)
            {
                throw new InvalidOperationException("SearXNG loopback hosts are blocked unless private-network access is enabled.");
            }

            return;
        }

        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            if (!IsAddressAllowed(ip, allowPrivateNetworkHosts))
            {
                throw new InvalidOperationException("SearXNG host address is blocked.");
            }
        }
    }

    public static bool IsAddressAllowed(IPAddress address, bool allowPrivateNetworkHosts)
    {
        if (IPAddress.IsLoopback(address))
        {
            return allowPrivateNetworkHosts;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (IsCloudMetadata(bytes))
            {
                return false;
            }

            if (IsIpv4PrivateOrLinkLocal(bytes))
            {
                return allowPrivateNetworkHosts;
            }

            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || IsIpv6UniqueLocal(address))
            {
                return allowPrivateNetworkHosts;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                return IsAddressAllowed(address.MapToIPv4(), allowPrivateNetworkHosts);
            }

            return true;
        }

        return false;
    }

    private static bool IsLocalHostName(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(host, "localhost.localdomain", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIpv4PrivateOrLinkLocal(byte[] bytes)
    {
        if (bytes.Length != 4)
        {
            return true;
        }

        if (bytes[0] == 10)
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

        if (bytes[0] == 169 && bytes[1] == 254)
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

        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
        {
            return true;
        }

        return false;
    }

    private static bool IsCloudMetadata(byte[] bytes)
    {
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254 && bytes[2] == 169 && bytes[3] == 254;
    }

    private static bool IsIpv6UniqueLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc;
    }
}
