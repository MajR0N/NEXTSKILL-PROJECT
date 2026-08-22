using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Matches;

namespace NextSkill.Api.Pages.Org;

/// <summary>
/// The referee's desk (decisions 015, 036, 050 §5).
///
/// [050](050-match-results-and-ratings.md) gave captains a dispute button and
/// gave nobody the ability to answer it — a rating could be parked by one click
/// with no surface anywhere in the product to un-park it. This is that surface,
/// and it is why the audit log had to exist first.
/// </summary>
public class DisputesModel(NextSkillDb db, TenantContext tenant, TimeProvider clock) : PageModel
{
    public sealed class DecisionInput
    {
        public Guid MatchId { get; set; }

        /// <summary>The corrected winner. Empty means "nobody won" — a void result.</summary>
        public Guid? WinnerTeamId { get; set; }

        public string Reason { get; set; } = "";
    }

    public sealed record DisputeRow(
        Guid MatchId,
        string Tournament,
        string TeamA,
        string TeamB,
        Guid TeamAId,
        Guid TeamBId,
        int? ScoreA,
        int? ScoreB,
        Guid? WinnerTeamId,
        MatchIntegrity Integrity,
        bool CaptainDisputed,
        string? DisputeReason);

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    public IReadOnlyList<DisputeRow> Disputes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var refereeable = await RefereeableOrganisersAsync(actor, cancellationToken);
        if (refereeable.Count == 0) return Forbid();

        var matches = await db.DisputesForManagement(refereeable)
            .AsNoTracking()
            .OrderBy(m => m.EndedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0) return Page();

        var teamIds = matches.SelectMany(m => new[] { m.TeamAId, m.TeamBId })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        var teams = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var tournaments = await db.Tournaments.IgnoreQueryFilters().AsNoTracking()
            .Where(t => matches.Select(m => m.TournamentId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        Disputes = matches.Select(m => new DisputeRow(
            m.Id,
            tournaments.GetValueOrDefault(m.TournamentId, "—"),
            m.TeamAId is { } a ? teams.GetValueOrDefault(a, "—") : "—",
            m.TeamBId is { } b ? teams.GetValueOrDefault(b, "—") : "—",
            m.TeamAId ?? Guid.Empty,
            m.TeamBId ?? Guid.Empty,
            m.ScoreA,
            m.ScoreB,
            m.WinnerTeamId,
            m.Integrity,
            m.DisputedAt is not null,
            m.DisputeReason)).ToArray();

        return Page();
    }

    /// <summary>The claim was heard and the result stands.</summary>
    public Task<IActionResult> OnPostDismissAsync(CancellationToken cancellationToken) =>
        DecideAsync(AuditAction.ResolveDispute, cancellationToken, (match, _) =>
        {
            DisputeResolution.Dismiss(match);
            return Task.CompletedTask;
        });

    /// <summary>The result was wrong. Corrects it and rebuilds the ratings.</summary>
    public Task<IActionResult> OnPostReverseAsync(CancellationToken cancellationToken) =>
        DecideAsync(AuditAction.ReverseResult, cancellationToken, (match, now) =>
            DisputeResolution.ReverseAsync(db, match, Input.WinnerTeamId, now, cancellationToken));

    private async Task<IActionResult> DecideAsync(
        AuditAction action,
        CancellationToken cancellationToken,
        Func<Match, DateTimeOffset, Task> decide)
    {
        var reason = Input.Reason.Trim();
        if (reason.Length is 0 or > 500) return BadRequest();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var match = await db.Matches
            .FromSqlInterpolated($"SELECT * FROM matches WHERE id = {Input.MatchId} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (match is null) return NotFound();

        // Asked as an OPERATION, never as a role (036). A referee reaches this
        // and an organiser admin who runs the structure does too, but the page
        // never has to know which of them is standing here.
        if (!AccessPolicy.Can(actor, Operation.ResolveDispute, new ResourceRef(match.OrganiserId)))
        {
            Audit.Record(
                db, actor.AccountId, action, clock.GetUtcNow(), AuditOutcome.Denied,
                organiserId: match.OrganiserId, resourceId: match.Id,
                retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Forbid();
        }

        tenant.Set(match.OrganiserId);
        var now = clock.GetUtcNow();

        // Audited BEFORE the effect, in the same transaction. 036 lists
        // force-advancing a bracket and overriding a score among the actions
        // that must never be silent, and a referee decision without a name
        // against it is indistinguishable from a bug.
        Audit.Record(
            db, actor.AccountId, action, now,
            organiserId: match.OrganiserId, resourceId: match.Id, reason: reason);

        await decide(match, now);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/org/disputes");
    }

    /// <summary>
    /// Every organiser this actor may referee in.
    ///
    /// <b>It used to read only the actor's OWN organiser roles</b>, which meant
    /// platform staff — whom [036](036-authorization.md) grants
    /// <see cref="Operation.ResolveDispute"/> precisely so they can settle
    /// <i>cross-organiser</i> disputes — were refused the page outright. The
    /// policy had always answered correctly; the page never asked it about
    /// anybody else's organiser.
    ///
    /// The candidate set is still reduced to concrete ids and then filtered by
    /// the policy, so the widening is exactly what 036 sanctions and no wider:
    /// an organiser's referee still sees only their own events.
    ///
    /// ponytail: a platform role loads every organiser id. Ceiling: one row per
    /// organiser on the platform, which is a number that stays small for a long
    /// time. Upgrade path: restrict the candidates to organisers that currently
    /// HAVE a contested match — the same query the page runs next anyway.
    /// </summary>
    private async Task<List<Guid>> RefereeableOrganisersAsync(Actor actor, CancellationToken ct)
    {
        var mine = (actor.Organisers ?? new Dictionary<Guid, OrganiserRole>()).Keys.ToHashSet();

        var candidates = actor.Platform is null
            ? mine.ToList()
            : await db.Organisers.AsNoTracking().Select(o => o.Id).ToListAsync(ct);

        return [.. candidates
            .Where(id => AccessPolicy.Can(actor, Operation.ResolveDispute, new ResourceRef(id)))];
    }
}
