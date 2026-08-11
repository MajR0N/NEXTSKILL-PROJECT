using System.Net;
using System.Security.Cryptography;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Produces the per-process partition key used by the authentication rate
/// limiter. The socket address is needed to slow one attacker down, but the
/// limiter does not need to retain the address itself. A random, unpersisted
/// HMAC key makes the partition stable only for this process lifetime.
/// </summary>
public static class IpAbuseKeys
{
    public static string For(IPAddress? address, ReadOnlySpan<byte> processKey) =>
        address is null
            ? "unknown"
            : Convert.ToHexString(HMACSHA256.HashData(processKey, address.GetAddressBytes()));
}
