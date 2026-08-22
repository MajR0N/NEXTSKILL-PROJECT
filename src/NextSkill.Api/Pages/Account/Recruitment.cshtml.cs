using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Recruitment;
using NextSkill.Domain.Teams;
using System.Text.Json;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// <c>/me/recruitment</c> — the one private management surface (decision 056).
///
/// Everything reversible about recruitment lives here: the player's own advert,
/// the openings of every team they captain, both directions of pending request,
/// and the contacts that were mutually agreed. Deliberately <b>not</b> scattered
/// across public pages, and deliberately not dependent on notification history —
/// a request you cannot find because you dismissed a bell is a request that will
/// be ignored.
/// </summary>
public class RecruitmentModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed class ListingInput
    {
        public Game Game { get; set; } = Game.Cs2;
        public List<string> Roles { get; set; } = [];
        public List<string> Slots { get; set; } = [];
        public List<string> Languages { get; set; } = [];
        public List<string> Regions { get; set; } = [];
        public string? Note { get; set; }

        // Availability, one window PER WEEKDAY: seven optional rows, Monday
        // first. Normalised to UTC hours-of-week on write, so "do these two
        // genuinely overlap?" is an array intersection the database answers.
        //
        // A row where the two hours are equal means "not that day", so weekend
        // mornings and midweek evenings are both expressible — the shape a
        // single window could not hold.
        public List<int> FromHours { get; set; } = [];
        public List<int> ToHours { get; set; } = [];
        public string TimeZoneId { get; set; } = RecruitmentRules.TimeZones[0];
    }

    public sealed class OpeningInput
    {
        public Guid TeamId { get; set; }
        public Game Game { get; set; } = Game.Cs2;
        public RecruitmentSlot Slot { get; set; } = RecruitmentSlot.Player;
        public List<string> Roles { get; set; } = [];
        public List<string> Languages { get; set; } = [];
        public List<string> Regions { get; set; } = [];
        public string? Note { get; set; }

        // Availability, one window PER WEEKDAY: seven optional rows, Monday
        // first. Normalised to UTC hours-of-week on write, so "do these two
        // genuinely overlap?" is an array intersection the database answers.
        //
        // A row where the two hours are equal means "not that day", so weekend
        // mornings and midweek evenings are both expressible — the shape a
        // single window could not hold.
        public List<int> FromHours { get; set; } = [];
        public List<int> ToHours { get; set; } = [];
        public string TimeZoneId { get; set; } = RecruitmentRules.TimeZones[0];
    }

    public sealed class RequestInput
    {
        public Guid Id { get; set; }
        public string? Note { get; set; }
    }

    public sealed record ListingRow(
        Guid Id, Game Game, string[] Roles, string[] Slots, string? Note,
        DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt, bool Expired);

    public sealed record OpeningRow(
        Guid Id, Guid TeamId, string Team, Game Game, RecruitmentSlot Slot,
        string[] Roles, string? Note, DateTimeOffset ExpiresAt, bool Expired,
        bool HasContactCaptain, int PendingRequests);

    public sealed record RequestRow(
        Guid Id, string Direction, string Who, string Team, RecruitmentSlot Slot,
        string? Note, RecruitmentState State, DateTimeOffset CreatedAt, bool ActionableByMe);

    public sealed record ContactRow(
        Guid OpeningId, Guid TeamId, string Team, string Who, string? Contact,
        DateTimeOffset ExpiresAt, RecruitmentSlot Slot, bool CanConvert, string? CannotConvertBecause);

    [BindProperty] public ListingInput Listing { get; set; } = new();
    [BindProperty] public OpeningInput Opening { get; set; } = new();
    [BindProperty] public RequestInput Request { get; set; } = new();

    public IReadOnlyList<ListingRow> MyListings { get; private set; } = [];
    public IReadOnlyList<OpeningRow> MyOpenings { get; private set; } = [];
    public IReadOnlyList<RequestRow> Pending { get; private set; } = [];
    public IReadOnlyList<RequestRow> Closed { get; private set; } = [];
    public IReadOnlyList<ContactRow> Contacts { get; private set; } = [];
    public IReadOnlyList<(Guid Id, string Name)> CaptainOf { get; private set; } = [];

    /// <summary>
    /// Whether this account has a verified contact it can share at all.
    ///
    /// Publishing or responding needs one, because the whole exchange ends in a
    /// contact crossing. Discovering that only at the moment of acceptance would
    /// waste the other person's time (056).
    /// </summary>
    public bool HasShareableContact { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();
        await LoadAsync(accountId, cancellationToken);
        return Page();
    }

    // ── the player's own advert ──────────────────────────────────────────

    public async Task<IActionResult> OnPostPublishListingAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var note = Listing.Note?.Trim();
        var now = clock.GetUtcNow();
        var hours = AvailabilityOf(Listing.FromHours, Listing.ToHours, Listing.TimeZoneId, now);
        var reason = RecruitmentRules.RejectPublication(
            Listing.Roles, Listing.Slots, Listing.Languages, Listing.Regions, hours, note)
            ?? RejectAvailability(Listing.FromHours, Listing.ToHours, Listing.TimeZoneId);

        if (reason is not null)
        {
            ModelState.AddModelError(string.Empty, reason);
            await LoadAsync(accountId, cancellationToken);
            return Page();
        }

        if (!await CanShareContactAsync(accountId, cancellationToken))
        {
            ModelState.AddModelError(string.Empty,
                "Najpierw połącz konto Discord lub Steam — bez tego nie ma czego udostępnić po akceptacji.");
            await LoadAsync(accountId, cancellationToken);
            return Page();
        }

        var existing = await db.LftListings
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.Game == Listing.Game, cancellationToken);

        if (existing is null)
        {
            db.LftListings.Add(new LftListing
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Game = Listing.Game,
                Roles = [.. Listing.Roles],
                Slots = [.. Listing.Slots],
                Languages = [.. Listing.Languages],
                Regions = [.. Listing.Regions],
                AvailabilityHours = hours,
                TimeZoneId = Listing.TimeZoneId,
                Note = note,
                Cycle = 1,
                PublishedAt = now,
                ExpiresAt = now + RecruitmentRules.AdvertLifetime,
                CreatedAt = now,
            });
        }
        else if (existing.ModeratedAt is not null)
        {
            // The one place a takedown has to hold. A listing is ONE row per
            // account and game, reused on republication — so without this the
            // author reopens the moderated advert with a single click and the
            // queue is theatre (015, 056).
            ModelState.AddModelError(string.Empty,
                "To ogłoszenie zostało zdjęte przez moderację i nie można go opublikować ponownie. " +
                "Napisz do nas, jeśli uważasz, że to pomyłka.");
            await LoadAsync(accountId, cancellationToken);
            return Page();
        }
        else
        {
            // A material change is a NEW advert: it starts a fresh cycle, moves
            // the row and neutrally closes unanswered requests from the old one.
            // Editing only the note does none of that, or every advertiser would
            // learn to tweak a full stop each morning and freshness would stop
            // meaning anything (056).
            var material = RecruitmentRules.IsMaterialChange(
                existing.Roles, Listing.Roles,
                existing.Slots, Listing.Slots,
                existing.Languages, Listing.Languages,
                existing.Regions, Listing.Regions,
                existing.AvailabilityHours, hours);

            existing.Roles = [.. Listing.Roles];
            existing.Slots = [.. Listing.Slots];
            existing.Languages = [.. Listing.Languages];
            existing.Regions = [.. Listing.Regions];
            existing.AvailabilityHours = hours;
            existing.TimeZoneId = Listing.TimeZoneId;
            existing.Note = note;
            existing.ClosedAt = null;

            if (material || existing.ExpiresAt <= now)
            {
                existing.Cycle++;
                existing.PublishedAt = now;
                existing.ExpiresAt = now + RecruitmentRules.AdvertLifetime;
                existing.ReminderSentAt = null;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Back();
    }

    public async Task<IActionResult> OnPostCloseListingAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var listing = await db.LftListings
            .FirstOrDefaultAsync(l => l.Id == Request.Id && l.AccountId == accountId, cancellationToken);
        if (listing is null) return NotFound();

        // Closed, not deleted: it stays privately reviewable so republishing is
        // one action rather than retyping everything.
        listing.ClosedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Back();
    }

    // ── a team's openings ────────────────────────────────────────────────

    public async Task<IActionResult> OnPostPublishOpeningAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        // Only a current captain publishes a vacancy on behalf of a team.
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: Opening.TeamId)))
            return Forbid();

        var note = Opening.Note?.Trim();
        var openingNow = clock.GetUtcNow();
        var hours = AvailabilityOf(Opening.FromHours, Opening.ToHours, Opening.TimeZoneId, openingNow);
        var reason = RecruitmentRules.RejectPublication(
            Opening.Roles, [Opening.Slot.ToString().ToLowerInvariant()],
            Opening.Languages, Opening.Regions, hours, note)
            ?? RejectAvailability(Opening.FromHours, Opening.ToHours, Opening.TimeZoneId);

        if (reason is not null)
        {
            ModelState.AddModelError(string.Empty, reason);
            await LoadAsync(accountId, cancellationToken);
            return Page();
        }

        if (!await CanShareContactAsync(accountId, cancellationToken))
        {
            ModelState.AddModelError(string.Empty,
                "Połącz konto Discord lub Steam — kapitan do kontaktu musi mieć co udostępnić.");
            await LoadAsync(accountId, cancellationToken);
            return Page();
        }

        // Spare capacity is not an opening, but an opening beyond capacity is a
        // promise the roster cannot keep (045, 056).
        var slot = Opening.Slot switch
        {
            RecruitmentSlot.Player => NextSkill.Domain.Teams.RosterSlot.Player,
            RecruitmentSlot.Substitute => NextSkill.Domain.Teams.RosterSlot.Substitute,
            _ => NextSkill.Domain.Teams.RosterSlot.Coach,
        };

        var occupied = await db.TeamMemberships.CountAsync(
            m => m.TeamId == Opening.TeamId && m.LeftAt == null && m.Slot == slot, cancellationToken);

        var openHere = await db.TeamOpenings.CountAsync(
            o => o.TeamId == Opening.TeamId && o.Slot == Opening.Slot && o.ClosedAt == null, cancellationToken);

        if (occupied + openHere >= NextSkill.Domain.Teams.RosterRules.Capacity(slot))
        {
            ModelState.AddModelError(string.Empty,
                "Nie ma wolnego miejsca na to ogłoszenie — najpierw zwolnij miejsce w składzie.");
            await LoadAsync(accountId, cancellationToken);
            return Page();
        }

        var now = openingNow;
        db.TeamOpenings.Add(new TeamOpening
        {
            Id = Guid.NewGuid(),
            TeamId = Opening.TeamId,
            Game = Opening.Game,
            Slot = Opening.Slot,
            ContactCaptainAccountId = accountId,
            Roles = [.. Opening.Roles],
            Languages = [.. Opening.Languages],
            Regions = [.. Opening.Regions],
            AvailabilityHours = hours,
            TimeZoneId = Opening.TimeZoneId,
            Note = note,
            Cycle = 1,
            PublishedAt = now,
            ExpiresAt = now + RecruitmentRules.AdvertLifetime,
            CreatedAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
        return Back();
    }

    public async Task<IActionResult> OnPostCloseOpeningAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var opening = await db.TeamOpenings.FirstOrDefaultAsync(o => o.Id == Request.Id, cancellationToken);
        if (opening is null) return NotFound();

        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: opening.TeamId)))
            return Forbid();

        var now = clock.GetUtcNow();
        opening.ClosedAt = now;

        // Closing takes its unanswered requests with it. Leaving them pending
        // would hold somebody's cap against a vacancy that no longer exists.
        await db.OpeningApplications
            .Where(a => a.TeamOpeningId == opening.Id && a.State == RecruitmentState.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.State, RecruitmentState.OpeningClosed)
                .SetProperty(a => a.ResolvedAt, now), cancellationToken);

        await db.TeamInterests
            .Where(i => i.TeamOpeningId == opening.Id && i.State == RecruitmentState.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.State, RecruitmentState.OpeningClosed)
                .SetProperty(i => i.ResolvedAt, now), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Back();
    }

    // ── responding, in both directions ───────────────────────────────────

    public Task<IActionResult> OnPostAcceptAsync(CancellationToken ct) => RespondAsync(RecruitmentState.Accepted, ct);
    public Task<IActionResult> OnPostDeclineAsync(CancellationToken ct) => RespondAsync(RecruitmentState.Declined, ct);
    public Task<IActionResult> OnPostWithdrawAsync(CancellationToken ct) => RespondAsync(RecruitmentState.Withdrawn, ct);

    private async Task<IActionResult> RespondAsync(RecruitmentState outcome, CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var now = clock.GetUtcNow();

        var application = await db.OpeningApplications
            .FirstOrDefaultAsync(a => a.Id == Request.Id && a.State == RecruitmentState.Pending, cancellationToken);

        if (application is not null)
        {
            var opening = await db.TeamOpenings.FirstAsync(o => o.Id == application.TeamOpeningId, cancellationToken);
            var isCaptain = AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: opening.TeamId));
            var isApplicant = application.AccountId == accountId;

            // The applicant may only withdraw; the captain may only accept or
            // decline. Neither can perform the other's action.
            if (outcome == RecruitmentState.Withdrawn ? !isApplicant : !isCaptain)
                return Forbid();

            application.State = outcome;
            application.ResolvedAt = now;

            if (outcome == RecruitmentState.Accepted)
                Features.Recruitment.Recruitment.Accept(db, opening, application.AccountId, now);

            await db.SaveChangesAsync(cancellationToken);
            return Back();
        }

        var interest = await db.TeamInterests
            .FirstOrDefaultAsync(i => i.Id == Request.Id && i.State == RecruitmentState.Pending, cancellationToken);

        if (interest is null) return NotFound();

        var targetOpening = await db.TeamOpenings.FirstAsync(o => o.Id == interest.TeamOpeningId, cancellationToken);
        var captain = AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: targetOpening.TeamId));
        var player = interest.AccountId == accountId;

        // Mirrored: the team withdraws its own approach, the player accepts or
        // declines it.
        if (outcome == RecruitmentState.Withdrawn ? !captain : !player)
            return Forbid();

        interest.State = outcome;
        interest.ResolvedAt = now;

        if (outcome == RecruitmentState.Accepted)
            Features.Recruitment.Recruitment.Accept(db, targetOpening, interest.AccountId, now);

        await db.SaveChangesAsync(cancellationToken);
        return Back();
    }

    /// <summary>
    /// A durable pair-level refusal, in both directions and across cycles.
    ///
    /// Separate from decline, which binds to one cycle, and separate from
    /// report, which is an abuse allegation. Conflating them would put words in
    /// somebody's mouth (056).
    /// </summary>
    /// <summary>
    /// Turn a mutual contact into the roster invitation that is the only thing
    /// counting as recruitment success (decision 056).
    ///
    /// <b>It creates an invitation, not a membership.</b> 056 is explicit: the
    /// player must still accept under ordinary capacity checks, and the opening
    /// is filled by that acceptance rather than by a captain deciding it is.
    /// Conversion that seated somebody would make the board's success metric a
    /// number the recruiter controls.
    ///
    /// The invitation is the ordinary one from [045](045-teams-and-rosters.md),
    /// carrying the opening it came from — which is what keeps "a player joined"
    /// and "the board filled a vacancy" separately measurable.
    /// </summary>
    public async Task<IActionResult> OnPostConvertAsync(Guid openingId, CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var now = clock.GetUtcNow();

        var opening = await db.TeamOpenings.FirstOrDefaultAsync(o => o.Id == openingId, cancellationToken);
        if (opening is null) return NotFound();

        // Any CURRENT captain may act, not only the designated contact: the
        // designated captain is whose identifier is revealed, which is a privacy
        // rule rather than an authority one (056).
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: opening.TeamId)))
            return Forbid();

        // A live grant is the whole permission to approach this person at all.
        var grant = await db.ContactGrants.FirstOrDefaultAsync(
            g => g.TeamOpeningId == openingId && g.RevokedAt == null && g.ExpiresAt > now,
            cancellationToken);
        if (grant is null) return NotFound();

        if (opening.ClosedAt is not null)
        {
            ModelState.AddModelError(string.Empty, "To ogłoszenie jest już zamknięte.");
            await LoadAsync(actor.AccountId, cancellationToken);
            return Page();
        }

        var slot = SlotOf(opening.Slot);
        var occupied = await db.TeamMemberships.CountAsync(
            m => m.TeamId == opening.TeamId && m.LeftAt == null && m.Slot == slot, cancellationToken);

        // The same cap the ordinary invite path applies, and for the same
        // reason: a pending invite occupies a place (045).
        if (!RosterRules.CanAdd(slot, occupied))
        {
            ModelState.AddModelError(string.Empty,
                "Brak wolnego miejsca w składzie na to ogłoszenie — zwolnij miejsce i spróbuj ponownie.");
            await LoadAsync(actor.AccountId, cancellationToken);
            return Page();
        }

        var team = await db.Teams.FirstAsync(t => t.Id == opening.TeamId, cancellationToken);

        db.TeamMemberships.Add(new TeamMembership
        {
            Id = Guid.NewGuid(),
            TeamId = opening.TeamId,
            AccountId = grant.AccountId,
            Role = TeamRole.Member,
            Slot = slot,
            // Pending, exactly like every other invitation.
            JoinedAt = null,
            FromTeamOpeningId = opening.Id,
            CreatedAt = now,
        });

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            AccountId = grant.AccountId,
            Type = NotificationType.TeamInvite,
            Data = JsonSerializer.SerializeToDocument(new
            {
                teamId = opening.TeamId, team = team.Name, tag = team.Tag, slot = slot.ToString(),
            }),
            CreatedAt = now,
        });

        // The request behind the grant is now answered. `Converted` is the only
        // state 056 counts as a conversation that went somewhere.
        await db.OpeningApplications
            .Where(a => a.TeamOpeningId == openingId && a.AccountId == grant.AccountId &&
                        a.State == RecruitmentState.Accepted)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.State, RecruitmentState.Converted), cancellationToken);
        await db.TeamInterests
            .Where(i => i.TeamOpeningId == openingId && i.AccountId == grant.AccountId &&
                        i.State == RecruitmentState.Accepted)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.State, RecruitmentState.Converted), cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The partial unique index refuses a second active membership for
            // this person — already invited, or already on the roster.
            ModelState.AddModelError(string.Empty, "Ten gracz jest już w drużynie albo ma zaproszenie.");
            db.ChangeTracker.Clear();
            await LoadAsync(actor.AccountId, cancellationToken);
            return Page();
        }

        return Back();
    }

    private static RosterSlot SlotOf(RecruitmentSlot slot) => slot switch
    {
        RecruitmentSlot.Player => RosterSlot.Player,
        RecruitmentSlot.Substitute => RosterSlot.Substitute,
        _ => RosterSlot.Coach,
    };

    public async Task<IActionResult> OnPostBlockAsync(Guid teamId, CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        if (!await db.RecruitmentBlocks.AnyAsync(
                b => b.AccountId == accountId && b.TeamId == teamId, cancellationToken))
        {
            db.RecruitmentBlocks.Add(new RecruitmentBlock
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                TeamId = teamId,
                ByPlayer = true,
                CreatedAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Back();
    }

    // ── loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        HasShareableContact = await CanShareContactAsync(accountId, cancellationToken);

        MyListings = await db.LftListings
            .Where(l => l.AccountId == accountId && l.ClosedAt == null)
            .OrderBy(l => l.Game)
            .Select(l => new ListingRow(
                l.Id, l.Game, l.Roles, l.Slots, l.Note, l.PublishedAt, l.ExpiresAt, l.ExpiresAt <= now))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var captainTeams = await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.JoinedAt != null && m.LeftAt == null &&
                        m.Role == TeamRole.Captain)
            .Join(db.Teams.Where(t => t.DisbandedAt == null), m => m.TeamId, t => t.Id,
                (m, t) => new { t.Id, t.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        CaptainOf = captainTeams.Select(t => (t.Id, t.Name)).ToArray();
        var teamIds = captainTeams.Select(t => t.Id).ToList();

        MyOpenings = teamIds.Count == 0 ? [] : await db.TeamOpenings
            .Where(o => teamIds.Contains(o.TeamId) && o.ClosedAt == null)
            .OrderByDescending(o => o.PublishedAt)
            .Join(db.Teams, o => o.TeamId, t => t.Id, (o, t) => new OpeningRow(
                o.Id, o.TeamId, t.Name, o.Game, o.Slot, o.Roles, o.Note, o.ExpiresAt,
                o.ExpiresAt <= now,
                o.ContactCaptainAccountId != null,
                db.OpeningApplications.Count(a => a.TeamOpeningId == o.Id && a.State == RecruitmentState.Pending) +
                db.TeamInterests.Count(i => i.TeamOpeningId == o.Id && i.State == RecruitmentState.Pending)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await LoadRequestsAsync(accountId, teamIds, now, cancellationToken);
        await LoadContactsAsync(accountId, teamIds, now, cancellationToken);
    }

    private async Task LoadRequestsAsync(
        Guid accountId, List<Guid> teamIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var applications = await db.OpeningApplications
            .Where(a => a.AccountId == accountId ||
                        db.TeamOpenings.Any(o => o.Id == a.TeamOpeningId && teamIds.Contains(o.TeamId)))
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .Join(db.TeamOpenings, a => a.TeamOpeningId, o => o.Id, (a, o) => new { App = a, Opening = o })
            .Join(db.Teams, x => x.Opening.TeamId, t => t.Id, (x, t) => new { x.App, x.Opening, Team = t })
            .Join(db.Accounts, x => x.App.AccountId, p => p.Id, (x, p) => new
            {
                x.App, x.Opening, x.Team, Player = p.Nickname,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var interests = await db.TeamInterests
            .Where(i => i.AccountId == accountId ||
                        db.TeamOpenings.Any(o => o.Id == i.TeamOpeningId && teamIds.Contains(o.TeamId)))
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .Join(db.TeamOpenings, i => i.TeamOpeningId, o => o.Id, (i, o) => new { Interest = i, Opening = o })
            .Join(db.Teams, x => x.Opening.TeamId, t => t.Id, (x, t) => new { x.Interest, x.Opening, Team = t })
            .Join(db.Accounts, x => x.Interest.AccountId, p => p.Id, (x, p) => new
            {
                x.Interest, x.Opening, x.Team, Player = p.Nickname,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var rows = applications
            .Select(x => new RequestRow(
                x.App.Id, "application",
                x.App.AccountId == accountId ? "Ty" : x.Player,
                x.Team.Name, x.Opening.Slot, x.App.Note, x.App.State, x.App.CreatedAt,
                // The captain acts on an application; the applicant may only withdraw.
                x.App.AccountId != accountId))
            .Concat(interests.Select(x => new RequestRow(
                x.Interest.Id, "interest",
                x.Interest.AccountId == accountId ? x.Team.Name : x.Player,
                x.Team.Name, x.Opening.Slot, x.Interest.Note, x.Interest.State, x.Interest.CreatedAt,
                x.Interest.AccountId == accountId)))
            .ToList();

        Pending = [.. rows.Where(r => r.State == RecruitmentState.Pending).OrderByDescending(r => r.CreatedAt)];

        // Unsuccessful closed requests stay visible privately for fourteen days
        // and are then hard-deleted by the worker.
        Closed = [.. rows
            .Where(r => r.State != RecruitmentState.Pending &&
                        r.CreatedAt > now - RecruitmentRules.ClosedRequestRetention)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)];
    }

    private async Task LoadContactsAsync(
        Guid accountId, List<Guid> teamIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var grants = await db.ContactGrants
            .Where(g => g.RevokedAt == null && g.ExpiresAt > now &&
                        (g.AccountId == accountId || g.CaptainAccountId == accountId))
            .Join(db.TeamOpenings, g => g.TeamOpeningId, o => o.Id, (g, o) => new { Grant = g, Opening = o })
            .Join(db.Teams, x => x.Opening.TeamId, t => t.Id, (x, t) => new { x.Grant, x.Opening, Team = t })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var rows = new List<ContactRow>();
        foreach (var grant in grants)
        {
            // Each side sees exactly one identifier: the other's. The captain's
            // is the DESIGNATED captain's and nobody else's (056).
            var otherId = grant.Grant.AccountId == accountId
                ? grant.Grant.CaptainAccountId
                : grant.Grant.AccountId;

            var nickname = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == otherId)
                .Select(a => a.Nickname)
                .FirstOrDefaultAsync(cancellationToken) ?? "—";

            // Only the team side converts, and only while the vacancy is open
            // and there is a place to offer. Saying WHY beats a missing button.
            var captainSide = grant.Grant.AccountId != accountId;
            var slot = SlotOf(grant.Opening.Slot);
            var occupied = captainSide
                ? await db.TeamMemberships.CountAsync(
                    m => m.TeamId == grant.Opening.TeamId && m.LeftAt == null && m.Slot == slot,
                    cancellationToken)
                : 0;

            var blocked =
                !captainSide ? null
                : grant.Opening.ClosedAt is not null ? "Ogłoszenie zostało zamknięte."
                : !RosterRules.CanAdd(slot, occupied) ? "Brak wolnego miejsca w składzie."
                : await db.TeamMemberships.AnyAsync(
                    m => m.TeamId == grant.Opening.TeamId && m.AccountId == grant.Grant.AccountId &&
                         m.LeftAt == null, cancellationToken)
                    ? "Ten gracz jest już w drużynie albo ma zaproszenie."
                : null;

            rows.Add(new ContactRow(
                grant.Grant.TeamOpeningId,
                grant.Opening.TeamId,
                grant.Team.Name,
                nickname,
                await Features.Recruitment.Recruitment.SharableContactAsync(db, otherId, cancellationToken),
                grant.Grant.ExpiresAt,
                grant.Opening.Slot,
                captainSide && blocked is null,
                blocked));
        }

        Contacts = rows;
    }

    /// <summary>
    /// The seven form rows as weekly windows. A row whose hours are equal is
    /// simply not a window, which is how "not that day" is expressed.
    /// </summary>
    private static List<RecruitmentRules.Window> WindowsOf(List<int> fromHours, List<int> toHours) =>
        [.. Enumerable.Range(0, 7)
            .Where(day => day < fromHours.Count && day < toHours.Count && toHours[day] > fromHours[day])
            .Select(day => new RecruitmentRules.Window(day, fromHours[day], toHours[day]))];

    /// <summary>
    /// The windows as UTC hours-of-week, or empty when the publisher gave none.
    ///
    /// Availability stays <b>optional</b>: 056 makes schedule a filter that
    /// narrows, not a hard requirement like game and roster slot, so an advert
    /// with no window must still be publishable and still be found.
    /// </summary>
    private static int[] AvailabilityOf(
        List<int> fromHours, List<int> toHours, string timeZoneId, DateTimeOffset reference) =>
        RecruitmentRules.Zone(timeZoneId) is not { } zone
            ? []
            : RecruitmentRules.ToUtcHours(WindowsOf(fromHours, toHours), zone, reference);

    /// <summary>
    /// Why the rows are unusable, or null.
    ///
    /// Needed because <see cref="AvailabilityOf"/> answers an empty array both
    /// for "none given" and for "given but nonsense" — and silently discarding
    /// what somebody just typed is the worse of the two.
    /// </summary>
    private static string? RejectAvailability(List<int> fromHours, List<int> toHours, string timeZoneId)
    {
        var any = fromHours.Zip(toHours).Any(row => row.First != row.Second);
        if (!any) return null;

        if (RecruitmentRules.Zone(timeZoneId) is null)
            return "Wybierz strefę czasową z listy.";

        foreach (var (from, to) in fromHours.Zip(toHours))
        {
            if (from == to) continue;
            if (from is < 0 or > 23 || to is < 1 or > 24 || to < from)
                return "Godzina zakończenia musi być późniejsza niż rozpoczęcia.";
        }

        return null;
    }

    private Task<bool> CanShareContactAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(
            a => a.Id == accountId && (a.DiscordId != null || a.SteamId != null), cancellationToken);

    private RedirectResult Back() => Redirect($"/{Locales.Current}/me/recruitment");
}
