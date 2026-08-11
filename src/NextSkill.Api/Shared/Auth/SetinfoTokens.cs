using System.Security.Cryptography;
using System.Text;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Persistent CS 1.6 identity tokens (decision 011).
///
/// A token is random rather than human-chosen, so SHA-256 is appropriate: the
/// 256-bit input has no dictionary to slow down. Only the hash is stored. The
/// plaintext exists long enough to render one copy-ready command after a
/// rotation and can never be retrieved later.
/// </summary>
public static class SetinfoTokens
{
    public const string InfoKey = "_ns";
    public const int EntropyBytes = 32;

    /// <summary>Returns the one-time plaintext token and the value safe to store.</summary>
    public static (string Token, string Hash) Create()
    {
        // Base64url has no whitespace, quotes or command separators, so the
        // value can be pasted inside a quoted GoldSrc console command as-is.
        var token = Base64Url(RandomNumberGenerator.GetBytes(EntropyBytes));
        return (token, HashOf(token));
    }

    public static string Command(string token) => $"setinfo {InfoKey} \"{token}\"";

    public static string HashOf(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Constant-time comparison for the plugin binding slice. Malformed stored
    /// data fails closed rather than turning one damaged account into a 500.
    /// </summary>
    public static bool Verify(string token, string storedHash)
    {
        try
        {
            var expected = Convert.FromBase64String(storedHash);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return expected.Length == actual.Length
                && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
