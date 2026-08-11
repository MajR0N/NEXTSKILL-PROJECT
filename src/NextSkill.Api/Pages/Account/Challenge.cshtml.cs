using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Sms;
using NextSkill.Domain.Auth;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// The second step of signing in (decision 047).
///
/// Reached only with a pending-login cookie, which proves a password was
/// accepted and grants nothing else. <b>No session exists yet</b> — that is the
/// whole point: a half-finished login must not be a login.
/// </summary>
[EnableRateLimiting(Sessions.AuthRateLimitPolicy)]
public class ChallengeModel(NextSkillDb db, TwoFactorLogin pending, ISmsSender sms) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Podaj kod.")]
    public string Code { get; set; } = "";

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    /// <summary>True when the visitor is redeeming a recovery code instead.</summary>
    [BindProperty(SupportsGet = true)] public bool Recovery { get; set; }

    public TwoFactorMethod Method { get; private set; }
    public int RecoveryCodesLeft { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadAsync() is { } denied) return denied;

        // An SMS factor needs a code sent before there is anything to type.
        if (Method == TwoFactorMethod.Sms && !Recovery) await SendSmsAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await LoadAsync() is { } denied) return denied;
        if (!ModelState.IsValid) return Page();

        var accountId = pending.PendingAccountId(HttpContext)!.Value;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.DeletedAt == null);
        if (account is null)
        {
            pending.End(HttpContext);
            return Redirect($"/{Locales.Current}/account/login");
        }

        var ok = Recovery
            ? await RecoveryCodes.RedeemAsync(db, accountId, Code)
            : Method switch
            {
                TwoFactorMethod.Totp => Totp.Verify(account.TwoFactorSecret ?? "", Code, DateTimeOffset.UtcNow),
                TwoFactorMethod.Sms => await OneTimeCodes.RedeemAsync(db, accountId, OneTimeCodePurpose.SmsSignIn, Code),
                _ => false,
            };

        if (!ok)
        {
            // ponytail: brute force is held off by the per-IP limiter (20 POSTs
            // per 5 minutes) plus TOTP's ~90-second validity window, which puts
            // a six-digit space far out of reach from one address. Ceiling: a
            // distributed attacker with many IPs. Upgrade path: a per-account
            // failure counter with a lockout, which needs a column and a policy
            // for who can clear it.
            ModelState.AddModelError(string.Empty, "Nieprawidłowy kod.");
            return Page();
        }

        // The pending state is consumed whether or not the code was reused, so
        // a correct code cannot be replayed against a second browser.
        pending.End(HttpContext);
        if (!await Sessions.SignInAsync(HttpContext, db, account))
            return Redirect($"/{Locales.Current}/account/login");

        return Redirect(Sessions.SafeReturnUrl(ReturnUrl, Locales.Current));
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        if (await LoadAsync() is { } denied) return denied;
        if (Method == TwoFactorMethod.Sms) await SendSmsAsync();

        return Page();
    }

    private async Task SendSmsAsync()
    {
        var accountId = pending.PendingAccountId(HttpContext)!.Value;
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && a.DeletedAt == null);
        if (account is null)
        {
            pending.End(HttpContext);
            return;
        }
        if (account.PhoneNumber is null) return;

        var code = await OneTimeCodes.IssueAsync(db, accountId, OneTimeCodePurpose.SmsSignIn);
        await sms.SendAsync(account.PhoneNumber, $"NEXTSKILL: Twój kod logowania to {code}. Wygasa za 5 minut.");
    }

    /// <summary>Loads the pending login, or the response to send instead of the page.</summary>
    private async Task<IActionResult?> LoadAsync()
    {
        if (pending.PendingAccountId(HttpContext) is not { } accountId)
            // No pending login: expired, tampered with, or somebody wandered
            // here directly. Back to the start, with nothing revealed.
            return Redirect($"/{Locales.Current}/account/login");

        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId && a.DeletedAt == null);
        if (account?.TwoFactorMethod is not { } method)
        {
            pending.End(HttpContext);
            return Redirect($"/{Locales.Current}/account/login");
        }

        Method = method;
        RecoveryCodesLeft = await db.RecoveryCodes.CountAsync(c => c.AccountId == accountId);
        return null;
    }
}
