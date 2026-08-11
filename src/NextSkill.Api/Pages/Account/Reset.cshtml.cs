using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;

// The namespace `...Pages.Account` shadows the Account ENTITY; the alias is the fix.
using AccountEntity = NextSkill.Api.Shared.Persistence.Account;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// Redeem a reset link and set a new password (decision 011).
///
/// <b>A reset does not log anybody in.</b> It ends with a redirect to the login
/// page — so setting a password proves nothing until it is used, and, more
/// importantly, <b>the second factor still applies</b>. If reset auto-signed
/// people in, anyone who took over an email account would walk straight past
/// two-factor authentication and it would be worth nothing.
/// </summary>
[EnableRateLimiting(Sessions.AuthRateLimitPolicy)]
public class ResetModel(NextSkillDb db) : PageModel
{
    public sealed class Form
    {
        [Required]
        public string Token { get; set; } = "";

        [Required(ErrorMessage = "Ustaw nowe hasło.")]
        [StringLength(200, MinimumLength = 10, ErrorMessage = "Hasło musi mieć co najmniej 10 znaków.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    [BindProperty] public Form Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }

    /// <summary>
    /// True when the link is expired, already used, or never existed. All three
    /// look the same on purpose — and none of them is a dead end.
    /// </summary>
    public bool LinkDead { get; private set; }

    public async Task OnGetAsync()
    {
        Input.Token = Token ?? "";
        LinkDead = await FindAccountAsync(Input.Token) is null;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            LinkDead = await FindAccountAsync(Input.Token) is null;
            return Page();
        }

        var account = await FindAccountAsync(Input.Token);
        if (account is null)
        {
            LinkDead = true;
            return Page();
        }

        account.PasswordHash = Passwords.Hash(Input.Password);

        // Single use, enforced by the row being gone rather than by a flag every
        // future query would have to remember to check.
        await db.PasswordResetTokens.Where(t => t.AccountId == account.Id).ExecuteDeleteAsync();

        // Every existing session dies. Whoever forced this reset — or was
        // already logged in on a stolen laptop — is signed out by the act of
        // recovering the account. This is the sessions table earning its keep
        // (011): with a self-contained cookie there would be nothing to revoke.
        await db.Sessions.Where(s => s.AccountId == account.Id).ExecuteDeleteAsync();

        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/account/login?reset=1");
    }

    /// <summary>
    /// Resolves a token to its account, or null.
    ///
    /// The lookup is by <b>hash</b>: what arrives is hashed and matched, so the
    /// stored value is never a working credential. Expiry is part of the query
    /// rather than a later check, because a later check is one someone can add a
    /// code path around.
    /// </summary>
    private async Task<AccountEntity?> FindAccountAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = ResetTokens.HashOf(token);

        var accountId = await db.PasswordResetTokens
            .Where(t => t.TokenHash == hash && t.ExpiresAt > DateTimeOffset.UtcNow)
            .Select(t => (Guid?)t.AccountId)
            .FirstOrDefaultAsync();

        return accountId is null ? null : await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
    }
}
