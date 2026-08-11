using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Teams;

/// <summary>
/// Create a team (decisions 021, 036).
///
/// The creator becomes <b>captain in the same transaction</b>. A team with no
/// captain is a team nobody can invite to, confirm results for, or delete — an
/// orphan that needs staff intervention to fix. Doing it in two steps means a
/// crash between them produces exactly that.
/// </summary>
[Authorize]
public class CreateModel(NextSkillDb db) : PageModel
{
    public sealed class Form
    {
        [Required(ErrorMessage = "Podaj nazwę drużyny.")]
        [StringLength(60, MinimumLength = 2, ErrorMessage = "Nazwa musi mieć od 2 do 60 znaków.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Podaj tag drużyny.")]
        [StringLength(8, MinimumLength = 2, ErrorMessage = "Tag musi mieć od 2 do 8 znaków.")]
        [RegularExpression(@"^[\p{L}\p{N}\.\-]+$",
            ErrorMessage = "Tag może zawierać tylko litery, cyfry, kropkę i myślnik.")]
        public string Tag { get; set; } = "";

        [StringLength(60)]
        public string? City { get; set; }
    }

    [BindProperty] public Form Input { get; set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        if (Sessions.AccountIdOf(User) is not { } accountId) return Forbid();

        var tag = Input.Tag.Trim();

        if (await db.Teams.AnyAsync(t => t.Tag.ToLower() == tag.ToLower()))
            ModelState.AddModelError("Input.Tag", "Ten tag jest już zajęty.");

        if (!ModelState.IsValid) return Page();

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = Input.Name.Trim(),
            Tag = tag,
            City = string.IsNullOrWhiteSpace(Input.City) ? null : Input.City.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Teams.Add(team);
        db.TeamMemberships.Add(new TeamMembership
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            AccountId = accountId,
            Role = TeamRole.Captain,
            // JoinedAt set immediately: the founder does not invite themselves.
            JoinedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            // One SaveChanges, therefore one transaction: the team and its
            // captain exist together or not at all.
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Ten tag został właśnie zajęty. Wybierz inny.");
            return Page();
        }

        return Redirect($"/{Locales.Current}/teams/{team.Id}");
    }
}
