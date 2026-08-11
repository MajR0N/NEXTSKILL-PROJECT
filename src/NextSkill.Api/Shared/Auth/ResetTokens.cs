using System.Security.Cryptography;
using System.Text;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Password-reset tokens (decision 011).
///
/// <b>Why these are hashed with SHA-256 and passwords are not.</b> A password is
/// human-chosen and therefore guessable, so it needs a slow, memory-hard hash to
/// make a stolen database expensive to attack. A reset token is 256 bits of
/// CSPRNG output: there is no dictionary, no reuse across sites, and no
/// structure to exploit. Slow hashing would buy nothing and cost 60ms on every
/// lookup.
///
/// <b>They are hashed all the same</b>, because a leaked database must not hand
/// an attacker a working reset link for every account that has one outstanding.
/// </summary>
public static class ResetTokens
{
    /// <summary>
    /// <b>Five minutes.</b> A reset link is a live credential sitting in a
    /// mailbox; the window is how long a stolen or forwarded one keeps working,
    /// and this is a deliberately short one.
    ///
    /// The cost is real and accepted: mail delivery can itself take a minute or
    /// two, so some legitimate users will follow an expired link. That is why
    /// expiry is <b>never a dead end</b> — the page says plainly what happened
    /// and offers a new link in one click, which turns the trade-off into a
    /// small annoyance rather than a lockout (020).
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long before another reset email will be sent for the same account.
    ///
    /// Without this, the form is a mailbomb aimed at anyone whose address you
    /// know — free, anonymous, and hosted by us ([040](040-threat-model.md)).
    /// The per-IP rate limit does not cover it, because the attacker's cost is
    /// one request and the victim's cost is one email.
    /// </summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromMinutes(1);

    /// <summary>Generates a token: the secret to email, and the hash to store.</summary>
    public static (string Token, string Hash) Create()
    {
        // URL-safe: the token travels in a link, and Base64's '+' and '/' get
        // mangled by mail clients that helpfully "fix" URLs.
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, HashOf(token));
    }

    public static string HashOf(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
