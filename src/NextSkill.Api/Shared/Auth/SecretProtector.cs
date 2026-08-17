using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Encryption at rest for the secrets that must be <b>recoverable</b>
/// (decision 012).
///
/// Passwords, <c>setinfo</c> tokens and reset tokens are hashed, because we only
/// ever need to <i>verify</i> them. Two values cannot be: an HMAC secret has to
/// be replayed to check a plugin's signature, and a TOTP secret has to be
/// replayed to derive the current code. Hashing them is impossible, so they are
/// encrypted instead — with a key ASP.NET Data Protection holds <b>outside the
/// database</b>, which is the property that matters. A stolen dump alone no
/// longer yields a signing credential or the ability to mint second factors.
///
/// Same mechanism as <c>DiscordWebhookProtector</c>, deliberately: one way of
/// doing this, not two.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider protection)
{
    /// <summary>
    /// Marks a value as encrypted.
    ///
    /// A prefix rather than try-decrypt-and-catch: exception-driven control flow
    /// would make a genuinely corrupt value indistinguishable from a legacy
    /// plaintext one, and quietly treat the first as the second.
    /// </summary>
    private const string Prefix = "dp1:";

    private readonly IDataProtector _protector =
        protection.CreateProtector("NextSkill.StoredSecret.v1");

    public string Protect(string plaintext) => Prefix + _protector.Protect(plaintext);

    public static bool IsProtected(string? stored) =>
        stored?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// The usable secret, or null when it cannot be recovered.
    ///
    /// An unprefixed value is returned as-is: it predates this class and is
    /// still plaintext in the database. <see cref="BackfillAsync"/> closes that
    /// window at startup, so the fallback only covers the moment between a
    /// deployment and its first boot — not an indefinite tolerance for
    /// unencrypted secrets.
    /// </summary>
    public string? Reveal(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        if (!IsProtected(stored)) return stored;

        try
        {
            return _protector.Unprotect(stored[Prefix.Length..]);
        }
        catch (CryptographicException)
        {
            // Wrong key ring, or a tampered value. Failing closed means the
            // plugin gets a 401 and the second factor refuses — both loud.
            return null;
        }
    }

    /// <summary>
    /// Encrypts anything still stored in the clear, once, at startup.
    ///
    /// Deliberately not a database migration: a migration has no access to the
    /// application's key ring, so this is the only place it can happen. Idempotent
    /// by the prefix — a second boot finds nothing to do.
    /// </summary>
    public async Task<int> BackfillAsync(NextSkillDb db, CancellationToken ct = default)
    {
        var changed = 0;

        var servers = await db.Servers.IgnoreQueryFilters()
            .Where(s => s.HmacSecret != null)
            .ToListAsync(ct);

        foreach (var server in servers.Where(s => !IsProtected(s.HmacSecret)))
        {
            server.HmacSecret = Protect(server.HmacSecret!);
            changed++;
        }

        var accounts = await db.Accounts
            .Where(a => a.TwoFactorSecret != null)
            .ToListAsync(ct);

        foreach (var account in accounts.Where(a => !IsProtected(a.TwoFactorSecret)))
        {
            account.TwoFactorSecret = Protect(account.TwoFactorSecret!);
            changed++;
        }

        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }
}
