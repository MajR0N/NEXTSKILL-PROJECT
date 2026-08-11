using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Ratings;
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
    public sealed record Member(Guid AccountId, string Nickname, TeamRole Role, bool Pending);

    public Team? Team { get; private set; }
    public IReadOnlyList<Member> Roster { get; private set; } = [];
    public IReadOnlyList<Member> Invited { get; private set; } = [];
    public Rating? Cs2 { get; private set; }
    public Rating? Cs16 { get; private set; }

    /// <summary>Whether the VIEWER may change the roster — drives the whole UI.</summary>
    public bool CanManage { get; private set; }
    public bool IsMember { get; private set; }

    /// <summary>Places left, counting pending invites (045).</summary>
    public int PlacesLeft => RosterRules.MaxActiveMembers - Roster.Count - Invited.Count;

    [BindProperty] public string? InviteNickname { get; set; }

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
        var invitee = await db.Accounts.FirstOrDefaultAsync(a => a.Nickname.ToLower() == nickname.ToLower());

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
        // captains inviting at the same instant can put the roster one over.
        // Upgrade path: a trigger or a serialisable transaction — neither worth
        // it for a limit whose exact edge nobody can perceive.
        var active = await db.TeamMemberships.CountAsync(m => m.TeamId == id && m.LeftAt == null && m.JoinedAt != null);
        var pending = await db.TeamMemberships.CountAsync(m => m.TeamId == id && m.LeftAt == null && m.JoinedAt == null);

        if (!RosterRules.CanInvite(active, pending))
        {
            ModelState.AddModelError(nameof(InviteNickname),
                $"Skład jest pełny — maksymalnie {RosterRules.MaxActiveMembers} osób razem z zaproszeniami. " +
                "Usuń kogoś albo cofnij zaproszenie.");
            await LoadAsync(id);
            return Page();
        }

        db.TeamMemberships.Add(new TeamMembership
        {
            Id = Guid.NewGuid(),
            TeamId = id,
            AccountId = invitee.Id,
            Role = TeamRole.Member,
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
            Data = JsonSerializer.SerializeToDocument(new { teamId = id, team = team.Name, tag = team.Tag }),
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
                (m, a) => new Member(a.Id, a.Nickname, m.Role, m.JoinedAt == null))
            .AsNoTracking()
            .ToListAsync();

        Roster = [.. rows.Where(m => !m.Pending).OrderByDescending(m => m.Role == TeamRole.Captain).ThenBy(m => m.Nickname)];
        Invited = [.. rows.Where(m => m.Pending).OrderBy(m => m.Nickname)];

        var ratings = await db.Ratings.AsNoTracking().Where(r => r.TeamId == id).ToListAsync();
        Cs2 = ratings.FirstOrDefault(r => r.Game == Game.Cs2);
        Cs16 = ratings.FirstOrDefault(r => r.Game == Game.Cs16);

        var actor = await Actors.CurrentAsync(db, User);
        CanManage = actor is not null
            && AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: id));

        var me = Sessions.AccountIdOf(User);
        IsMember = me is not null && Roster.Any(m => m.AccountId == me);
    }

    /// <summary>Same generated monogram as the ranking page, so a team looks identical everywhere (019).</summary>
    public static string Initials(string name) => string.Concat(
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    public static int Palette(string tag) => Math.Abs(tag.GetHashCode(StringComparison.Ordinal)) % 5 + 1;

    public static bool IsProvisional(Rating r) => new Glicko2Rating(r.Value, r.Deviation, r.Volatility).IsProvisional;
}
