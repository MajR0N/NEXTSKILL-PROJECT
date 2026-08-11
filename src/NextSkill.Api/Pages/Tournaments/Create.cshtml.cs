using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Tournaments;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// Creates an organiser-owned private draft, optionally from an independent
/// snapshot of one of that organiser's templates (decisions 015, 046).
/// </summary>
public class CreateModel(NextSkillDb db, TenantContext tenant) : PageModel
{
    public sealed record TemplateChoice(
        Guid Id,
        Guid OrganiserId,
        string Organiser,
        string Name,
        Game Game,
        TeamSize TeamSize,
        TournamentFormat Format,
        int MaxTeams);

    [BindProperty]
    public TournamentSetupForm Input { get; set; } = new();

    public IReadOnlyList<(Guid Id, string Name)> Organisers { get; private set; } = [];
    public IReadOnlyList<TemplateChoice> Templates { get; private set; } = [];
    public IReadOnlyList<SeasonChoice> Seasons { get; private set; } = [];
    public string? AppliedTemplateName { get; private set; }

    public static IReadOnlyList<TournamentFormat> BuildableFormats => TournamentSetup.BuildableFormats;

    public async Task<IActionResult> OnGetAsync(Guid? templateId)
    {
        if (await LoadOrganisersAsync() is { } denied) return denied;
        await LoadTemplatesAsync();
        await LoadSeasonsAsync();

        if (templateId is { } id)
        {
            var template = await db.TemplatesForManagement(Organisers.Select(o => o.Id).ToArray())
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (template is null) return NotFound();

            Input = TournamentSetup.From(template);
            Input.SeasonId = TournamentSetup.DefaultSeasonId(Seasons, DateTimeOffset.UtcNow);
            AppliedTemplateName = template.Name;
            return Page();
        }

        Input.OrganiserId = Organisers[0].Id;
        Input.SeasonId = TournamentSetup.DefaultSeasonId(Seasons, DateTimeOffset.UtcNow);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await LoadOrganisersAsync() is { } denied) return denied;
        await LoadTemplatesAsync();
        await LoadSeasonsAsync();

        // A form id is not authority. The policy is checked against the fresh
        // organiser roles before the tenant-scoped write is enabled (036).
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null ||
            !AccessPolicy.Can(actor, Operation.ManageTournament, new ResourceRef(Input.OrganiserId)))
            return Forbid();

        TournamentSetup.Validate(Input, ModelState);
        await TournamentSetup.ValidateSeasonAsync(db, Input, existing: null, state: ModelState);
        if (!ModelState.IsValid) return Page();

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            OrganiserId = Input.OrganiserId,
            Name = "",
            Status = TournamentStatus.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        TournamentSetup.ApplyDetails(Input, tournament);
        TournamentSetup.ApplyStructure(Input, tournament);

        tenant.Set(Input.OrganiserId);
        db.Tournaments.Add(tournament);
        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/t/{tournament.Id}");
    }

    public async Task<IActionResult> OnPostDeleteTemplateAsync(Guid templateId)
    {
        var template = await db.FindTemplateForManagementAsync(templateId);
        if (template is null) return NotFound();

        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageTournament, new ResourceRef(template.OrganiserId)))
            return Forbid();

        tenant.Set(template.OrganiserId);
        db.TournamentTemplates.Remove(template);
        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/tournaments/create");
    }

    private async Task<IActionResult?> LoadOrganisersAsync()
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();

        var allowed = (actor.Organisers ?? new Dictionary<Guid, OrganiserRole>())
            .Where(kv => AccessPolicy.Can(actor, Operation.ManageTournament, new ResourceRef(kv.Key)))
            .Select(kv => kv.Key)
            .ToList();

        var query = actor.Platform == PlatformRole.Owner
            ? db.Organisers
            : db.Organisers.Where(o => allowed.Contains(o.Id));

        Organisers = [.. await query.AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new ValueTuple<Guid, string>(o.Id, o.Name))
            .ToListAsync()];

        return Organisers.Count == 0 ? Forbid() : null;
    }

    private async Task LoadTemplatesAsync()
    {
        var organiserIds = Organisers.Select(o => o.Id).ToArray();

        Templates = await db.TemplatesForManagement(organiserIds)
            .Join(db.Organisers,
                template => template.OrganiserId,
                organiser => organiser.Id,
                (template, organiser) => new { Template = template, Organiser = organiser.Name })
            .AsNoTracking()
            .OrderBy(x => x.Organiser)
            .ThenBy(x => x.Template.Name)
            .Select(x => new TemplateChoice(
                x.Template.Id,
                x.Template.OrganiserId,
                x.Organiser,
                x.Template.Name,
                x.Template.Game,
                x.Template.TeamSize,
                x.Template.Format,
                x.Template.MaxTeams))
            .ToListAsync();
    }

    private async Task LoadSeasonsAsync() =>
        Seasons = await TournamentSetup.AssignableSeasonsAsync(db);
}
