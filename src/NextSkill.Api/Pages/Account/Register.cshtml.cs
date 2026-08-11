using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;

// This page's namespace is `...Pages.Account`, which shadows the `Account`
// ENTITY. The alias is the fix; renaming either would be worse.
using AccountEntity = NextSkill.Api.Shared.Persistence.Account;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// Registration — the narrow archetype (041): one column, one job, no
/// navigation competing with it.
///
/// A native account is the primary identity (011). Steam and Discord are
/// optional links added later, never login requirements, because a large share
/// of the CS 1.6 audience does not own the game on Steam and must not be
/// excluded by the sign-up form.
/// </summary>
[EnableRateLimiting(Sessions.AuthRateLimitPolicy)]
public class RegisterModel(NextSkillDb db) : PageModel
{
    public sealed class Form
    {
        [Required(ErrorMessage = "Podaj adres e-mail.")]
        [EmailAddress(ErrorMessage = "To nie wygląda na adres e-mail.")]
        [StringLength(254)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Wybierz nick.")]
        [StringLength(32, MinimumLength = 3, ErrorMessage = "Nick musi mieć od 3 do 32 znaków.")]
        [RegularExpression(@"^[\p{L}\p{N}_\-\.]+$",
            ErrorMessage = "Nick może zawierać tylko litery, cyfry, kropkę, myślnik i podkreślnik.")]
        public string Nickname { get; set; } = "";

        // Length beats composition rules: "Password1!" satisfies every symbol
        // requirement ever written and is trivially guessed. A floor of 10 and
        // no character classes is current OWASP guidance, and it is also the
        // rule people actually follow instead of working around (011, 020).
        [Required(ErrorMessage = "Ustaw hasło.")]
        [StringLength(200, MinimumLength = 10, ErrorMessage = "Hasło musi mieć co najmniej 10 znaków.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    [BindProperty] public Form Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        // Emails are stored lowercase so the plain unique index is
        // case-insensitive in effect, and so "A@x.pl" cannot become a second
        // account alongside "a@x.pl".
        var email = Input.Email.Trim().ToLowerInvariant();
        var nickname = Input.Nickname.Trim();

        // ponytail: registration tells the user their email is already taken.
        // Ceiling: that is an account-enumeration oracle — someone can test
        // whether an address has an account here. Upgrade path: accept the
        // submission silently and send "you already have an account" by email,
        // which needs the email pipeline 043 has not built yet. Choosing a
        // usable form now over a security posture we cannot yet complete.
        if (await db.Accounts.AnyAsync(a => a.DeletedAt == null && a.Email == email))
            ModelState.AddModelError("Input.Email", "Ten adres jest już zarejestrowany.");

        if (await db.Accounts.AnyAsync(a => a.DeletedAt == null && a.Nickname.ToLower() == nickname.ToLower()))
            ModelState.AddModelError("Input.Nickname", "Ten nick jest już zajęty.");

        if (!ModelState.IsValid) return Page();

        var account = new AccountEntity
        {
            Id = Guid.NewGuid(),
            Email = email,
            Nickname = nickname,
            PasswordHash = Passwords.Hash(Input.Password),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The checks above are a courtesy; the unique indexes are the truth.
            // Two people submitting the same nickname at the same moment both
            // pass the check and one loses here — which is correct, and why the
            // database owns the rule rather than this method (008).
            ModelState.AddModelError(string.Empty, "Ten e-mail lub nick został właśnie zajęty. Spróbuj innego.");
            return Page();
        }

        if (!await Sessions.SignInAsync(HttpContext, db, account))
            return Redirect($"/{Locales.Current}/account/login");

        return Redirect($"/{Locales.Current}");
    }
}
