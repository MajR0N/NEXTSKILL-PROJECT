using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Ratings;
using NextSkill.Domain.Recruitment;
using NextSkill.Domain.Teams;

namespace NextSkill.Api.Pages.Teams;

/// <summary>
/// A team page — the detail archetype (041), and a public, permanently
/// shareable URL: <c>/pl/teams/{id}</c> (018).
///
/// <b>Not fully grilled.</b> BUILD-PLAN lists match history, rating trend,
/// trophies and open slots as undecided. What is here is what the roster
/// mechanics need in order to be honest; the rest arrives with its own grilling.
/// </summary>
public class DetailsModel(NextSkillDb db) : PageModel
{
    public sealed record Member(Guid AccountId, string Nickname, TeamRole Role, RosterSlot Slot, bool Pending);

    /// <summary>
    /// One past result, <b>integrity-aware</b> as 049 requires: a result the
    /// system is not sure it received correctly is shown as such rather than
    /// counted quietly into a record.
    /// </summary>
    public sealed record HistoryRow(
        Guid MatchId,
        string Opponent,
        int? OwnScore,
        int? OpponentScore,
        bool Won,
        bool Forfeit,
        MatchIntegrity Integrity,
        DateTimeOffset? EndedAt);

    /// <summary>One immutable point on the rating trend (049).</summary>
    public sealed record TrendPoint(DateTimeOffset At, double Value, double Deviation);

    public Team? Team { get; private set; }
    public IReadOnlyList<Member> Roster { get; private set; } = [];
    public IReadOnlyList<Member> Invited { get; private set; } = [];
    public Rating? Cs2 { get; private set; }
    public Rating? Cs16 { get; private set; }

    /// <summary>
    /// Which game's history this page is showing (049: one shareable per-game
    /// view, because a mixed CS2/CS 1.6 record describes nobody).
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "game")]
    public Game HistoryGame { get; set; } = Game.Cs2;

    public IReadOnlyList<HistoryRow> History { get; private set; } = [];
    public IReadOnlyList<TrendPoint> Trend { get; private set; } = [];
    public int Wins { get; private set; }
    public int Losses { get; private set; }

    /// <summary>
    /// Results excluded from the record because their integrity is unresolved.
    /// Surfaced as a count rather than silently dropped — a record that quietly
    /// omits matches is a record nobody can reconcile against their own memory.
    /// </summary>
    public int UncountedResults { get; private set; }

    /// <summary>Whether the VIEWER may change the roster — drives the whole UI.</summary>
    public bool CanManage { get; private set; }
    public bool IsMember { get; private set; }
    public bool CanRestore { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool Restored { get; set; }

    /// <summary>
    /// How many places each slot has left, counting pending invites (045).
    /// Drives which options the invite form even offers.
    /// </summary>
    public int PlacesLeft(RosterSlot slot) =>
        RosterRules.Capacity(slot) - Roster.Concat(Invited).Count(m => m.Slot == slot);

    public bool AnyPlaceLeft => Enum.GetValues<RosterSlot>().Any(s => PlacesLeft(s) > 0);

    [BindProperty] public string? InviteNickname { get; set; }
    [BindProperty] public RosterSlot InviteSlot { get; set; } = RosterSlot.Player;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        await LoadAsync(id);
        // A team that does not exist and a team id someone guessed look the
        // same from outside: 404, never a distinguishing error.
        return Team is null ? NotFound() : Page();
    }

    // ── roster changes ───────────────────────────────────────────────────

    public async Task<IActionResult> OnPostInviteAsync(Guid id)
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();

        // The policy decides, not the page. "Only the captain may change the
        // roster" lives in ONE place and is exhaustively tested (036).
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: id)))
            return Forbid();

        var nickname = (InviteNickname ?? "").Trim();
        var invitee = await db.Accounts.FirstOrDefaultAsync(a =>
            a.DeletedAt == null && a.Nickname.ToLower() == nickname.ToLower());

        if (invitee is null)
        {
            // Naming an account that does not exist is not an enumeration risk
            // here: nicknames are public on every ranking and match page.
            ModelState.AddModelError(nameof(InviteNickname), "Nie ma gracza o takim nicku.");
            await LoadAsync(id);
            return Page();
        }

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id);
        if (team is null) return NotFound();
        if (team.DisbandedAt is not null) return NotFound();

        // ponytail: the cap is enforced here, not by the database. Ceiling: two
        // captains inviting at the same instant can put one slot over. Upgrade
        // path: a trigger or a serialisable transaction — neither worth it for a
        // limit whose exact edge nobody can perceive.
        //
        // Pending invites count. A cap that ignored them could not stop a full
        // roster from sending two hundred invitations (040, 045).
        var occupied = await db.TeamMemberships
            .CountAsync(m => m.TeamId == id && m.LeftAt == null && m.Slot == InviteSlot);

        if (!RosterRules.CanAdd(InviteSlot, occupied))
        {
            ModelState.AddModelError(nameof(InviteNickname),
                $"{SlotName(InviteSlot)}: brak wolnych miejsc ({RosterRules.Capacity(InviteSlot)}). " +
                "Zwolnij miejsce albo wybierz inną rolę.");
            await LoadAsync(id);
            return Page();
        }

        db.TeamMemberships.Add(new TeamMembership
        {
            Id = Guid.NewGuid(),
            TeamId = id,
            AccountId = invitee.Id,
            Role = TeamRole.Member,
            Slot = InviteSlot,
            // JoinedAt stays NULL: an invite IS a pending membership, which is
            // why it grants nothing until accepted (see Actors.CurrentAsync).
            JoinedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            AccountId = invitee.Id,
            Type = NotificationType.TeamInvite,
            // Rendering data only. Never a secret (012, 043).
            Data = JsonSerializer.SerializeToDocument(new { teamId = id, team = team.Name, tag = team.Tag, slot = InviteSlot.ToString() }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The partial unique index refuses a second ACTIVE membership for
            // the same person (008). Two captains clicking at once, or a double
            // submit, both land here instead of creating a duplicate.
            ModelState.AddModelError(nameof(InviteNickname), "Ten gracz jest już w drużynie albo ma zaproszenie.");
            await LoadAsync(id);
            return Page();
        }

        return Redirect($"/{Locales.Current}/teams/{id}");
    }

    public async Task<IActionResult> OnPostRespondAsync(Guid id, bool accept)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        // Scoped to the caller's OWN pending row, so this handler can only ever
        // answer an invite addressed to them — no authorization question left
        // to get wrong.
        var invite = await db.TeamMemberships.FirstOrDefaultAsync(m =>
            m.TeamId == id && m.AccountId == accountId && m.JoinedAt == null && m.LeftAt == null);

        if (invite is null) return NotFound();

        if (accept)
        {
            invite.JoinedAt = DateTimeOffset.UtcNow;

            // THE ONLY THING 056 COUNTS AS RECRUITMENT SUCCESS. Mutual contact
            // is a conversation start; a vacancy is filled when the player
            // agrees to fill it, which is here and not at the captain's end.
            // Closing at conversion instead would let a recruiter mark their
            // own success.
            if (invite.FromTeamOpeningId is { } openingId)
            {
                await db.TeamOpenings
                    .Where(o => o.Id == openingId && o.ClosedAt == null)
                    .ExecuteUpdateAsync(
                        u => u.SetProperty(o => o.ClosedAt, invite.JoinedAt), default);

                // Everyone still waiting on a vacancy that no longer exists is
                // closed neutrally — the same shape a material listing change
                // already uses. It is not a rejection of them.
                foreach (var state in new[] { RecruitmentState.Pending })
                {
                    await db.OpeningApplications
                        .Where(a => a.TeamOpeningId == openingId && a.State == state)
                        .ExecuteUpdateAsync(
                            u => u.SetProperty(a => a.State, RecruitmentState.OpeningClosed), default);
                    await db.TeamInterests
                        .Where(i => i.TeamOpeningId == openingId && i.State == state)
                        .ExecuteUpdateAsync(
                            u => u.SetProperty(i => i.State, RecruitmentState.OpeningClosed), default);
                }
            }

            var captains = await db.TeamMemberships
                .Where(m => m.TeamId == id && m.Role == TeamRole.Captain && m.LeftAt == null)
                .Select(m => m.AccountId)
                .ToListAsync();

            var team = await db.Teams.FirstAsync(t => t.Id == id);
            var nickname = User.Identity?.Name ?? "";

            foreach (var captain in captains)
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    AccountId = captain,
                    Type = NotificationType.TeamInviteAccepted,
                    Data = JsonSerializer.SerializeToDocument(new { teamId = id, team = team.Name, player = nickname }),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
        }
        else
        {
            // A declined invite is deleted, not tombstoned. Keeping it would
            // block a later re-invite through the same unique index, for no
            // benefit anyone can see.
            db.TeamMemberships.Remove(invite);
        }

        await db.SaveChangesAsync();
        return Redirect($"/{Locales.Current}/teams/{id}");
    }

    /// <summary>
    /// Make another accepted member a captain.
    ///
    /// <b>This is what makes leaving possible at all.</b> The last captain is
    /// refused permission to leave, and the refusal tells them to hand the
    /// captaincy over — so without this handler that message pointed at nothing
    /// and the sole captain of a team was trapped in it permanently.
    ///
    /// A team may hold several captains, so handing over is promote-then-leave
    /// rather than a bespoke atomic transfer. Two operations that already exist
    /// beat a third that only serves one story.
    /// </summary>
    public async Task<IActionResult> OnPostPromoteAsync(Guid id, Guid accountId)
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: id)))
            return Forbid();

        // JoinedAt != null: a pending invite must never be promotable, or an
        // invite nobody accepted could take control of the team.
        var membership = await db.TeamMemberships.FirstOrDefaultAsync(m =>
            m.TeamId == id && m.AccountId == accountId && m.LeftAt == null && m.JoinedAt != null);

        if (membership is null) return NotFound();

        membership.Role = TeamRole.Captain;

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = NotificationType.RosterChanged,
            Data = JsonSerializer.SerializeToDocument(new { teamId = id, role = "captain" }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return Redirect($"/{Locales.Current}/teams/{id}");
    }

    /// <summary>
    /// Move somebody between slots — bench a starter, field a substitute,
    /// make somebody a coach.
    ///
    /// This is what a bench is <i>for</i>. Without it, a team whose player is
    /// unavailable would have to remove them and re-invite the substitute, which
    /// loses the roster history and makes a one-week absence look like leaving
    /// the team.
    /// </summary>
    public async Task<IActionResult> OnPostSlotAsync(Guid id, Guid accountId, RosterSlot slot)
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: id)))
            return Forbid();

        var membership = await db.TeamMemberships.FirstOrDefaultAsync(m =>
            m.TeamId == id && m.AccountId == accountId && m.LeftAt == null);

        if (membership is null) return NotFound();
        if (membership.Slot == slot) return Redirect($"/{Locales.Current}/teams/{id}");

        // The destination cap applies to a move exactly as it does to an invite.
        // Six starters arrived at by promotion is still six starters.
        var occupied = await db.TeamMemberships
            .CountAsync(m => m.TeamId == id && m.LeftAt == null && m.Slot == slot);

        if (!RosterRules.CanAdd(slot, occupied))
        {
            ModelState.AddModelError(string.Empty,
                $"{SlotName(slot)}: brak wolnych miejsc ({RosterRules.Capacity(slot)}). Zwolnij miejsce najpierw.");
            await LoadAsync(id);
            return Page();
        }

        membership.Slot = slot;

        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = NotificationType.RosterChanged,
            Data = JsonSerializer.SerializeToDocument(new { teamId = id, slot = slot.ToString() }),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
        return Redirect($"/{Locales.Current}/teams/{id}");
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid id, Guid accountId)
    {
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: id)))
            return Forbid();

        return await LeaveAsync(id, accountId, lastCaptainMessage:
            "Nie możesz usunąć ostatniego kapitana. Najpierw przekaż kapitanat.");
    }

    public async Task<IActionResult> OnPostLeaveAsync(Guid id)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        return await LeaveAsync(id, accountId, lastCaptainMessage:
            "Jesteś jedynym kapitanem. Przekaż kapitanat, zanim odejdziesz.");
    }

    private async Task<IActionResult> LeaveAsync(Guid teamId, Guid accountId, string lastCaptainMessage)
    {
        var membership = await db.TeamMemberships.FirstOrDefaultAsync(m =>
            m.TeamId == teamId && m.AccountId == accountId && m.LeftAt == null);

        if (membership is null) return NotFound();

        // A team without a captain cannot invite, confirm a result or dispute
        // one — it is stuck and needs staff to unstick it. Refusing here is
        // cheaper than a support ticket.
        if (membership.Role == TeamRole.Captain)
        {
            var otherCaptains = await db.TeamMemberships.CountAsync(m =>
                m.TeamId == teamId && m.Role == TeamRole.Captain &&
                m.LeftAt == null && m.JoinedAt != null && m.AccountId != accountId);

            if (otherCaptains == 0)
            {
                ModelState.AddModelError(string.Empty, lastCaptainMessage);
                await LoadAsync(teamId);
                return Page();
            }
        }

        // LeftAt rather than a delete: match history has to keep saying who
        // actually played, and the partial unique index deliberately ignores
        // departed rows so the same player can rejoin later (008).
        membership.LeftAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Redirect($"/{Locales.Current}/teams/{teamId}");
    }

    // ── loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync(Guid id)
    {
        Team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (Team is null) return;

        var rows = await db.TeamMemberships
            .Where(m => m.TeamId == id && m.LeftAt == null)
            .Join(db.Accounts, m => m.AccountId, a => a.Id,
                (m, a) => new Member(a.Id, a.Nickname, m.Role, m.Slot, m.JoinedAt == null))
            .AsNoTracking()
            .ToListAsync();

        Roster = [.. rows.Where(m => !m.Pending)
                         .OrderBy(m => m.Slot)
                         .ThenByDescending(m => m.Role == TeamRole.Captain)
                         .ThenBy(m => m.Nickname)];
        Invited = [.. rows.Where(m => m.Pending).OrderBy(m => m.Nickname)];

        var ratings = await db.Ratings.AsNoTracking().Where(r => r.TeamId == id).ToListAsync();
        Cs2 = ratings.FirstOrDefault(r => r.Game == Game.Cs2);
        Cs16 = ratings.FirstOrDefault(r => r.Game == Game.Cs16);

        var actor = await Actors.CurrentAsync(db, User);
        CanManage = actor is not null
            && AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: id));

        var me = Sessions.AccountIdOf(User);
        IsMember = me is not null && Roster.Any(m => m.AccountId == me);

        CanRestore = actor is not null &&
            AccessPolicy.Can(actor, Operation.RestoreTeam, new ResourceRef(TeamId: id));

        await LoadHistoryAsync(id);
    }

    /// <summary>
    /// Competitive history and the rating trend (decision 049).
    ///
    /// <b>The trend comes from the immutable ledger</b>, never from trying to
    /// reconstruct an old rating by subtracting from the current aggregate —
    /// which is exactly why 050 made rating changes append-only. Superseded rows
    /// are excluded, so a referee reversal corrects the chart instead of leaving
    /// a phantom spike in it.
    /// </summary>
    private async Task LoadHistoryAsync(Guid id)
    {
        if (!Enum.IsDefined(HistoryGame)) HistoryGame = Game.Cs2;

        Guid[] self = [id];

        var results = await db.PublicTeamResults(self)
            .Where(m => m.Game == HistoryGame)
            .OrderByDescending(m => m.EndedAt)
            .Take(25)
            .Select(m => new
            {
                m.Id,
                m.TeamAId,
                m.TeamBId,
                m.ScoreA,
                m.ScoreB,
                m.WinnerTeamId,
                m.Integrity,
                m.EndedAt,
                Forfeit = m.ForfeitedTeamId != null,
            })
            .AsNoTracking()
            .ToListAsync();

        if (results.Count > 0)
        {
            var opponentIds = results
                .Select(m => m.TeamAId == id ? m.TeamBId!.Value : m.TeamAId!.Value)
                .Distinct()
                .ToList();

            var opponents = await db.Teams.AsNoTracking()
                .Where(t => opponentIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            History = results.Select(m =>
            {
                var isA = m.TeamAId == id;
                var opponentId = isA ? m.TeamBId!.Value : m.TeamAId!.Value;
                return new HistoryRow(
                    m.Id,
                    opponents.GetValueOrDefault(opponentId, "—"),
                    isA ? m.ScoreA : m.ScoreB,
                    isA ? m.ScoreB : m.ScoreA,
                    m.WinnerTeamId == id,
                    m.Forfeit,
                    m.Integrity,
                    m.EndedAt);
            }).ToArray();

            // Only confirmed results count toward the record. A suspect or
            // disputed one is history and is displayed, but a win-loss line that
            // silently included it would be asserting more than we know (049).
            var counted = History.Where(h => h.Integrity == MatchIntegrity.Confirmed).ToList();
            Wins = counted.Count(h => h.Won);
            Losses = counted.Count(h => !h.Won);
            UncountedResults = History.Count - counted.Count;
        }

        Trend = await db.PublicRatingMovements(self, subjectIsTeam: true, HistoryGame)
            .OrderBy(r => r.AppliedAt)
            .Select(r => new TrendPoint(r.AppliedAt, r.ValueAfter!.Value, r.DeviationAfter!.Value))
            .AsNoTracking()
            .ToListAsync();
    }

    /// <summary>Same generated monogram as the ranking page, so a team looks identical everywhere (019).</summary>
    public static string Initials(string name) => string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public static int Palette(string tag) => Math.Abs(tag.GetHashCode(StringComparison.Ordinal)) % 5 + 1;

    public static string SlotName(RosterSlot slot) => slot switch
    {
        RosterSlot.Player => "Skład podstawowy",
        RosterSlot.Substitute => "Rezerwa",
        RosterSlot.Coach => "Sztab",
        _ => slot.ToString(),
    };

    public static bool IsProvisional(Rating r) => new Glicko2Rating(r.Value, r.Deviation, r.Volatility).IsProvisional;
}
