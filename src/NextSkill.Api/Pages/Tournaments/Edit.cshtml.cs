using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Discord;
using NextSkill.Api.Features.Tournaments;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// Tournament setup editing and template capture (decisions 015, 046).
/// Competitive structure freezes when registration is published; all editing
/// freezes when a bracket may exist. Both rules are enforced on POST.
/// </summary>
public class EditModel(
    NextSkillDb db,
    TenantContext tenant,
    DiscordWebhookProtector discordProtector) : PageModel
{
    [BindProperty]
    public TournamentSetupForm Input { get; set; } = new();

    [BindProperty]
    public string? TemplateName { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Updated { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool TemplateSaved { get; set; }

    [BindProperty]
    public string? DiscordWebhookUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool DiscordUpdated { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool DiscordRemoved { get; set; }

    public Tournament? Tournament { get; private set; }
    public int ActiveEntries { get; private set; }
    public bool MayEditDetails { get; private set; }
    public bool MayEditStructure { get; private set; }
    public bool SeasonAssignmentLocked { get; private set; }
    public string? AssignedSeasonName { get; private set; }
    public IReadOnlyList<SeasonChoice> Seasons { get; private set; } = [];
    public bool DiscordConfigured { get; private set; }
    public DateTimeOffset? DiscordLastSucceededAt { get; private set; }
    public DateTimeOffset? DiscordLastFailedAt { get; private set; }
    public string? DiscordLastError { get; private set; }
    public int DiscordAbandonedAnnouncements { get; private set; }

    public static IReadOnlyList<TournamentFormat> BuildableFormats => TournamentSetup.BuildableFormats;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var tournament = await db.FindForManagementAsync(id);
        if (tournament is null) return NotFound();
        if (await AuthorizeAsync(tournament) is { } denied) return denied;

        await PreparePageAsync(tournament, copyInput: true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The status handler takes the same lock. An edit therefore cannot
        // validate Draft rules and land after another request opens registration.
        var tournament = await db.Tournaments
            .FromSqlInterpolated($"SELECT * FROM tournaments WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);

        if (tournament is null) return NotFound();
        if (await AuthorizeAsync(tournament, cancellationToken) is { } denied) return denied;

        await PreparePageAsync(tournament, copyInput: false, cancellationToken);

        if (!MayEditDetails)
        {
            ModelState.Clear();
            ModelState.AddModelError(string.Empty,
                "Ustawienia trwającego, zakończonego albo odwołanego turnieju są historią i nie można ich zmieniać.");
            Input = TournamentSetup.From(tournament);
            return Page();
        }

        TournamentSetup.Validate(Input, ModelState);
        await TournamentSetup.ValidateSeasonAsync(
            db, Input, tournament, ModelState, cancellationToken);

        if (Input.MaxTeams < ActiveEntries)
            ModelState.AddModelError("Input.MaxTeams",
                $"Limit nie może być mniejszy niż liczba zapisanych drużyn ({ActiveEntries}).");

        if (!MayEditStructure)
        {
            if (Input.Game != tournament.Game)
                ModelState.AddModelError("Input.Game", "Gry nie można zmienić po otwarciu zapisów.");
            if (Input.TeamSize != tournament.TeamSize)
                ModelState.AddModelError("Input.TeamSize", "Rozmiaru składu nie można zmienić po otwarciu zapisów.");
            if (Input.Format != tournament.Format)
                ModelState.AddModelError("Input.Format", "Formatu nie można zmienić po otwarciu zapisów.");
        }

        if (!ModelState.IsValid) return Page();

        TournamentSetup.ApplyDetails(Input, tournament);
        if (MayEditStructure)
            TournamentSetup.ApplyStructure(Input, tournament);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Redirect($"/{Locales.Current}/t/{id}/edit?updated=true");
    }

    public async Task<IActionResult> OnPostSaveTemplateAsync(Guid id, CancellationToken cancellationToken)
    {
        // This form intentionally posts no tournament setup fields.
        ModelState.Clear();

        var tournament = await db.FindForManagementAsync(id);
        if (tournament is null) return NotFound();
        if (await AuthorizeAsync(tournament, cancellationToken) is { } denied) return denied;

        var name = (TemplateName ?? "").Trim();
        if (name.Length is < 3 or > 80)
        {
            ModelState.AddModelError(nameof(TemplateName), "Nazwa szablonu musi mieć od 3 do 80 znaków.");
            await PreparePageAsync(tournament, copyInput: true, cancellationToken);
            return Page();
        }

        var nameKey = name.ToLowerInvariant();
        if (await db.TournamentTemplates.AnyAsync(t => t.NameKey == nameKey, cancellationToken))
        {
            ModelState.AddModelError(nameof(TemplateName), "Szablon o tej nazwie już istnieje.");
            await PreparePageAsync(tournament, copyInput: true, cancellationToken);
            return Page();
        }

        var now = DateTimeOffset.UtcNow;
        var template = new TournamentTemplate
        {
            Id = Guid.NewGuid(),
            OrganiserId = tournament.OrganiserId,
            Name = name,
            NameKey = nameKey,
            Game = tournament.Game,
            TeamSize = tournament.TeamSize,
            Format = tournament.Format,
            MaxTeams = tournament.MaxTeams,
            Description = tournament.Description,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.TournamentTemplates.Add(template);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique index closes simultaneous saves of the same name.
            db.Entry(template).State = EntityState.Detached;
            ModelState.AddModelError(nameof(TemplateName), "Szablon o tej nazwie już istnieje.");
            await PreparePageAsync(tournament, copyInput: true, cancellationToken);
            return Page();
        }

        return Redirect($"/{Locales.Current}/t/{id}/edit?templateSaved=true");
    }

    public async Task<IActionResult> OnPostSaveDiscordAsync(Guid id, CancellationToken cancellationToken)
    {
        // Treat the submitted URL as a password. Clearing model state before
        // any validation response prevents Razor from echoing the credential.
        var submittedUrl = DiscordWebhookUrl;
        ModelState.Clear();
        DiscordWebhookUrl = null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tournament = await db.Tournaments
            .FromSqlInterpolated($"SELECT * FROM tournaments WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (tournament is null) return NotFound();
        if (await AuthorizeAsync(tournament, cancellationToken) is { } denied) return denied;

        if (!DiscordWebhookUrls.TryParse(submittedUrl, out var uri, out var error))
        {
            ModelState.AddModelError(nameof(DiscordWebhookUrl), error);
            await PreparePageAsync(tournament, copyInput: true, cancellationToken);
            return Page();
        }

        var webhook = await db.TournamentDiscordWebhooks
            .SingleOrDefaultAsync(w => w.TournamentId == tournament.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (webhook is null)
        {
            webhook = new TournamentDiscordWebhook
            {
                Id = Guid.NewGuid(),
                OrganiserId = tournament.OrganiserId,
                TournamentId = tournament.Id,
                ConfiguredAt = now,
            };
            db.TournamentDiscordWebhooks.Add(webhook);
        }

        webhook.ProtectedUrl = discordProtector.Protect(uri!);
        webhook.ConfiguredAt = now;
        webhook.DisabledAt = null;
        webhook.LastSucceededAt = null;
        webhook.LastFailedAt = null;
        webhook.LastError = null;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/t/{id}/edit?discordUpdated=true");
    }

    public async Task<IActionResult> OnPostRemoveDiscordAsync(Guid id, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        DiscordWebhookUrl = null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tournament = await db.Tournaments
            .FromSqlInterpolated($"SELECT * FROM tournaments WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (tournament is null) return NotFound();
        if (await AuthorizeAsync(tournament, cancellationToken) is { } denied) return denied;

        var now = DateTimeOffset.UtcNow;
        var webhook = await db.TournamentDiscordWebhooks
            .SingleOrDefaultAsync(w => w.TournamentId == tournament.Id, cancellationToken);
        if (webhook is not null)
        {
            webhook.ProtectedUrl = null;
            webhook.DisabledAt = now;
            webhook.LastFailedAt = null;
            webhook.LastError = null;
        }

        await db.DiscordAnnouncements
            .Where(a => a.TournamentId == tournament.Id &&
                        a.SentAt == null && a.AbandonedAt == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(a => a.AbandonedAt, now)
                .SetProperty(a => a.LastError, "configuration_removed"),
                cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/t/{id}/edit?discordRemoved=true");
    }

    private async Task<IActionResult?> AuthorizeAsync(
        Tournament tournament,
        CancellationToken cancellationToken = default)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageTournament, new ResourceRef(tournament.OrganiserId)))
            return Forbid();

        tenant.Set(tournament.OrganiserId);
        return null;
    }

    private async Task PreparePageAsync(
        Tournament tournament,
        bool copyInput,
        CancellationToken cancellationToken = default)
    {
        Tournament = tournament;
        MayEditDetails = TournamentLifecycle.MayEditDetails(tournament.Status);
        MayEditStructure = TournamentLifecycle.MayEditStructure(tournament.Status);
        ActiveEntries = await db.Registrations
            .CountAsync(r => r.TournamentId == tournament.Id && r.WithdrawnAt == null, cancellationToken);

        Seasons = await TournamentSetup.AssignableSeasonsAsync(db, cancellationToken);
        if (tournament.SeasonId is { } seasonId)
        {
            var assigned = await db.Seasons.AsNoTracking()
                .Where(s => s.Id == seasonId)
                .Select(s => new { s.Name, s.Closed })
                .FirstOrDefaultAsync(cancellationToken);
            AssignedSeasonName = assigned?.Name;
            SeasonAssignmentLocked = assigned?.Closed == true;
        }

        var discord = await db.TournamentDiscordWebhooks.AsNoTracking()
            .SingleOrDefaultAsync(w => w.TournamentId == tournament.Id, cancellationToken);
        DiscordConfigured = discord?.ProtectedUrl is not null && discord.DisabledAt is null;
        DiscordLastSucceededAt = discord?.LastSucceededAt;
        DiscordLastFailedAt = discord?.LastFailedAt;
        DiscordLastError = discord?.LastError;
        DiscordAbandonedAnnouncements = await db.DiscordAnnouncements.AsNoTracking()
            .CountAsync(a => a.TournamentId == tournament.Id && a.AbandonedAt != null, cancellationToken);

        if (copyInput)
            Input = TournamentSetup.From(tournament);
    }
}
