using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Recovers the real visitor address from behind a reverse proxy
/// (decisions 011, 042).
///
/// <b>The bug this closes.</b> The authentication limiter partitions on
/// <c>Connection.RemoteIpAddress</c>, which is correct when the visitor connects
/// directly and completely wrong behind Cloudflare — there, every request
/// arrives from a Cloudflare edge address, so the entire internet shares one
/// token bucket and a single attacker locks every user out of logging in. A
/// brute-force defence that becomes a denial-of-service tool is worse than none.
///
/// <b>Why an allow-list and not just "read the header".</b>
/// <c>X-Forwarded-For</c> is attacker-controlled. Trusting it unconditionally
/// would let anyone mint a fresh bucket per request by changing one header,
/// which removes the limiter entirely — a strictly worse failure than the one
/// being fixed. ASP.NET's forwarded-headers middleware only honours the header
/// when the request actually arrived from a configured network, so the two
/// halves have to ship together.
/// </summary>
public static class TrustedProxies
{
    /// <summary>
    /// Cloudflare's published edge ranges, as at 2026-08-17.
    ///
    /// Configurable through <c>Hosting:TrustedProxies</c> precisely because this
    /// list changes: pinning it in code alone would mean a Cloudflare expansion
    /// silently reverts everyone to sharing one bucket again. Empty by default
    /// in configuration, so a direct-connect deployment trusts nothing.
    ///
    /// ponytail: a static list refreshed by hand. Ceiling: a new Cloudflare
    /// range is treated as an untrusted client until somebody updates config —
    /// which fails SAFE (their edge address becomes the partition key) rather
    /// than open. Upgrade path: fetch and cache
    /// https://www.cloudflare.com/ips-v4 at startup, worth it once anyone is
    /// actually operating this.
    /// </summary>
    public static readonly string[] CloudflareRanges =
    [
        "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
        "141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
        "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
        "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
        "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32",
        "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32",
    ];

    /// <summary>
    /// Builds the middleware options from configured CIDR ranges.
    ///
    /// <b>The framework defaults are cleared deliberately.</b> ASP.NET trusts
    /// loopback out of the box, which is right for a local reverse proxy and
    /// wrong here: this application's own health checks and in-process test
    /// clients arrive on loopback, and none of them speaks for a visitor.
    /// </summary>
    public static void Configure(ForwardedHeadersOptions options, IEnumerable<string> cidrRanges)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        var networks = 0;
        foreach (var range in cidrRanges)
        {
            if (TryParseCidr(range, out var network))
            {
                options.KnownNetworks.Add(network!);
                networks++;
            }
        }

        // One hop, always. Cloudflare appends the visitor to X-Forwarded-For, so
        // the address we want is the last entry it added — walking further back
        // would start reading values the CLIENT supplied, which is exactly the
        // spoofing this is meant to prevent.
        options.ForwardLimit = 1;

        // With nothing trusted the middleware is inert, and the socket address
        // stands. That is the correct behaviour for a direct-connect deployment
        // and for the in-process test host.
        if (networks == 0) options.ForwardedHeaders = ForwardedHeaders.None;
    }

    private static bool TryParseCidr(string value, out Microsoft.AspNetCore.HttpOverrides.IPNetwork? network)
    {
        network = null;

        var slash = value.IndexOf('/');
        if (slash <= 0) return false;

        if (!IPAddress.TryParse(value[..slash], out var address)) return false;
        if (!int.TryParse(value[(slash + 1)..], out var prefix)) return false;

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefix < 0 || prefix > maxPrefix) return false;

        // The ASP.NET type, not System.Net's — the middleware's KnownNetworks
        // collection takes its own, and the two names collide.
        network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefix);
        return true;
    }
}
