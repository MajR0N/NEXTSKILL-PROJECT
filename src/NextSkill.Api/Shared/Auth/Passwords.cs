using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Argon2id password hashing (decision 011).
///
/// This is the one place the project accepts a dependency it could have hand-waved
/// around: .NET ships PBKDF2 but no Argon2, and 011 chose Argon2id deliberately
/// because it is memory-hard — an attacker with a stolen database and a GPU farm
/// gains far less than they would against an iteration-only hash.
///
/// <b>The threat model is "assume the database leaks"</b> (011). Correct hashing is
/// the only thing that makes that survivable, so this file is not a candidate for
/// simplification.
/// </summary>
public static class Passwords
{
    // OWASP's Argon2id baseline: 19 MiB, 2 iterations, parallelism 1. The memory
    // cost is the point — it is what a GPU cannot cheaply parallelise.
    private const int MemoryKib = 19 * 1024;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Hash format: <c>argon2id$v$m$t$p$salt$hash</c>, both parts base64.</summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt);
        return string.Join('$',
            "argon2id", 19, MemoryKib, Iterations, Parallelism,
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Verifies a password against a stored hash. Returns false rather than
    /// throwing on a malformed stored value: a corrupt row must fail the login,
    /// not 500 the login page and tell an attacker the row is interesting.
    /// </summary>
    public static bool Verify(string password, string stored)
    {
        // Argon2 THROWS on an empty password rather than hashing it. Model
        // validation should stop one reaching here, but a verifier that can
        // crash is a verifier that turns a blank field into a 500 — and a 500 on
        // one specific address is itself a signal. Fail closed instead.
        if (string.IsNullOrEmpty(password)) return false;

        var parts = stored.Split('$');
        if (parts.Length != 7 || parts[0] != "argon2id") return false;
        if (!int.TryParse(parts[2], out var memory) ||
            !int.TryParse(parts[3], out var iterations) ||
            !int.TryParse(parts[4], out var parallelism)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[5]);
            expected = Convert.FromBase64String(parts[6]);
        }
        catch (FormatException) { return false; }

        // Parameters come from the STORED hash, not from the constants above, so
        // raising the cost later does not lock out every existing account.
        var actual = Derive(password, salt, memory, iterations, parallelism, expected.Length);

        // Constant-time: a byte-by-byte comparison leaks how much of the hash
        // matched through timing, which is enough to reconstruct it.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(
        string password, byte[] salt,
        int memory = MemoryKib, int iterations = Iterations,
        int parallelism = Parallelism, int length = HashBytes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }
}
