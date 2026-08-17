using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// Login (decisions 011, 029, 040).
///
/// Two rules run through everything here:
/// <list type="bullet">
/// <item>the failure message never reveals whether the address exists, and</item>
/// <item>the response takes the same time whether it exists or not.</item>
/// </list>
/// The second is the one usually forgotten — identical wording is pointless if
/// a wrong password takes 60ms of Argon2 and an unknown address returns in 2ms.
/// </summary>
[EnableRateLimiting(Sessions.AuthRateLimitPolicy)]
public class LoginModel(NextSkillDb db, TwoFactorLogin pending) : PageModel
{
    public sealed class Form
    {
        [Required(ErrorMessage = "Podaj adres e-mail.")]
        [EmailAddress(ErrorMessage = "To nie wygląda na adres e-mail.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Podaj hasło.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    [BindProperty] public Form Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>Set by the reset flow, which redirects here rather than signing anybody in.</summary>
    [BindProperty(SupportsGet = true)] public bool Reset { get; set; }

    /// <summary>Confirms that the account-wide session revocation completed.</summary>
    [BindProperty(SupportsGet = true)] public bool AllSessionsRevoked { get; set; }

    /// <summary>Confirms that identifying account data was erased.</summary>
    [BindProperty(SupportsGet = true)] public bool AccountDeleted { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.DeletedAt == null && a.Email == email);

        // No account: still run a full Argon2 verification against a throwaway
        // hash and throw the answer away. The work is wasted on purpose — it is
        // what makes an unknown address indistinguishable from a wrong password
        // by stopwatch.
        var ok = Passwords.Verify(Input.Password, account?.PasswordHash ?? DecoyHash.Value);

        if (account is null || !ok)
        {
            // One message for both cases, and deliberately not "wrong password"
            // — that phrasing confirms the address exists (040).
            ModelState.AddModelError(string.Empty, "Nieprawidłowy e-mail lub hasło.");
            return Page();
        }

        // Checked only AFTER the password verified, and that ordering is the
        // whole point. Telling an unauthenticated caller "this account is
        // banned" would confirm the address exists, which is the enumeration
        // leak 040 spends the rest of this method closing. Someone who has just
        // proved they own the account learns nothing new by being told why they
        // cannot get in — and a sanction nobody can understand cannot be
        // appealed (015).
        if (account.BannedAt is not null)
        {
            ModelState.AddModelError(string.Empty,
                string.IsNullOrWhiteSpace(account.BanReason)
                    ? "To konto zostało zablokowane."
                    : $"To konto zostało zablokowane: {account.BanReason}");
            return Page();
        }

        // A password is one factor. If the account has a second, the login is
        // NOT finished — no session is created, and the pending state travels in
        // its own short-lived cookie that grants nothing (047).
        if (account.TwoFactorMethod is not null)
        {
            pending.Begin(HttpContext, account.Id);

            var returnTo = Sessions.SafeReturnUrl(ReturnUrl, Locales.Current);
            return Redirect($"/{Locales.Current}/account/challenge?returnUrl={Uri.EscapeDataString(returnTo)}");
        }

        if (!await Sessions.SignInAsync(HttpContext, db, account))
        {
            ModelState.AddModelError(string.Empty, "Nieprawidłowy e-mail lub hasło.");
            return Page();
        }

        return Redirect(Sessions.SafeReturnUrl(ReturnUrl, Locales.Current));
    }

    /// <summary>
    /// A real Argon2id hash of a value nobody knows, computed once at startup.
    /// Verifying against it costs exactly what verifying a real account costs.
    /// </summary>
    private static class DecoyHash
    {
        public static readonly string Value = Passwords.Hash(Guid.NewGuid().ToString());
    }
}
