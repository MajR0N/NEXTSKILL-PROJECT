using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Auth;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// The half-signed-in state between "password accepted" and "second factor
/// accepted" (decision 047).
///
/// <b>This state cannot be a session.</b> A session cookie means signed in, and
/// somebody who has only proved a password is not signed in. So the pending
/// login travels in its own short-lived, data-protected cookie that carries an
/// account id and an expiry and grants nothing else.
/// </summary>
public sealed class TwoFactorLogin(IDataProtectionProvider protection)
{
    public const string CookieName = "ns_2fa";

    /// <summary>
    /// Five minutes. Long enough to open an app or wait for a text, short
    /// enough that a pending login left on a shared machine is worthless.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // A purpose string binds the payload to this use. A token minted here can
    // never be read as anything else, even though the same key protects it.
    private IDataProtector Protector => protection.CreateProtector("NextSkill.TwoFactorLogin.v1");

    public void Begin(HttpContext http, Guid accountId)
    {
        var payload = $"{accountId:N}|{DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds()}";

        http.Response.Cookies.Append(CookieName, Protector.Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = Lifetime,
            IsEssential = true,
        });
    }

    /// <summary>The account waiting to finish signing in, or null.</summary>
    public Guid? PendingAccountId(HttpContext http)
    {
        if (http.Request.Cookies[CookieName] is not { } protectedValue) return null;

        string payload;
        try
        {
            payload = Protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            // Tampered, or protected for a different purpose. Either way it is
            // not a pending login.
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var accountId)
            || !long.TryParse(parts[1], out var expiry))
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(expiry) > DateTimeOffset.UtcNow ? accountId : null;
    }

    public void End(HttpContext http) => http.Response.Cookies.Delete(CookieName);
}

/// <summary>
/// Recovery codes: the way back in when the phone is gone (decision 047).
///
/// Without them, turning on two-factor is a way to lose an account — the exact
/// lockout password reset exists to prevent, reintroduced one layer up.
/// </summary>
public static class RecoveryCodes
{
    public const int Count = 10;

    /// <summary>
    /// Generates a fresh set, replacing any existing ones.
    ///
    /// Returned in plaintext <b>once</b>. Only hashes are stored, so there is no
    /// "show my codes again" — the same rotate-never-retrieve rule the
    /// <c>setinfo</c> token follows (011, 029).
    /// </summary>
    public static async Task<IReadOnlyList<string>> IssueAsync(NextSkillDb db, Guid accountId, CancellationToken ct = default)
    {
        await db.RecoveryCodes.Where(c => c.AccountId == accountId).ExecuteDeleteAsync(ct);

        var codes = new List<string>(Count);
        for (var i = 0; i < Count; i++)
        {
            // Grouped, because these get written on paper and typed back in.
            var code = $"{RandomNumberGenerator.GetInt32(10000, 100000)}-{RandomNumberGenerator.GetInt32(10000, 100000)}";
            codes.Add(code);

            db.RecoveryCodes.Add(new RecoveryCode
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                CodeHash = ResetTokens.HashOf(code),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return codes;
    }

    /// <summary>Redeems a code. Single use — the row is deleted, not flagged.</summary>
    public static async Task<bool> RedeemAsync(NextSkillDb db, Guid accountId, string code, CancellationToken ct = default)
    {
        var hash = ResetTokens.HashOf(code.Trim());

        var deleted = await db.RecoveryCodes
            .Where(c => c.AccountId == accountId && c.CodeHash == hash)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }
}

/// <summary>Short numeric codes delivered by SMS (decision 047).</summary>
public static class OneTimeCodes
{
    /// <summary>Five minutes, matching the reset link — one number for people to remember.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static async Task<string> IssueAsync(
        NextSkillDb db, Guid accountId, OneTimeCodePurpose purpose, CancellationToken ct = default)
    {
        // Any outstanding code for this purpose stops working: two live codes
        // double the guessing surface and help nobody.
        await db.OneTimeCodes.Where(c => c.AccountId == accountId && c.Purpose == purpose)
            .ExecuteDeleteAsync(ct);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        db.OneTimeCodes.Add(new OneTimeCode
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Purpose = purpose,
            CodeHash = ResetTokens.HashOf(code),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + Lifetime,
        });

        await db.SaveChangesAsync(ct);
        return code;
    }

    public static async Task<bool> RedeemAsync(
        NextSkillDb db, Guid accountId, OneTimeCodePurpose purpose, string code, CancellationToken ct = default)
    {
        var hash = ResetTokens.HashOf(code.Trim());

        var deleted = await db.OneTimeCodes
            .Where(c => c.AccountId == accountId && c.Purpose == purpose
                        && c.CodeHash == hash && c.ExpiresAt > DateTimeOffset.UtcNow)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }
}
