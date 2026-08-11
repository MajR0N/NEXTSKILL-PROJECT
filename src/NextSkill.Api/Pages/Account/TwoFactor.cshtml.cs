using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
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
/// Turn two-factor authentication on or off (decision 047).
///
/// <b>Nothing is enabled until a code is verified.</b> Storing a secret and
/// trusting that the app scanned it correctly is how people lock themselves out
/// — the setup is only real once the phone has proved it agrees.
/// </summary>
[Authorize]
[EnableRateLimiting(Sessions.AuthRateLimitPolicy)]
public class TwoFactorModel(NextSkillDb db, ISmsSender sms) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Podaj kod z aplikacji.")]
    public string Code { get; set; } = "";

    [BindProperty]
    [Phone(ErrorMessage = "To nie wygląda na numer telefonu.")]
    public string? PhoneNumber { get; set; }

    public TwoFactorMethod? Enabled { get; private set; }
    public int RecoveryCodesLeft { get; private set; }

    /// <summary>Secret being set up right now, held in the form until confirmed.</summary>
    [BindProperty] public string? PendingSecret { get; set; }

    public string? ProvisioningUri { get; private set; }

    /// <summary>Shown exactly once, immediately after enabling (011, 029).</summary>
    public IReadOnlyList<string> FreshRecoveryCodes { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Starts TOTP setup: a secret to scan, not yet trusted.</summary>
    public async Task<IActionResult> OnPostStartTotpAsync()
    {
        await LoadAsync();
        if (Enabled is not null) return Page();

        PendingSecret = Totp.NewSecret();
        ProvisioningUri = Totp.ProvisioningUri("NEXTSKILL", User.Identity?.Name ?? "", PendingSecret);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmTotpAsync()
    {
        await LoadAsync();
        if (Enabled is not null) return Page();

        if (string.IsNullOrWhiteSpace(PendingSecret) ||
            !Totp.Verify(PendingSecret, Code, DateTimeOffset.UtcNow))
        {
            ModelState.AddModelError(string.Empty, "Ten kod się nie zgadza. Sprawdź, czy zegar w telefonie jest ustawiony automatycznie.");
            ProvisioningUri = string.IsNullOrWhiteSpace(PendingSecret)
                ? null
                : Totp.ProvisioningUri("NEXTSKILL", User.Identity?.Name ?? "", PendingSecret);
            return Page();
        }

        return await EnableAsync(TwoFactorMethod.Totp, secret: PendingSecret);
    }

    /// <summary>Sends a code to the number being added, proving it is reachable.</summary>
    public async Task<IActionResult> OnPostStartSmsAsync()
    {
        await LoadAsync();
        if (Enabled is not null) return Page();

        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            ModelState.AddModelError(nameof(PhoneNumber), "Podaj numer telefonu.");
            return Page();
        }

        var accountId = Sessions.AccountIdOf(User)!.Value;
        var code = await OneTimeCodes.IssueAsync(db, accountId, OneTimeCodePurpose.PhoneVerification);
        await sms.SendAsync(PhoneNumber, $"NEXTSKILL: kod potwierdzenia numeru to {code}.");

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmSmsAsync()
    {
        await LoadAsync();
        if (Enabled is not null) return Page();

        var accountId = Sessions.AccountIdOf(User)!.Value;

        if (string.IsNullOrWhiteSpace(PhoneNumber) ||
            !await OneTimeCodes.RedeemAsync(db, accountId, OneTimeCodePurpose.PhoneVerification, Code))
        {
            ModelState.AddModelError(string.Empty, "Nieprawidłowy kod.");
            return Page();
        }

        return await EnableAsync(TwoFactorMethod.Sms, phone: PhoneNumber);
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var accountId = Sessions.AccountIdOf(User)!.Value;
        var account = await db.Accounts.FirstAsync(a => a.Id == accountId);

        account.TwoFactorMethod = null;
        account.TwoFactorSecret = null;
        account.TwoFactorEnabledAt = null;
        account.PhoneVerifiedAt = null;

        // Codes for a factor that no longer exists are just credentials lying
        // around.
        await db.RecoveryCodes.Where(c => c.AccountId == accountId).ExecuteDeleteAsync();
        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/account/two-factor");
    }

    private async Task<IActionResult> EnableAsync(TwoFactorMethod method, string? secret = null, string? phone = null)
    {
        var accountId = Sessions.AccountIdOf(User)!.Value;
        var account = await db.Accounts.FirstAsync(a => a.Id == accountId);

        account.TwoFactorMethod = method;
        account.TwoFactorSecret = secret;
        account.TwoFactorEnabledAt = DateTimeOffset.UtcNow;

        if (phone is not null)
        {
            account.PhoneNumber = phone;
            account.PhoneVerifiedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();

        // Issued with the factor, never later. Turning on two-factor without
        // handing over a way back in is how an account is lost to a dropped
        // phone (047).
        FreshRecoveryCodes = await RecoveryCodes.IssueAsync(db, accountId);

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var accountId = Sessions.AccountIdOf(User)!.Value;

        var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId);
        Enabled = account.TwoFactorMethod;
        PhoneNumber ??= account.PhoneNumber;
        RecoveryCodesLeft = await db.RecoveryCodes.CountAsync(c => c.AccountId == accountId);
    }
}
