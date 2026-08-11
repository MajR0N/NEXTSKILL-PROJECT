using System.Security.Cryptography;
using System.Text;

namespace NextSkill.Domain.Auth;

/// <summary>
/// Time-based one-time passwords, RFC 6238 (decision 047).
///
/// <b>"Support Authy" means "implement TOTP".</b> Authy, Google Authenticator,
/// 1Password, Aegis and every other authenticator implement the same standard,
/// so there is nothing to integrate with and no vendor to depend on. Thirty
/// lines of stdlib buys support for all of them, offline, for free.
///
/// Pure and deterministic: given a secret and a moment, the code is fixed. That
/// is what makes it testable against the published RFC vectors instead of
/// against our own expectations — the same reason Glicko-2 is checked against
/// Glickman's worked example.
/// </summary>
public static class Totp
{
    /// <summary>Thirty seconds, as every authenticator app assumes.</summary>
    public const int StepSeconds = 30;

    public const int Digits = 6;

    /// <summary>
    /// How many steps either side of "now" are accepted.
    ///
    /// One step, so a code is valid for at most ~90 seconds. Phone clocks drift
    /// and people type slowly; zero tolerance produces support tickets, and a
    /// wide window is a bigger brute-force target for no benefit anyone feels.
    /// </summary>
    public const int WindowSteps = 1;

    /// <summary>A fresh 160-bit secret, Base32-encoded as authenticators expect.</summary>
    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>
    /// The <c>otpauth://</c> URI an authenticator scans or accepts by hand.
    ///
    /// The issuer appears twice — as a label prefix and as a parameter — because
    /// older apps read one and newer apps read the other, and getting it wrong
    /// means the entry shows up in someone's app called "Unknown".
    /// </summary>
    public static string ProvisioningUri(string issuer, string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>The code for one moment. Exposed for tests against the RFC vectors.</summary>
    public static string Compute(string secret, DateTimeOffset at)
    {
        var counter = at.ToUnixTimeSeconds() / StepSeconds;
        return ComputeForCounter(Base32Decode(secret), counter);
    }

    /// <summary>
    /// Whether a submitted code is currently valid.
    ///
    /// The comparison is <b>constant time</b>: a byte-by-byte string compare
    /// leaks how many leading digits were right, which turns a million-guess
    /// space into six thousand.
    /// </summary>
    public static bool Verify(string secret, string code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code)) return false;

        var submitted = code.Trim().Replace(" ", "");
        if (submitted.Length != Digits || !submitted.All(char.IsAsciiDigit)) return false;

        byte[] key;
        try { key = Base32Decode(secret); }
        catch (FormatException) { return false; }

        var counter = now.ToUnixTimeSeconds() / StepSeconds;
        var submittedBytes = Encoding.ASCII.GetBytes(submitted);

        var ok = false;
        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var candidate = Encoding.ASCII.GetBytes(ComputeForCounter(key, counter + offset));

            // No early exit: leaving the loop on the first match would leak
            // WHICH step matched through timing, which tells an attacker the
            // clock offset. Cheap to avoid, so avoid it.
            ok |= CryptographicOperations.FixedTimeEquals(candidate, submittedBytes);
        }

        return ok;
    }

    private static string ComputeForCounter(byte[] key, long counter)
    {
        Span<byte> message = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, counter);

        Span<byte> mac = stackalloc byte[20];
        HMACSHA1.HashData(key, message, mac);

        // Dynamic truncation, straight from RFC 4226 §5.4.
        var offset = mac[^1] & 0x0F;
        var binary =
            ((mac[offset] & 0x7F) << 24) |
            ((mac[offset + 1] & 0xFF) << 16) |
            ((mac[offset + 2] & 0xFF) << 8) |
            (mac[offset + 3] & 0xFF);

        return (binary % (int)Math.Pow(10, Digits)).ToString(new string('0', Digits));
    }

    // ── Base32 (RFC 4648, no padding) — what authenticator apps speak ──────

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string input)
    {
        var cleaned = input.Trim().TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var bytes = new List<byte>(cleaned.Length * 5 / 8);
        int buffer = 0, bits = 0;

        foreach (var c in cleaned)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException($"'{c}' is not valid Base32.");

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
