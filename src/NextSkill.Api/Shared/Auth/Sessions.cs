using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Session lifecycle: sign in, validate on every request, sign out
/// (decision 011).
///
/// <b>Why a database row and not just a cookie.</b> An ASP.NET cookie is signed,
/// so its contents cannot be forged — but it is also self-contained, which means
/// nothing server-side can stop it. Ban an account, change a password, click
/// "log out everywhere": the cookie carries on working until it expires. Pairing
/// the cookie with a row makes revocation a delete.
/// </summary>
public static class Sessions
{
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>Claim carrying the <c>sessions</c> row id, checked on every request.</summary>
    public const string SessionIdClaim = "ns:sid";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// Creates the session row and issues the cookie.
    ///
    /// A NEW session id is minted on every sign-in and the old cookie is
    /// replaced. That is session fixation defence: an attacker who plants a
    /// known session id in someone's browser before they log in ends up holding
    /// an id that is no longer the one in use.
    /// </summary>
    public static async Task SignInAsync(HttpContext http, NextSkillDb db, Account account)
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow + Lifetime,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        // Only the ids and the nickname go in the cookie. Roles are deliberately
        // NOT baked in: a promotion or a ban has to take effect on the next
        // request, not whenever the cookie happens to expire (036).
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Nickname),
            new Claim(SessionIdClaim, session.Id.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }

    /// <summary>Deletes the session row, then clears the cookie. Order matters.</summary>
    public static async Task SignOutAsync(HttpContext http, NextSkillDb db)
    {
        if (SessionIdOf(http.User) is { } id)
            await db.Sessions.Where(s => s.Id == id).ExecuteDeleteAsync();

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Runs on every authenticated request: does this cookie still correspond to
    /// a live session row? If not, the principal is rejected and the cookie
    /// cleared — which is what makes logout, bans and password changes take
    /// effect immediately.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var id = SessionIdOf(ctx.Principal);
        if (id is null)
        {
            ctx.RejectPrincipal();
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<NextSkillDb>();
        var alive = await db.Sessions
            .AnyAsync(s => s.Id == id && s.ExpiresAt > DateTimeOffset.UtcNow);

        if (!alive)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public static Guid? SessionIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    public static Guid? AccountIdOf(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Login URL in the caller's language, with a return path attached.</summary>
    public static string LoginUrl(HttpRequest request)
    {
        var returnTo = Uri.EscapeDataString(request.Path + request.QueryString);
        return $"/{Locales.Current}/account/login?returnUrl={returnTo}";
    }

    /// <summary>
    /// Where to go after a successful login.
    ///
    /// <b>Relative paths only</b> (011, 040). An open redirect turns our own
    /// login page into a credible phishing launcher: the victim sees a real
    /// NEXTSKILL URL and lands on the attacker's site. Anything not starting
    /// with a single <c>/</c> is discarded — including <c>//evil.com</c>, which
    /// browsers treat as protocol-relative and absolute.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl, string locale) =>
        returnUrl is not null
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : $"/{locale}";
}
