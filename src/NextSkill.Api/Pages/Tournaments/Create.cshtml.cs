using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// Create a tournament (decisions 015, 046).
///
/// Organiser staff only. A tournament belongs to exactly one organiser and that
/// is its tenant boundary ([037]) — so the very first thing this page does is
/// work out which organisers the visitor may act for, and refuse if the answer
/// is none.
/// </summary>
public class CreateModel(NextSkillDb db, TenantContext tenant) : PageModel
{
    public sealed class Form
    {
        [Required(ErrorMessage = "Podaj nazwę turnieju.")]
        [StringLength(120, MinimumLength = 3, ErrorMessage = "Nazwa musi mieć od 3 do 120 znaków.")]
        public string Name { get; set; } = "";

        public Guid OrganiserId { get; set; }
        public Game Game { get; set; } = Game.Cs2;
        public TeamSize TeamSize { get; set; } = TeamSize.Squad;
        public TournamentFormat Format { get; set; } = TournamentFormat.SingleElimination;

        [Range(2, 128, ErrorMessage = "Liczba drużyn musi być między 2 a 128.")]
        public int MaxTeams { get; set; } = 16;

        [StringLength(4000)]
        public string? Description { get; set; }

        public DateTimeOffset? StartsAt { get; set; }
    }

    [BindProperty] public Form Input { get; set; } = new();

    /// <summary>Organisers this visitor may create for. Empty means no page.</summary>
    public IReadOnlyList<(Guid Id, string Name)> Organisers { get; private set; } = [];

    /// <summary>Only the two formats that actually generate today (022).</summary>
    public static readonly TournamentFormat[] BuildableFormats =
        [TournamentFormat.SingleElimination, TournamentFormat.DoubleElimination];

    public async Task<IActionResult> OnGetAsync()
    {
        if (await LoadOrganisersAsync() is { } denied) return denied;

        Input.OrganiserId = Organisers[0].Id;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await LoadOrganisersAsync() is { } denied) return denied;

        // The submitted organiser must be one of theirs. Without this, the id is
        // just a number in a form and anyone could create a tournament inside
        // somebody else's organisation (036, 040).
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null ||
            !AccessPolicy.Can(actor, Operation.ManageTournament, new ResourceRef(Input.OrganiserId)))
            return Forbid();

        if (!BuildableFormats.Contains(Input.Format))
            ModelState.AddModelError("Input.Format", "Ten format nie jest jeszcze dostępny.");

        if (!ModelState.IsValid) return Page();

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            OrganiserId = Input.OrganiserId,
            Name = Input.Name.Trim(),
            Game = Input.Game,
            Format = Input.Format,
            TeamSize = Input.TeamSize,
            MaxTeams = Input.MaxTeams,
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            StartsAt = Input.StartsAt,

            // Draft, never open. An organiser needs to read their own page back
            // before the world does, and 046 keeps drafts private for exactly
            // that reason.
            Status = TournamentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // The tenant is established from the organiser we just authorised
        // against, so the tenant-scoped write is scoped by the same fact the
        // policy checked — not by anything the form said.
        tenant.Set(Input.OrganiserId);
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/t/{tournament.Id}");
    }

    /// <summary>
    /// Loads the organisers this visitor may create tournaments for, or returns
    /// the response to send instead.
    /// </summary>
    private async Task<IActionResult?> LoadOrganisersAsync()
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();

        var allowed = (actor.Organisers ?? new Dictionary<Guid, OrganiserRole>())
            .Where(kv => AccessPolicy.Can(actor, Operation.ManageTournament, new ResourceRef(kv.Key)))
            .Select(kv => kv.Key)
            .ToList();

        // The platform owner may act inside any organiser (036), so they get the
        // full list rather than an empty one.
        var query = actor.Platform == PlatformRole.Owner
            ? db.Organisers
            : db.Organisers.Where(o => allowed.Contains(o.Id));

        // AsNoTracking before the projection: a value tuple is not an entity,
        // and EF has nothing to track once the query has produced one.
        Organisers = [.. await query.AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new ValueTuple<Guid, string>(o.Id, o.Name))
            .ToListAsync()];

        // Not 404: they are logged in and this page exists, they simply may not
        // use it. 403 is the honest answer and it is what 036 asks for when the
        // resource is not secret.
        return Organisers.Count == 0 ? Forbid() : null;
    }
}
