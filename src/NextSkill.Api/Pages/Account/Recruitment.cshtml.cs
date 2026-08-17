using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Recruitment;

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

    public sealed record ContactRow(Guid OpeningId, string Team, string Who, string? Contact, DateTimeOffset ExpiresAt);

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
        var reason = RecruitmentRules.RejectPublication(
            Listing.Roles, Listing.Slots, Listing.Languages, Listing.Regions, [], note);

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

        var now = clock.GetUtcNow();
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
                Note = note,
                Cycle = 1,
                PublishedAt = now,
                ExpiresAt = now + RecruitmentRules.AdvertLifetime,
                CreatedAt = now,
            });
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
                existing.AvailabilityHours, []);

            existing.Roles = [.. Listing.Roles];
            existing.Slots = [.. Listing.Slots];
            existing.Languages = [.. Listing.Languages];
            existing.Regions = [.. Listing.Regions];
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
        var reason = RecruitmentRules.RejectPublication(
            Opening.Roles, [Opening.Slot.ToString().ToLowerInvariant()],
            Opening.Languages, Opening.Regions, [], note);

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

        var now = clock.GetUtcNow();
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
            .Join(db.Teams, x => x.Opening.TeamId, t => t.Id, (x, t) => new { x.Grant, Team = t })
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

            rows.Add(new ContactRow(
                grant.Grant.TeamOpeningId,
                grant.Team.Name,
                nickname,
                await Features.Recruitment.Recruitment.SharableContactAsync(db, otherId, cancellationToken),
                grant.Grant.ExpiresAt));
        }

        Contacts = rows;
    }

    private Task<bool> CanShareContactAsync(Guid accountId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(
            a => a.Id == accountId && (a.DiscordId != null || a.SteamId != null), cancellationToken);

    private RedirectResult Back() => Redirect($"/{Locales.Current}/me/recruitment");
}
