using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Ratings;
using NextSkill.Domain.Recruitment;

namespace NextSkill.Api.Pages.Lft;

/// <summary>Which side of the board is being read (decision 056).</summary>
public enum BoardView { Teams, Players }

/// <summary>
/// The LFT board (decision 056).
///
/// <b>One board with two explicit sides, never a mixed feed.</b> The directions
/// carry different facts and different actions, and merging them would produce a
/// list where half the rows cannot be acted on.
///
/// <b>Freshness orders it. Rating never buys prominence.</b> A
/// highest-rating-first board turns evidence into permanent visibility and
/// starves exactly the new players the product exists to give a first team —
/// so rating is shown and filterable and never sorted by.
/// </summary>
public class IndexModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public const int PageSize = 20;

    public sealed record Card(
        Guid Id,
        string Title,
        string? Subtitle,
        Guid SubjectId,
        string[] Roles,
        string[] Slots,
        string[] Languages,
        string[] Regions,
        string? Note,
        string? Availability,
        double? Rating,
        double? Deviation,
        int Matches,
        bool Provisional,
        bool Unrated,
        DateTimeOffset PublishedAt);

    [BindProperty(SupportsGet = true, Name = "game")]
    public Game Game { get; set; } = Game.Cs2;

    /// <summary>
    /// Defaults to teams recruiting: the visitor who arrived from a shared link
    /// is far more often a player looking for somewhere to play (056).
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "view")]
    public BoardView View { get; set; } = BoardView.Teams;

    [BindProperty(SupportsGet = true, Name = "role")]
    public string? Role { get; set; }

    [BindProperty(SupportsGet = true, Name = "slot")]
    public string? Slot { get; set; }

    [BindProperty(SupportsGet = true, Name = "lang")]
    public string? Language { get; set; }

    [BindProperty(SupportsGet = true, Name = "region")]
    public string? Region { get; set; }

    [BindProperty(SupportsGet = true, Name = "skip")]
    public int Skip { get; set; }

    // ── the schedule filter (056) ────────────────────────────────────────
    //
    // In the URL like every other filter, so a narrowed board stays shareable —
    // and expressed as an explicit window rather than "my availability", because
    // the board browses without an account and a filter that silently means
    // something different when signed in is a filter nobody can reason about.

    [BindProperty(SupportsGet = true, Name = "day")]
    public int? Day { get; set; }

    [BindProperty(SupportsGet = true, Name = "from")]
    public int? From { get; set; }

    [BindProperty(SupportsGet = true, Name = "to")]
    public int? To { get; set; }

    /// <summary>Defaults to the scene's zone rather than guessing from a header.</summary>
    [BindProperty(SupportsGet = true, Name = "tz")]
    public string Zone { get; set; } = RecruitmentRules.TimeZones[0];

    /// <summary>
    /// The requested window as UTC hours-of-week, empty when not filtering.
    ///
    /// Computed against <b>this week</b>, exactly as a freshly published advert
    /// is — which is what makes a filter written in one zone find an advert
    /// published in another whose daylight-saving rules differ.
    /// </summary>
    private int[] WantedHours =>
        Day is { } day && From is { } from && To is { } to && RecruitmentRules.Zone(Zone) is { } zone
            ? RecruitmentRules.ToUtcHours(
                [new RecruitmentRules.Window(day, from, to)], zone, clock.GetUtcNow())
            : [];

    public bool FilteringSchedule => WantedHours.Length > 0;

    public IReadOnlyList<Card> Cards { get; private set; } = [];
    public bool HasMore { get; private set; }
    public bool AnyFilter => Role is not null || Slot is not null || Language is not null
        || Region is not null || FilteringSchedule;

    /// <summary>True when this game/view is empty regardless of filters.</summary>
    public bool WholeViewEmpty { get; private set; }

    public sealed class ActionInput
    {
        public Guid Id { get; set; }
        public Guid OpeningId { get; set; }
        public string? Note { get; set; }
    }

    [BindProperty]
    public ActionInput Action { get; set; } = new();

    public string? ActionError { get; private set; }
    public string? ActionNotice { get; private set; }

    /// <summary>
    /// Somebody flags a public advert for a human to look at (056).
    ///
    /// <b>Kept apart from decline and block on purpose.</b> An ordinary decline
    /// must never be recorded as an abuse allegation, and a block is a private
    /// pair-level control needing no moderator — only this enters a queue.
    ///
    /// The target is resolved by looking the id up in both tables rather than
    /// trusting the board view, which arrives in the query string and would
    /// otherwise decide which table a hand-written POST wrote a report against.
    /// </summary>
    public async Task<IActionResult> OnPostReportAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var reason = Action.Note?.Trim();
        if (reason is { Length: > 500 }) return BadRequest();

        var listingId = await db.LftListings
            .Where(l => l.Id == Action.Id).Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var openingId = listingId is null
            ? await db.TeamOpenings
                .Where(o => o.Id == Action.Id).Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (listingId is null && openingId is null) return NotFound();

        db.AdvertReports.Add(new AdvertReport
        {
            Id = Guid.NewGuid(),
            ReporterAccountId = accountId,
            LftListingId = listingId,
            TeamOpeningId = openingId,
            Reason = string.IsNullOrEmpty(reason) ? null : reason,
            CreatedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Already reported by this person. The database cap is what stops
            // one reporter flooding the queue, and hitting it means the intent
            // is already recorded — so this is success, not an error.
            db.ChangeTracker.Clear();
        }

        ActionNotice = "Zgłoszenie trafiło do moderacji.";
        await OnGetAsync(cancellationToken);
        return Page();
    }

    /// <summary>
    /// A player asks about one opening.
    ///
    /// A public LFT listing is deliberately not required: 049's private
    /// application path stays valid, and demanding an advert first would make
    /// asking about a team harder than it is on Discord.
    /// </summary>
    public async Task<IActionResult> OnPostApplyAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var opening = await db.TeamOpenings
            .FirstOrDefaultAsync(o => o.Id == Action.Id, cancellationToken);
        if (opening is null) return NotFound();

        var now = clock.GetUtcNow();
        var refusal = await Features.Recruitment.Recruitment
            .RejectApplicationAsync(db, opening, accountId, now, cancellationToken);

        if (refusal is not null)
        {
            ActionError = refusal;
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var note = Action.Note?.Trim();
        if (note is { Length: > RecruitmentRules.MaxNoteLength }) return BadRequest();

        db.OpeningApplications.Add(new OpeningApplication
        {
            Id = Guid.NewGuid(),
            TeamOpeningId = opening.Id,
            AccountId = accountId,
            Cycle = opening.Cycle,
            State = RecruitmentState.Pending,
            Note = note,
            CreatedAt = now,
            ExpiresAt = now + RecruitmentRules.RequestLifetime,
        });

        await db.SaveChangesAsync(cancellationToken);
        return Redirect($"/{Localisation.Locales.Current}/me/recruitment");
    }

    /// <summary>
    /// A captain approaches one listed player <b>on behalf of one real
    /// opening</b>. There is no context-free approach, by construction: the
    /// opening id is required and is validated against the captain's own teams.
    /// </summary>
    public async Task<IActionResult> OnPostInviteAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var listing = await db.LftListings
            .FirstOrDefaultAsync(l => l.Id == Action.Id, cancellationToken);
        if (listing is null) return NotFound();

        var opening = await db.TeamOpenings
            .FirstOrDefaultAsync(o => o.Id == Action.OpeningId, cancellationToken);
        if (opening is null) return NotFound();

        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: opening.TeamId)))
            return Forbid();

        var now = clock.GetUtcNow();
        var refusal = await Features.Recruitment.Recruitment
            .RejectInterestAsync(db, opening, listing, now, cancellationToken);

        if (refusal is not null)
        {
            ActionError = refusal;
            await OnGetAsync(cancellationToken);
            return Page();
        }

        var note = Action.Note?.Trim();
        if (note is { Length: > RecruitmentRules.MaxNoteLength }) return BadRequest();

        db.TeamInterests.Add(new TeamInterest
        {
            Id = Guid.NewGuid(),
            TeamOpeningId = opening.Id,
            AccountId = listing.AccountId,
            LftListingId = listing.Id,
            Cycle = opening.Cycle,
            State = RecruitmentState.Pending,
            Note = note,
            CreatedAt = now,
            ExpiresAt = now + RecruitmentRules.RequestLifetime,
        });

        await db.SaveChangesAsync(cancellationToken);
        return Redirect($"/{Localisation.Locales.Current}/me/recruitment");
    }

    /// <summary>Openings this visitor captains, for approaching a listed player.</summary>
    public IReadOnlyList<(Guid Id, string Label)> MyOpenings { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(Game) || !Enum.IsDefined(View))
            return BadRequest("Nieprawidłowy filtr tablicy.");

        // An unknown filter value is refused rather than ignored. Silently
        // dropping it would show a broader result set than the URL claims,
        // which is the "silent filter relaxation" 056 rejects by name.
        if (Role is not null && !RecruitmentRules.Roles.Contains(Role)) return BadRequest("Nieznana rola.");
        if (Slot is not null && !RecruitmentRules.Slots.Contains(Slot)) return BadRequest("Nieznane miejsce.");
        if (Language is not null && !RecruitmentRules.Languages.Contains(Language)) return BadRequest("Nieznany język.");
        if (Region is not null && !RecruitmentRules.Regions.Contains(Region)) return BadRequest("Nieznany region.");

        // Refused rather than dropped, exactly like the others above: a window
        // that silently stops applying shows a broader set than the URL claims.
        if (!RecruitmentRules.TimeZones.Contains(Zone)) return BadRequest("Nieznana strefa czasowa.");
        if (Day is { } day && day is < 0 or > 6) return BadRequest("Nieprawidłowy dzień.");
        if (From is { } from && from is < 0 or > 23) return BadRequest("Nieprawidłowa godzina.");
        if (To is { } to && to is < 1 or > 24) return BadRequest("Nieprawidłowa godzina.");
        if (From is { } f && To is { } t && t <= f) return BadRequest("Nieprawidłowy zakres godzin.");

        Skip = Math.Clamp(Skip, 0, 10_000);

        // Volatile state: worth following links out of, not worth indexing.
        Response.Headers["X-Robots-Tag"] = "noindex, follow";

        if (View == BoardView.Teams)
        {
            await LoadOpeningsAsync(cancellationToken);
        }
        else
        {
            await LoadPlayersAsync(cancellationToken);

            // Only needed on the players view, and only for a captain: this is
            // what makes "approach on behalf of a real opening" a choice the UI
            // can offer rather than a rule the server merely enforces.
            var actor = await Actors.CurrentAsync(db, User, cancellationToken);
            if (actor?.Teams is { Count: > 0 })
            {
                var captainTeams = actor.Teams
                    .Where(t => t.Value == TeamRole.Captain)
                    .Select(t => t.Key)
                    .ToList();

                if (captainTeams.Count > 0)
                {
                    var now = clock.GetUtcNow();
                    MyOpenings = await db.TeamOpenings
                        .Where(o => captainTeams.Contains(o.TeamId) && o.Game == Game &&
                                    o.ClosedAt == null && o.ExpiresAt > now)
                        .Join(db.Teams, o => o.TeamId, t => t.Id,
                            (o, t) => new { o.Id, Label = t.Name + " — " + o.Slot })
                        .AsNoTracking()
                        .Select(x => new ValueTuple<Guid, string>(x.Id, x.Label))
                        .ToListAsync(cancellationToken);
                }
            }
        }

        return Page();
    }

    private async Task LoadOpeningsAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var live = db.TeamOpenings
            .Where(o => o.Game == Game && o.ClosedAt == null && o.ExpiresAt > now);

        WholeViewEmpty = !await live.AnyAsync(cancellationToken);

        var query = live;
        if (Role is not null) query = query.Where(o => o.Roles.Contains(Role));
        if (Slot is not null) query = query.Where(o => o.Slot.ToString().ToLower() == Slot);
        if (Language is not null) query = query.Where(o => o.Languages.Contains(Language));
        if (Region is not null) query = query.Where(o => o.Regions.Contains(Region));

        // Genuine weekly overlap, answered by PostgreSQL as an array
        // intersection — which is the entire reason the hours were normalised to
        // UTC on write instead of reconciled per row (056).
        //
        // It must reach SQL as `&&` rather than an EXISTS over unnest(), because
        // `&&` is the only array predicate the GIN index on availability_hours
        // can answer. A check asserts the generated SQL, since the shape of this
        // expression is what decides it.
        if (WantedHours is { Length: > 0 } wantedForOpenings)
            query = query.Where(o => o.AvailabilityHours.Any(h => wantedForOpenings.Contains(h)));

        var rows = await query
            .OrderByDescending(o => o.PublishedAt)
            .ThenBy(o => o.Id)
            .Skip(Skip)
            .Take(PageSize + 1)
            .Join(db.Teams, o => o.TeamId, t => t.Id, (o, t) => new { Opening = o, Team = t })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        HasMore = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();
        if (rows.Count == 0) return;

        // Derived from the SAME projection the ranking uses. Recruitment stores
        // no second copy of a rating — a self-minted number is precisely what a
        // Discord advert already offers (056).
        var teamIds = rows.Select(r => r.Team.Id).ToList();
        var ratings = await db.Ratings.AsNoTracking()
            .Where(r => r.TeamId != null && teamIds.Contains(r.TeamId!.Value) && r.Game == Game)
            .ToDictionaryAsync(r => r.TeamId!.Value, cancellationToken);

        Cards = rows.Select(r =>
        {
            var rating = ratings.GetValueOrDefault(r.Team.Id);
            return new Card(
                r.Opening.Id,
                r.Team.Name,
                r.Team.Tag,
                r.Team.Id,
                r.Opening.Roles,
                [r.Opening.Slot.ToString().ToLowerInvariant()],
                r.Opening.Languages,
                r.Opening.Regions,
                r.Opening.Note,
                Features.Recruitment.AvailabilityText.Describe(
                    r.Opening.AvailabilityHours, r.Opening.TimeZoneId, r.Opening.PublishedAt),
                rating?.Value,
                rating?.Deviation,
                rating?.MatchesPlayed ?? 0,
                rating is not null && rating.Deviation > Glicko2Rating.ProvisionalDeviation,
                rating is null,
                r.Opening.PublishedAt);
        }).ToArray();
    }

    private async Task LoadPlayersAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var live = db.LftListings
            .Where(l => l.Game == Game && l.ClosedAt == null && l.ExpiresAt > now);

        WholeViewEmpty = !await live.AnyAsync(cancellationToken);

        var query = live;
        if (Role is not null) query = query.Where(l => l.Roles.Contains(Role));
        if (Slot is not null) query = query.Where(l => l.Slots.Contains(Slot));
        if (Language is not null) query = query.Where(l => l.Languages.Contains(Language));
        if (Region is not null) query = query.Where(l => l.Regions.Contains(Region));

        if (WantedHours is { Length: > 0 } wantedForPlayers)
            query = query.Where(l => l.AvailabilityHours.Any(h => wantedForPlayers.Contains(h)));

        var rows = await query
            .OrderByDescending(l => l.PublishedAt)
            .ThenBy(l => l.Id)
            .Skip(Skip)
            .Take(PageSize + 1)
            .Join(db.Accounts.Where(a => a.DeletedAt == null && a.BannedAt == null),
                l => l.AccountId, a => a.Id, (l, a) => new { Listing = l, Account = a })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        HasMore = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();
        if (rows.Count == 0) return;

        var accountIds = rows.Select(r => r.Account.Id).ToList();
        var ratings = await db.Ratings.AsNoTracking()
            .Where(r => r.AccountId != null && accountIds.Contains(r.AccountId!.Value) && r.Game == Game)
            .ToDictionaryAsync(r => r.AccountId!.Value, cancellationToken);

        Cards = rows.Select(r =>
        {
            var rating = ratings.GetValueOrDefault(r.Account.Id);
            return new Card(
                r.Listing.Id,
                r.Account.Nickname,
                null,
                r.Account.Id,
                r.Listing.Roles,
                r.Listing.Slots,
                r.Listing.Languages,
                r.Listing.Regions,
                r.Listing.Note,
                Features.Recruitment.AvailabilityText.Describe(
                    r.Listing.AvailabilityHours, r.Listing.TimeZoneId, r.Listing.PublishedAt),
                // OpenSkill's canonical conservative score, exactly as ranking
                // and profile display it.
                rating is null ? null : rating.Value - 3 * rating.Deviation,
                rating?.Deviation,
                rating?.MatchesPlayed ?? 0,
                rating is not null && rating.Deviation > PlayerRating.ProvisionalSigma,
                rating is null,
                r.Listing.PublishedAt);
        }).ToArray();
    }
}
