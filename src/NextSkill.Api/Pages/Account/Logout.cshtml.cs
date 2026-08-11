using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// Logout — <b>POST only</b>, and therefore antiforgery-protected by Razor Pages
/// automatically.
///
/// A GET logout is a real bug, not pedantry: any page on the internet could log
/// a NEXTSKILL user out with an <c>&lt;img src&gt;</c>, and a prefetching browser
/// or link scanner could do it by accident.
/// </summary>
public class LogoutModel(NextSkillDb db) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await Sessions.SignOutAsync(HttpContext, db);

        return Redirect($"/{Locales.Current}");
    }

    /// <summary>A GET here means a stale link or a prefetch — bounce, never act.</summary>
    public IActionResult OnGet() => Redirect($"/{Locales.Default}");
}
