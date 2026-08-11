using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Teams;

/// <summary>
/// Edit or disband a team (decision 045). Captain only, through
/// <see cref="Operation.ManageTeam"/>.
///
/// <b>Renaming is allowed even after matches have been played.</b> Brackets and
/// results reference the team by id, not by name, so a rebrand cannot corrupt
/// history — and community teams rebrand constantly. Refusing would be a rule
/// that protects nothing at the cost of something people actually do.
/// </summary>
public class EditModel(NextSkillDb db) : PageModel
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

    /// <summary>Typed to confirm a disband — see the handler for why.</summary>
    [BindProperty] public string? ConfirmTag { get; set; }

    public Team? Team { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (await GuardAsync(id) is { } denied) return denied;

        Input = new Form { Name = Team!.Name, Tag = Team.Tag, City = Team.City };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (await GuardAsync(id) is { } denied) return denied;
        if (!ModelState.IsValid) return Page();

        var tag = Input.Tag.Trim();

        if (await db.Teams.AnyAsync(t => t.Id != id && t.Tag.ToLower() == tag.ToLower()))
        {
            // Includes disbanded teams on purpose. A tag is how a team is
            // recognised in a bracket; letting a new team inherit a dead one's
            // tag would make old results read as if they belonged to somebody
            // else (045).
            ModelState.AddModelError("Input.Tag", "Ten tag jest już zajęty.");
            return Page();
        }

        Team!.Name = Input.Name.Trim();
        Team.Tag = tag;
        Team.City = string.IsNullOrWhiteSpace(Input.City) ? null : Input.City.Trim();

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Ten tag został właśnie zajęty. Wybierz inny.");
            return Page();
        }

        return Redirect($"/{Locales.Current}/teams/{id}");
    }

    public async Task<IActionResult> OnPostDisbandAsync(Guid id)
    {
        if (await GuardAsync(id) is { } denied) return denied;

        // Typing the tag is the confirmation. A yes/no dialog is clicked
        // reflexively; retyping the thing being destroyed is the one pattern
        // that reliably interrupts autopilot (020).
        if (!string.Equals(ConfirmTag?.Trim(), Team!.Tag, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ConfirmTag), $"Wpisz dokładnie „{Team.Tag}”, żeby potwierdzić.");
            Input = new Form { Name = Team.Name, Tag = Team.Tag, City = Team.City };
            return Page();
        }

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Soft, always. Matches and ratings point at this id; deleting the row
        // would leave finished tournaments referencing nothing (045).
        var disbandedAt = DateTimeOffset.UtcNow;
        Team.DisbandedAt = disbandedAt;

        // Everyone leaves at the same moment, so nobody keeps captain rights
        // over a team that no longer accepts anything — and so the roster stops
        // claiming players who are free to join elsewhere.
        await db.TeamMemberships
            .Where(m => m.TeamId == id && m.LeftAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeftAt, disbandedAt));

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Redirect($"/{Locales.Current}/teams");
    }

    /// <summary>
    /// Loads the team and answers the authorization question once, so no handler
    /// can be added later that forgets to ask it.
    /// </summary>
    private async Task<IActionResult?> GuardAsync(Guid id)
    {
        Team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id);
        if (Team is null || Team.DisbandedAt is not null) return NotFound();

        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();

        return AccessPolicy.Can(actor, Operation.ManageTeam, new ResourceRef(TeamId: id))
            ? null
            : Forbid();
    }
}
