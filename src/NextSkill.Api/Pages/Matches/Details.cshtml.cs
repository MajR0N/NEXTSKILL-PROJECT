using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;
using NextSkill.Domain.Veto;

namespace NextSkill.Api.Pages.Matches;

/// <summary>
/// The durable, server-rendered match lobby. Live transport will enhance this
/// page later; a plain fetch remains the complete state interface (028, 051).
/// </summary>
public class DetailsModel(NextSkillDb db, TenantContext tenant, TimeProvider clock) : PageModel
{
    public sealed class VetoInput
    {
        public Guid TeamId { get; set; }
        public string Map { get; set; } = "";
        public VetoStartingSide? Side { get; set; }
    }

    /// <summary>A captain acting on a received result (decision 050 §5).</summary>
    public sealed class ResultInput
    {
        public Guid TeamId { get; set; }
        public string Reason { get; set; } = "";
    }

    public sealed record LobbyTeam(Guid Id, string Name, string Tag);
    public sealed record VetoActionRow(
        VetoActionKind Kind, string Map, string Team, VetoStartingSide? Side, bool Automatic);
    public sealed record ParticipantRow(string Nickname, string Team, RosterSlot Slot, bool Bound);

    [BindProperty]
    public VetoInput Input { get; set; } = new();

    [BindProperty]
    public ResultInput Result { get; set; } = new();

    public Match? Match { get; private set; }
    public Tournament? Tournament { get; private set; }
    public LobbyTeam? TeamA { get; private set; }
    public LobbyTeam? TeamB { get; private set; }
    public bool TeamAReady { get; private set; }
    public bool TeamBReady { get; private set; }
    public bool VetoStarted { get; private set; }
    public bool VetoCompleted { get; private set; }
    public Guid? ReadyTeamId { get; private set; }
    public Guid? CurrentTurnTeamId { get; private set; }
    public string? CurrentTurnTeamName { get; private set; }
    public VetoActionKind? CurrentTurnKind { get; private set; }
    public IReadOnlyList<string> LegalMaps { get; private set; } = [];
    public IReadOnlyList<VetoStartingSide> LegalSides { get; private set; } = [];
    public IReadOnlyList<VetoActionRow> VetoActions { get; private set; } = [];
    public bool MayTakeTurn { get; private set; }

    // ── the result, once one has arrived (decision 050) ──────────────────
    public bool HasResult { get; private set; }
    public bool Rated { get; private set; }
    public bool Disputed { get; private set; }
    public string? DisputeReason { get; private set; }
    public string? WinnerName { get; private set; }
    public bool TeamAConfirmed { get; private set; }
    public bool TeamBConfirmed { get; private set; }

    /// <summary>The team this visitor captains, when the result is still open to them.</summary>
    public Guid? MayActOnResultFor { get; private set; }

    public IReadOnlyList<ParticipantRow> Participants { get; private set; } = [];

    /// <summary>
    /// This visitor's own link code, and nobody else's.
    ///
    /// Factor 2 of 011's binding is a secret belonging to one person. It is
    /// selected by account id, disappears the moment the bind succeeds, and the
    /// response carries <c>no-store</c> so it never sits in a shared cache —
    /// the same handling the <c>setinfo</c> rotation already uses.
    /// </summary>
    public string? MyLinkCode { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        await ResolveExpiredTurnAsync(id, cancellationToken);

        Match = await db.FindPublicMatchAsync(id);
        if (Match is null || Match.TeamAId is not { } teamAId || Match.TeamBId is not { } teamBId)
            return NotFound();

        Tournament = await db.PublicTournaments()
            .AsNoTracking()
            .FirstAsync(t => t.Id == Match.TournamentId, cancellationToken);

        var teams = await db.Teams.AsNoTracking()
            .Where(t => t.Id == teamAId || t.Id == teamBId)
            .Select(t => new LobbyTeam(t.Id, t.Name, t.Tag))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        if (!teams.TryGetValue(teamAId, out var teamA) || !teams.TryGetValue(teamBId, out var teamB))
            return NotFound();

        TeamA = teamA;
        TeamB = teamB;

        var veto = await db.FindPublicMatchVetoAsync(id);
        TeamAReady = veto?.TeamAReadyAt is not null;
        TeamBReady = veto?.TeamBReadyAt is not null;
        VetoStarted = veto?.TurnStartedAt is not null && veto.CompletedAt is null;
        VetoCompleted = veto?.CompletedAt is not null;

        var actionEntities = veto is null
            ? []
            : await db.PublicMatchVetoActions(id)
                .OrderBy(a => a.Sequence)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        var actions = actionEntities.Select(ToDomainAction).ToArray();
        VetoActions = actionEntities.Select(a => new VetoActionRow(
            a.Kind, a.Map, a.TeamId == teamAId ? teamA.Name : teamB.Name, a.Side, a.Automatic)).ToArray();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        ReadyTeamId = new[] { teamAId, teamBId }
            .Where(teamId => actor is not null &&
                AccessPolicy.Can(actor, Operation.ActInVeto, new ResourceRef(TeamId: teamId)))
            .Cast<Guid?>()
            .FirstOrDefault();

        if (VetoStarted)
        {
            var turn = VetoRules.Next(veto!.BestOf, teamAId, teamBId, veto.MapPool, actions);
            if (turn is not null)
            {
                CurrentTurnTeamId = turn.TeamId;
                CurrentTurnTeamName = turn.TeamId == teamAId ? teamA.Name : teamB.Name;
                CurrentTurnKind = turn.Kind;
                LegalMaps = turn.LegalMaps;
                LegalSides = turn.Kind == VetoActionKind.ChooseSide
                    ? [VetoStartingSide.Ct, VetoStartingSide.T]
                    : [];
                MayTakeTurn = actor is not null &&
                    AccessPolicy.Can(actor, Operation.ActInVeto, new ResourceRef(TeamId: turn.TeamId));
            }
        }

        await LoadResultAsync(Match, teamA, teamB, actor, cancellationToken);
        return Page();
    }

    private async Task LoadResultAsync(
        Match match, LobbyTeam teamA, LobbyTeam teamB, Actor? actor, CancellationToken cancellationToken)
    {
        HasResult = match.EndedAt is not null;
        Rated = match.RatedAt is not null;
        Disputed = match.DisputedAt is not null;
        DisputeReason = match.DisputeReason;
        TeamAConfirmed = match.TeamAConfirmedAt is not null;
        TeamBConfirmed = match.TeamBConfirmedAt is not null;
        WinnerName = match.WinnerTeamId is { } winner
            ? winner == teamA.Id ? teamA.Name : teamB.Name
            : null;

        // Confirming and disputing are the corroboration anti-match-fixing rests
        // on (007), so they belong to a captain of a team that actually played —
        // and only while the rating is still reversible. Once the sweeper has
        // settled it, un-doing it is a referee's job, not a captain's (050 §5).
        if (HasResult && !Rated && !Disputed && actor is not null)
        {
            MayActOnResultFor = new[] { teamA.Id, teamB.Id }
                .Where(teamId => AccessPolicy.Can(
                    actor, Operation.ConfirmResult, new ResourceRef(TeamId: teamId)))
                .Cast<Guid?>()
                .FirstOrDefault();
        }

        // Fetched, then ordered in memory. A lineup is at most ten rows, and
        // "the home team first" is not a SQL sort — expressed as one, it becomes
        // an ORDER BY over a constructed record that EF cannot translate at all.
        var participants = (await db.PublicMatchParticipants(match.Id).ToListAsync(cancellationToken))
            .OrderBy(p => p.TeamId == teamA.Id ? 0 : 1)
            .ThenBy(p => p.Slot)
            .ThenBy(p => p.CreatedAt)
            .ToList();

        if (participants.Count == 0) return;

        var nicknames = await db.Accounts
            .Where(a => participants.Select(p => p.AccountId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Nickname, cancellationToken);

        Participants = participants
            .Select(p => new ParticipantRow(
                nicknames.GetValueOrDefault(p.AccountId, "—"),
                p.TeamId == teamA.Id ? teamA.Name : teamB.Name,
                p.Slot,
                p.Provenance == ParticipantProvenance.Bound))
            .ToArray();

        if (actor is null) return;

        // Scoped to the caller's OWN account id, which is what makes the escape
        // hatch safe here — the same reasoning Actors.cs uses. It can only ever
        // return the code belonging to the person asking.
        MyLinkCode = await db.MatchParticipants
            .IgnoreQueryFilters()
            .Where(p => p.MatchId == match.Id &&
                        p.AccountId == actor.AccountId &&
                        p.Provenance == ParticipantProvenance.Declared)
            .Select(p => p.LinkCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (MyLinkCode is not null)
            Response.Headers.CacheControl = "no-store";
    }

    /// <summary>
    /// A captain agrees the score, which closes the challenge window early when
    /// both do (050 §5). It touches the rating and never the bracket.
    /// </summary>
    public async Task<IActionResult> OnPostConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        return await ActOnResultAsync(id, Operation.ConfirmResult, cancellationToken,
            (match, teamAId, _, now) =>
            {
                if (Result.TeamId == teamAId) match.TeamAConfirmedAt ??= now;
                else match.TeamBConfirmedAt ??= now;
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// A captain claims the result is illegitimate.
    ///
    /// It pauses the rating sweep and alerts the organiser's staff. It
    /// deliberately does <b>not</b> block bracket advancement: that would hand a
    /// losing captain a one-click lever to stall a Saturday-night event
    /// (040 §9, 050 §5).
    /// </summary>
    public async Task<IActionResult> OnPostDisputeAsync(Guid id, CancellationToken cancellationToken)
    {
        var reason = Result.Reason.Trim();
        if (reason.Length is 0 or > 500)
            return BadRequest();

        return await ActOnResultAsync(id, Operation.DisputeResult, cancellationToken,
            async (match, _, _, now) =>
            {
                match.DisputedAt = now;
                match.DisputedByAccountId = Sessions.AccountIdOf(User);
                match.DisputeReason = reason;

                // The organiser panel is a later slice, so the dispute is parked
                // until a referee has somewhere to stand. Held is recoverable —
                // the ledger is built to be recomputed — and these rows are what
                // make it visible the moment the bell ships (043).
                var staff = await db.OrganiserStaff.IgnoreQueryFilters()
                    .Where(s => s.OrganiserId == match.OrganiserId)
                    .Select(s => s.AccountId)
                    .ToListAsync(cancellationToken);

                foreach (var accountId in staff)
                {
                    db.Notifications.Add(new Notification
                    {
                        Id = Guid.NewGuid(),
                        AccountId = accountId,
                        Type = NotificationType.ResultDisputed,
                        // Rendering data only. Never the reason text, which is
                        // user input, and never a link code (012).
                        Data = System.Text.Json.JsonDocument.Parse(
                            $$"""{"matchId":"{{match.Id}}"}"""),
                        CreatedAt = now,
                    });
                }
            });
    }

    private async Task<IActionResult> ActOnResultAsync(
        Guid id,
        Operation operation,
        CancellationToken cancellationToken,
        Func<Match, Guid, Guid, DateTimeOffset, Task> act)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var match = await LockMatchAsync(id, cancellationToken);
        if (match is null || match.TeamAId is not { } teamAId || match.TeamBId is not { } teamBId)
            return NotFound();

        // Nothing to agree with yet, or nothing left to agree about: the sweeper
        // has already settled it, and a rating already applied is a referee's
        // problem rather than a captain's.
        if (match.EndedAt is null || match.RatedAt is not null || match.DisputedAt is not null)
            return StatusCode(StatusCodes.Status409Conflict);

        if ((Result.TeamId != teamAId && Result.TeamId != teamBId) ||
            !AccessPolicy.Can(actor, operation, new ResourceRef(TeamId: Result.TeamId)))
            return Forbid();

        tenant.Set(match.OrganiserId);
        await act(match, teamAId, teamBId, clock.GetUtcNow());

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RedirectToLobby(id);
    }

    public async Task<IActionResult> OnPostReadyAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var match = await LockMatchAsync(id, cancellationToken);
        if (match is null || match.TeamAId is not { } teamAId || match.TeamBId is not { } teamBId)
            return NotFound();

        var tournamentIsRunning = await db.Tournaments.IgnoreQueryFilters()
            .AnyAsync(t => t.Id == match.TournamentId && t.Status == TournamentStatus.Running, cancellationToken);
        if (!tournamentIsRunning || match.Status is not (MatchStatus.Scheduled or MatchStatus.VetoInProgress))
            return NotFound();

        if (!MayActForParticipant(actor, teamAId, teamBId, Input.TeamId)) return Forbid();

        tenant.Set(match.OrganiserId);
        var veto = await db.MatchVetos.SingleOrDefaultAsync(v => v.MatchId == id, cancellationToken);
        if (veto is null)
        {
            veto = new MatchVeto
            {
                Id = Guid.NewGuid(),
                OrganiserId = match.OrganiserId,
                MatchId = match.Id,
                BestOf = 1,
                MapPool = VetoMapPools.For(match.Game).ToArray(),
                CreatedAt = clock.GetUtcNow(),
            };
            db.MatchVetos.Add(veto);
        }

        var now = clock.GetUtcNow();
        if (Input.TeamId == teamAId) veto.TeamAReadyAt ??= now;
        if (Input.TeamId == teamBId) veto.TeamBReadyAt ??= now;

        if (veto.TeamAReadyAt is not null && veto.TeamBReadyAt is not null && veto.TurnStartedAt is null)
        {
            veto.TurnStartedAt = now;
            match.Status = MatchStatus.VetoInProgress;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RedirectToLobby(id);
    }

    public async Task<IActionResult> OnPostVetoAsync(Guid id, CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var match = await LockMatchAsync(id, cancellationToken);
        if (match is null || match.TeamAId is not { } teamAId || match.TeamBId is not { } teamBId)
            return NotFound();
        if (!MayActForParticipant(actor, teamAId, teamBId, Input.TeamId)) return Forbid();

        tenant.Set(match.OrganiserId);
        var veto = await db.MatchVetos.SingleOrDefaultAsync(v => v.MatchId == id, cancellationToken);
        if (veto?.TurnStartedAt is null || veto.CompletedAt is not null || match.Status != MatchStatus.VetoInProgress)
            return NotFound();

        var actionEntities = await db.MatchVetoActions
            .Where(a => a.MatchVetoId == veto.Id)
            .OrderBy(a => a.Sequence)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var turn = VetoRules.Next(
            veto.BestOf, teamAId, teamBId, veto.MapPool,
            actionEntities.Select(ToDomainAction).ToArray());

        if (turn is null || Input.TeamId != turn.TeamId)
            return StatusCode(StatusCodes.Status409Conflict);

        VetoAction action;
        try
        {
            action = turn.Kind == VetoActionKind.ChooseSide && Input.Side is { } side
                ? VetoRules.ChooseSide(turn, Input.TeamId, side)
                : VetoRules.Apply(turn, Input.TeamId, Input.Map);
        }
        catch (InvalidOperationException) { return StatusCode(StatusCodes.Status409Conflict); }

        var now = clock.GetUtcNow();
        db.MatchVetoActions.Add(new MatchVetoAction
        {
            Id = Guid.NewGuid(),
            OrganiserId = match.OrganiserId,
            MatchVetoId = veto.Id,
            Sequence = action.Sequence,
            TeamId = action.TeamId,
            Kind = action.Kind,
            Map = action.Map,
            Side = action.Side,
            Automatic = action.Automatic,
            CreatedAt = now,
        });
        veto.CurrentStep = action.Sequence + 1;
        veto.TurnStartedAt = now;

        var allActions = actionEntities.Select(ToDomainAction).Append(action).ToArray();
        CompleteIfTerminal(match, veto, teamAId, teamBId, allActions, now);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RedirectToLobby(id);
    }

    private Task<Match?> LockMatchAsync(Guid id, CancellationToken cancellationToken) =>
        db.Matches
            .FromSqlInterpolated($"SELECT * FROM matches WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);

    private async Task ResolveExpiredTurnAsync(Guid id, CancellationToken cancellationToken)
    {
        // The common read path takes no lock. Only an apparently overdue turn
        // enters the serialised write path; the timestamp is checked again
        // under the match lock before anything is appended.
        var publicVeto = await db.FindPublicMatchVetoAsync(id);
        var observedNow = clock.GetUtcNow();
        if (publicVeto?.TurnStartedAt is not { } observedStart || publicVeto.CompletedAt is not null ||
            observedNow < observedStart + VetoRules.TurnDuration)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var match = await LockMatchAsync(id, cancellationToken);
        if (match is null || match.TeamAId is not { } teamAId || match.TeamBId is not { } teamBId)
            return;

        tenant.Set(match.OrganiserId);
        var veto = await db.MatchVetos.SingleOrDefaultAsync(v => v.MatchId == id, cancellationToken);
        var now = clock.GetUtcNow();
        if (veto?.TurnStartedAt is not { } started || veto.CompletedAt is not null ||
            now < started + VetoRules.TurnDuration)
            return;

        var actionEntities = await db.MatchVetoActions
            .Where(a => a.MatchVetoId == veto.Id)
            .OrderBy(a => a.Sequence)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var turn = VetoRules.Next(
            veto.BestOf, teamAId, teamBId, veto.MapPool,
            actionEntities.Select(ToDomainAction).ToArray());
        if (turn is null)
            return;

        var action = turn.Kind == VetoActionKind.ChooseSide
            ? VetoRules.ChooseSide(
                turn, turn.TeamId,
                RandomNumberGenerator.GetInt32(2) == 0 ? VetoStartingSide.Ct : VetoStartingSide.T,
                automatic: true)
            : VetoRules.Apply(
                turn, turn.TeamId,
                turn.LegalMaps[RandomNumberGenerator.GetInt32(turn.LegalMaps.Count)],
                automatic: true);
        db.MatchVetoActions.Add(new MatchVetoAction
        {
            Id = Guid.NewGuid(),
            OrganiserId = match.OrganiserId,
            MatchVetoId = veto.Id,
            Sequence = action.Sequence,
            TeamId = action.TeamId,
            Kind = action.Kind,
            Map = action.Map,
            Side = action.Side,
            Automatic = true,
            CreatedAt = now,
        });
        veto.CurrentStep = action.Sequence + 1;
        veto.TurnStartedAt = now;
        var allActions = actionEntities.Select(ToDomainAction).Append(action).ToArray();
        CompleteIfTerminal(match, veto, teamAId, teamBId, allActions, now);

        // Decision 051: ordinary reads lazily make exactly one overdue move.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void CompleteIfTerminal(
        Match match,
        MatchVeto veto,
        Guid teamAId,
        Guid teamBId,
        IReadOnlyList<VetoAction> actions,
        DateTimeOffset now)
    {
        if (VetoRules.Next(veto.BestOf, teamAId, teamBId, veto.MapPool, actions) is not null)
            return;

        var deciderSide = RandomNumberGenerator.GetInt32(2) == 0
            ? VetoStartingSide.Ct
            : VetoStartingSide.T;
        var maps = VetoRules.Decide(
            veto.BestOf, teamAId, teamBId, veto.MapPool, actions, deciderSide);
        foreach (var (decision, order) in maps.Select((decision, order) => (decision, order)))
        {
            db.MatchMaps.Add(new MatchMap
            {
                Id = Guid.NewGuid(),
                OrganiserId = match.OrganiserId,
                MatchId = match.Id,
                Order = order,
                Map = decision.Map,
                TeamAStartsAs = decision.TeamAStartsAs,
            });
        }

        match.Map = maps[0].Map;
        match.Status = MatchStatus.AwaitingPlayers;
        veto.CompletedAt = now;
        veto.TurnStartedAt = null;
    }

    private static bool MayActForParticipant(Actor actor, Guid teamAId, Guid teamBId, Guid teamId) =>
        (teamId == teamAId || teamId == teamBId) &&
        AccessPolicy.Can(actor, Operation.ActInVeto, new ResourceRef(TeamId: teamId));

    private RedirectResult RedirectToLobby(Guid id) => Redirect($"/{Locales.Current}/m/{id}");

    private static VetoAction ToDomainAction(MatchVetoAction action) => new(
        action.Sequence, action.TeamId, action.Kind, action.Map, action.Side, action.Automatic);
}
