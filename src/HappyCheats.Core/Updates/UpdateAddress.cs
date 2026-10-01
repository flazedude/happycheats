using System.Net;
using System.Net.Sockets;

namespace HappyCheats.Core.Updates;

public static class UpdateAddress
{
    // HTTP is restricted to literal private IPv4 addresses or loopback. Releases still require
    // the locally pinned signing key and a matching SHA-256, including on a trusted LAN.
    public static bool IsLocalAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    public static Uri Validate(string value, string description = "Update URL")
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp &&
                (uri.IsLoopback || (IPAddress.TryParse(uri.Host, out var ip) && IsLocalAddress(ip))))))
            throw new InvalidDataException($"{description} must use HTTPS, or HTTP on a private LAN IPv4 address or localhost.");
        return uri;
    }
}
