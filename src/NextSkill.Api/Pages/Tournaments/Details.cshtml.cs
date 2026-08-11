using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Tournaments;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// The tournament page (decisions 043, 046).
///
/// <b>State-driven, because a tournament page has three lives.</b> Before,
/// during and after are genuinely different pages wearing one URL: registration
/// wants "what is this and how do I join", running wants "who plays next",
/// finished wants "who won". Same URL throughout, because that link is the one
/// that gets pasted into Discord and it has to keep working (018).
/// </summary>
public class DetailsModel(NextSkillDb db, TenantContext tenant) : PageModel
{
    public sealed record Entry(Guid TeamId, string Name, string Tag, int? Seed, int Fieldable, bool Ready);

    public Tournament? Tournament { get; private set; }
    public string OrganiserName { get; private set; } = "";
    public IReadOnlyList<Entry> Entries { get; private set; } = [];

    /// <summary>Teams the visitor captains, and whether each may enter.</summary>
    public IReadOnlyList<(Guid Id, string Name, int Fieldable, bool Entered)> MyTeams { get; private set; } = [];

    public bool CanManage { get; private set; }

    public int PlayersRequired => (int)(Tournament?.TeamSize ?? TeamSize.Squad);
    public int PlacesLeft => (Tournament?.MaxTeams ?? 0) - Entries.Count;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        await LoadAsync(id);
        return Tournament is null ? NotFound() : Page();
    }

    // ── entering and leaving ─────────────────────────────────────────────

    public async Task<IActionResult> OnPostEnterAsync(Guid id, Guid teamId)
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();

        // Entering a tournament is a roster-level act: the captain commits their
        // team's time. It is NOT the organiser's operation (036).
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: teamId)))
            return Forbid();

        var tournament = await db.FindPublicTournamentAsync(id);
        if (tournament is null) return NotFound();

        if (!TournamentLifecycle.AcceptsRegistrations(tournament.Status))
        {
            ModelState.AddModelError(string.Empty, "Zapisy są zamknięte.");
            await LoadAsync(id);
            return Page();
        }

        tenant.Set(tournament.OrganiserId);

        var taken = await db.Registrations.CountAsync(r => r.TournamentId == id && r.WithdrawnAt == null);
        if (taken >= tournament.MaxTeams)
        {
            ModelState.AddModelError(string.Empty, "Brak wolnych miejsc.");
            await LoadAsync(id);
            return Page();
        }

        // The rule that connects 045 to tournaments. A team of three entering a
        // 5v5 is a forfeit that has not happened yet — and a forfeit discovered
        // at match time costs the opponent their evening, not just a result.
        var fieldable = await FieldableCountAsync(teamId);
        if (fieldable < (int)tournament.TeamSize)
        {
            ModelState.AddModelError(string.Empty,
                $"Ten turniej to {(int)tournament.TeamSize}v{(int)tournament.TeamSize}, " +
                $"a drużyna ma {fieldable} zawodników mogących grać. Uzupełnij skład.");
            await LoadAsync(id);
            return Page();
        }

        if (await db.Teams.AnyAsync(t => t.Id == teamId && t.DisbandedAt != null))
            return NotFound();

        db.Registrations.Add(new TournamentRegistration
        {
            Id = Guid.NewGuid(),
            OrganiserId = tournament.OrganiserId,
            TournamentId = id,
            TeamId = teamId,
            RegisteredAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The partial unique index is the real guard against a double
            // submit, exactly as it is for roster invites (008).
            ModelState.AddModelError(string.Empty, "Ta drużyna jest już zapisana.");
            await LoadAsync(id);
            return Page();
        }

        return Redirect($"/{Locales.Current}/t/{id}");
    }

    public async Task<IActionResult> OnPostWithdrawAsync(Guid id, Guid teamId)
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: teamId)))
            return Forbid();

        var tournament = await db.FindPublicTournamentAsync(id);
        if (tournament is null) return NotFound();

        // Once a bracket exists, a team vanishing would leave matches pointing
        // at a team that is not in the event (023). Withdrawing from a running
        // tournament is a forfeit, which is a different operation with a
        // different result — not this one.
        if (TournamentLifecycle.ParticipantsAreFrozen(tournament.Status))
        {
            ModelState.AddModelError(string.Empty,
                "Turniej już trwa. Wycofanie się z trwającego turnieju to walkower — zgłoś to organizatorowi.");
            await LoadAsync(id);
            return Page();
        }

        tenant.Set(tournament.OrganiserId);

        var entry = await db.Registrations.FirstOrDefaultAsync(r =>
            r.TournamentId == id && r.TeamId == teamId && r.WithdrawnAt == null);

        if (entry is null) return NotFound();

        entry.WithdrawnAt = DateTimeOffset.UtcNow;

        // A withdrawal invalidates the seeding: seeds are positions in a field,
        // and the field just changed. Clearing them is honest; keeping stale
        // ones would show an organiser a bracket shape that no longer holds.
        entry.Seed = null;
        await db.Registrations.Where(r => r.TournamentId == id && r.WithdrawnAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Seed, (int?)null));

        await db.SaveChangesAsync();
        return Redirect($"/{Locales.Current}/t/{id}");
    }

    // ── organiser controls ───────────────────────────────────────────────

    public async Task<IActionResult> OnPostStatusAsync(Guid id, TournamentStatus to)
    {
        var tournament = await db.FindForManagementAsync(id);
        if (tournament is null) return NotFound();

        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.RunTournament, new ResourceRef(tournament.OrganiserId)))
            return Forbid();

        // The state machine decides, not the buttons. A URL typed by hand gets
        // the same answer as a click (046).
        if (!TournamentLifecycle.CanTransition(tournament.Status, to))
        {
            ModelState.AddModelError(string.Empty,
                $"Nie można przejść z „{tournament.Status}” do „{to}”.");
            await LoadAsync(id);
            return Page();
        }

        tenant.Set(tournament.OrganiserId);

        // Closing registration is the moment the field is known, so it is the
        // moment seeds can exist at all (046).
        if (to == TournamentStatus.RegistrationClosed)
            await AssignSeedsAsync(id);

        // Starting the tournament is where the bracket comes into existence.
        // Doing it here, in the same transaction as the status change, means a
        // tournament can never be Running without one (023, 046).
        if (to == TournamentStatus.Running)
            await BracketBuilder.BuildAsync(db, tournament);

        if (to == TournamentStatus.RegistrationOpen)
            // Reopening invalidates seeds: the field is about to change again.
            await db.Registrations.Where(r => r.TournamentId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Seed, (int?)null));

        tournament.Status = to;
        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/t/{id}");
    }

    /// <summary>
    /// Orders the field by rating and writes seed numbers.
    ///
    /// Seeds are written to the database rather than computed on the fly so that
    /// a bracket, once shown, does not reshuffle because somebody's rating moved
    /// in another organiser's event overnight.
    /// </summary>
    private async Task AssignSeedsAsync(Guid tournamentId)
    {
        var entries = await db.Registrations
            .Where(r => r.TournamentId == tournamentId && r.WithdrawnAt == null)
            .ToListAsync();

        var teamIds = entries.Select(e => e.TeamId).ToList();
        var game = await db.Tournaments.Where(t => t.Id == tournamentId).Select(t => t.Game).FirstAsync();

        var ratings = await db.Ratings
            .Where(r => r.TeamId != null && teamIds.Contains(r.TeamId!.Value) && r.Game == game)
            .ToDictionaryAsync(r => r.TeamId!.Value, r => r);

        var order = Seeding.Order(entries.Select(e =>
        {
            ratings.TryGetValue(e.TeamId, out var rating);
            return new Entrant(
                e.TeamId,
                rating?.Value,
                // Same threshold the ranking page uses, taken from the engine
                // rather than copied as a number (025).
                rating is not null && new Domain.Ratings.Glicko2Rating(rating.Value, rating.Deviation, rating.Volatility).IsProvisional,
                e.RegisteredAt);
        }));

        for (var i = 0; i < order.Count; i++)
            entries.First(e => e.TeamId == order[i]).Seed = i + 1;
    }

    // ── loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync(Guid id)
    {
        var tournament = await db.FindForManagementAsync(id);
        if (tournament is null) return;

        var actor = await Actors.CurrentAsync(db, User);
        CanManage = actor is not null
            && AccessPolicy.Can(actor, Operation.RunTournament, new ResourceRef(tournament.OrganiserId));

        // A draft is private: it exists for whoever runs it and nobody else (046).
        if (!TournamentLifecycle.IsPublic(tournament.Status) && !CanManage) return;

        Tournament = tournament;
        OrganiserName = await db.Organisers.Where(o => o.Id == tournament.OrganiserId)
            .Select(o => o.Name).FirstAsync();

        var rows = await db.PublicRegistrations(id)
            .Join(db.Teams, r => r.TeamId, t => t.Id, (r, t) => new { r.TeamId, t.Name, t.Tag, r.Seed })
            .AsNoTracking()
            .ToListAsync();

        var required = (int)tournament.TeamSize;
        var entries = new List<Entry>();
        foreach (var row in rows)
        {
            var fieldable = await FieldableCountAsync(row.TeamId);
            entries.Add(new Entry(row.TeamId, row.Name, row.Tag, row.Seed, fieldable, fieldable >= required));
        }

        // Seeded order once seeds exist, alphabetical before that — never a
        // random-looking order that changes between refreshes.
        Entries = [.. entries.OrderBy(e => e.Seed ?? int.MaxValue).ThenBy(e => e.Name)];

        if (Sessions.AccountIdOf(User) is not { } accountId) return;

        var captained = await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.LeftAt == null && m.JoinedAt != null
                        && m.Role == TeamRole.Captain)
            .Join(db.Teams.Where(t => t.DisbandedAt == null),
                m => m.TeamId, t => t.Id, (m, t) => new { t.Id, t.Name })
            .AsNoTracking()
            .ToListAsync();

        var mine = new List<(Guid, string, int, bool)>();
        foreach (var team in captained)
            mine.Add((team.Id, team.Name, await FieldableCountAsync(team.Id),
                Entries.Any(e => e.TeamId == team.Id)));

        MyTeams = mine;
    }

    /// <summary>
    /// How many accepted members of this team may actually take a slot in a
    /// match — <b>coaches excluded</b> (045).
    /// </summary>
    private async Task<int> FieldableCountAsync(Guid teamId)
    {
        var slots = await db.TeamMemberships
            .Where(m => m.TeamId == teamId && m.LeftAt == null && m.JoinedAt != null)
            .Select(m => m.Slot)
            .ToListAsync();

        return slots.Count(RosterRules.IsFieldable);
    }

    public static string Initials(string name) => Teams.DetailsModel.Initials(name);
    public static int Palette(string tag) => Teams.DetailsModel.Palette(tag);

    public static string StatusName(TournamentStatus status) => status switch
    {
        TournamentStatus.Draft => "Szkic",
        TournamentStatus.RegistrationOpen => "Zapisy otwarte",
        TournamentStatus.RegistrationClosed => "Zapisy zamknięte",
        TournamentStatus.Running => "W trakcie",
        TournamentStatus.Completed => "Zakończony",
        TournamentStatus.Cancelled => "Odwołany",
        _ => status.ToString(),
    };
}
