using System.Net;
using System.Net.Sockets;

namespace RecipeScraper.Core.Security;

public static class SsrfGuard
{
    private static readonly HashSet<string> BlockedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost", "0.0.0.0", "::1", "metadata.google.internal",
    };

    public static bool IsBlockedTarget(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) return true;

        var hostname = url.Host;
        if (BlockedHostnames.Contains(hostname)) return true;
        if (hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

        // Reject IP-literal hosts in private/loopback/link-local ranges (e.g. cloud metadata
        // endpoints at 169.254.169.254) to reduce SSRF exposure from user-supplied URLs.
        if (IPAddress.TryParse(hostname, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = ip.GetAddressBytes();
            var a = octets[0];
            var b = octets[1];
            if (a is 127 or 10 or 0) return true;
            if (a == 169 && b == 254) return true;
            if (a == 172 && b is >= 16 and <= 31) return true;
            if (a == 192 && b == 168) return true;
        }

        return false;
    }
}
