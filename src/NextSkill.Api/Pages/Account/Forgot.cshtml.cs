using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Email;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// Request a password-reset link (decision 011).
///
/// <b>This page answers identically whether or not the address exists.</b> A
/// "no such account" message turns the form into a way to test which addresses
/// are registered here — and knowing that somebody plays on NEXTSKILL is a
/// small leak that combines badly with everything else an attacker knows
/// ([040](040-threat-model.md)).
/// </summary>
[EnableRateLimiting(Sessions.AuthRateLimitPolicy)]
public class ForgotModel(NextSkillDb db, IEmailSender email) : PageModel
{
    public sealed class Form
    {
        [Required(ErrorMessage = "Podaj adres e-mail.")]
        [EmailAddress(ErrorMessage = "To nie wygląda na adres e-mail.")]
        public string Email { get; set; } = "";
    }

    [BindProperty] public Form Input { get; set; } = new();

    /// <summary>Shown after any submission, successful or not.</summary>
    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var address = Input.Email.Trim().ToLowerInvariant();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.DeletedAt == null && a.Email == address);

        if (account is not null)
        {
            // Mailbomb guard. The per-IP limiter does not cover this: the
            // attacker's cost is one request and the victim's cost is one email,
            // so the throttle has to be per ACCOUNT (040).
            var recent = await db.PasswordResetTokens.AnyAsync(t =>
                t.AccountId == account.Id &&
                t.CreatedAt > DateTimeOffset.UtcNow - ResetTokens.ResendAfter);

            if (!recent)
            {
                // Any older link stops working the moment a new one is issued.
                // Two live reset links for one account doubles the window
                // without doubling anything useful.
                await db.PasswordResetTokens.Where(t => t.AccountId == account.Id).ExecuteDeleteAsync();

                var (token, hash) = ResetTokens.Create();

                db.PasswordResetTokens.Add(new PasswordResetToken
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    TokenHash = hash,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow + ResetTokens.Lifetime,
                });
                await db.SaveChangesAsync();

                var link = $"{Request.Scheme}://{Request.Host}/{Locales.Current}/account/reset?token={Uri.EscapeDataString(token)}";
                var minutes = (int)ResetTokens.Lifetime.TotalMinutes;

                await email.SendAsync(account.Email, "NEXTSKILL — reset hasła",
                    $"""
                     Cześć {account.Nickname},

                     ktoś (mam nadzieję, że Ty) poprosił o zmianę hasła do konta NEXTSKILL.

                     {link}

                     Link działa {minutes} minut i tylko raz. Jeśli wygaśnie, poproś o nowy —
                     to jedno kliknięcie.

                     Jeśli to nie Ty, zignoruj tę wiadomość. Hasło pozostaje bez zmian
                     i nikt nie dostał dostępu do konta.
                     """);
            }
        }

        // Outside the `if` on purpose: the answer must not depend on what we
        // found. Nothing above branches into expensive work either, so the
        // response time does not give it away.
        Sent = true;
        return Page();
    }
}
