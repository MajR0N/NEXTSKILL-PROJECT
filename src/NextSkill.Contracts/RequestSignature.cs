using System.Security.Cryptography;
using System.Text;

namespace NextSkill.Contracts;

/// <summary>Why a signature was rejected. <see cref="Valid"/> means accept.</summary>
public enum SignatureResult
{
    Valid,
    Malformed,
    Mismatch,
    /// <summary>Outside the replay window — too old, or implausibly far in the future.</summary>
    Expired,
}

/// <summary>
/// HMAC-SHA256 request signing, shared by the panel and the plugin so both sides
/// compute it identically (decisions 009, 012).
///
/// <b>Why HMAC and not a bearer token:</b> a bearer token is sent with every
/// request, so a leak is total compromise. An HMAC signature proves the sender
/// knows a shared secret <i>without ever transmitting it</i>, and because the
/// signature covers the body it also proves the message was not altered.
/// The timestamp stops a captured request being replayed later.
/// </summary>
public static class RequestSignature
{
    /// <summary>How far apart the two clocks may be before a request is refused.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Signs the raw body together with a Unix-seconds timestamp.
    ///
    /// The timestamp is inside the signed payload, not merely alongside it —
    /// otherwise an attacker could keep a captured body and simply present a
    /// fresh timestamp.
    /// </summary>
    public static string Compute(byte[] body, long unixSeconds, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var prefix = Encoding.UTF8.GetBytes($"{unixSeconds}.");
        var buffer = new byte[prefix.Length + body.Length];
        Buffer.BlockCopy(prefix, 0, buffer, 0, prefix.Length);
        Buffer.BlockCopy(body, 0, buffer, prefix.Length, body.Length);
        return Convert.ToHexString(hmac.ComputeHash(buffer)).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a signature against the <b>raw bytes as received</b>.
    ///
    /// Never verify against a re-serialised object: property order, whitespace
    /// and number formatting can all differ, so it would fail for honest
    /// requests and — worse — could pass for altered ones.
    /// </summary>
    public static SignatureResult Verify(
        byte[] body,
        string? presentedSignature,
        string? presentedTimestamp,
        string secret,
        DateTimeOffset now,
        TimeSpan? window = null)
    {
        if (string.IsNullOrWhiteSpace(presentedSignature) || string.IsNullOrWhiteSpace(presentedTimestamp))
            return SignatureResult.Malformed;

        if (!long.TryParse(presentedTimestamp, out var unixSeconds))
            return SignatureResult.Malformed;

        // Check the clock BEFORE the HMAC: an expired request is rejected
        // without spending CPU on a hash an attacker controls the input to.
        var age = now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        var allowed = window ?? DefaultWindow;
        if (age > allowed || age < -allowed)
            return SignatureResult.Expired;

        var expected = Compute(body, unixSeconds, secret);

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(presentedSignature.ToLowerInvariant());
        if (a.Length != b.Length) return SignatureResult.Mismatch;

        // CONSTANT TIME. A plain string comparison stops at the first differing
        // byte, so response timing leaks how much of the signature was correct —
        // enough to reconstruct it one byte at a time.
        return CryptographicOperations.FixedTimeEquals(a, b)
            ? SignatureResult.Valid
            : SignatureResult.Mismatch;
    }
}
