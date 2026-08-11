namespace NextSkill.Domain.Auth;

/// <summary>
/// How an account proves the second factor (decision 047).
///
/// <b>Not equally good, and the product should not pretend otherwise.</b> TOTP
/// is offline, free and unaffected by a hijacked phone number. SMS depends on a
/// paid gateway and is defeated by a SIM swap, which is a routine attack on
/// anyone worth attacking. SMS exists because some people will not install an
/// app — not because it is as strong.
/// </summary>
public enum TwoFactorMethod
{
    /// <summary>RFC 6238 — Authy, Google Authenticator, 1Password, Aegis, any of them.</summary>
    Totp,

    /// <summary>A code by text message. Weaker; see above.</summary>
    Sms,
}
