using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Discord;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Features.Tournaments;

/// <summary>
/// Writes a generated bracket into <c>matches</c> (decisions 023, 046).
///
/// <b>The one job here is not to be clever.</b> The generator is pure and
/// exhaustively tested; this translates its output and nothing more. Every
/// temptation to recompute routing, resolve byes differently, or "fix up" a
/// position is a chance to reintroduce the Phase 2 bug, where the wrong round's
/// losers were routed and the bracket still looked completely normal.
/// </summary>
public static class BracketBuilder
{
    /// <summary>
    /// Generates and persists the whole bracket in <b>one transaction</b>, then
    /// resolves byes.
    ///
    /// One transaction because a half-written bracket is worse than none: it
    /// renders, it looks plausible, and it routes teams into matches that do not
    /// exist (008).
    /// </summary>
    public static async Task BuildAsync(NextSkillDb db, Tournament tournament, CancellationToken ct = default)
    {
        var seeds = await db.Registrations
            .Where(r => r.TournamentId == tournament.Id && r.WithdrawnAt == null && r.Seed != null)
            .ToDictionaryAsync(r => r.Seed!.Value, r => r.TeamId, ct);

        if (seeds.Count < 2)
            throw new InvalidOperationException("A bracket needs at least two seeded teams.");

        var bracket = tournament.Format switch
        {
            TournamentFormat.SingleElimination => BracketGenerator.GenerateSingleElimination(seeds.Count),
            TournamentFormat.DoubleElimination => BracketGenerator.GenerateDoubleElimination(seeds.Count),

            // Fail closed. 022 committed to five formats and built two; a third
            // arriving here must stop rather than silently produce a single
            // elimination bracket somebody then plays.
            _ => throw new NotSupportedException(
                $"{tournament.Format} has no generator yet (022). Registration should not have allowed it."),
        };

        // Guids minted BEFORE anything is written, so routing can be stored as a
        // real foreign key instead of a number the application resolves later.
        var ids = bracket.Matches.ToDictionary(m => m.Id, _ => Guid.NewGuid());

        var fedSlots = bracket.Matches
            .SelectMany(m => new[]
            {
                m.WinnerToMatch is { } winner && m.WinnerToSlot is { } winnerSlot
                    ? (Match: winner, Slot: winnerSlot)
                    : default,
                m.LoserToMatch is { } loser && m.LoserToSlot is { } loserSlot
                    ? (Match: loser, Slot: loserSlot)
                    : default,
            })
            .Where(x => x != default)
            .ToHashSet();

        Guid? TeamOf(Slot slot) => slot.Seed is { } seed && seeds.TryGetValue(seed, out var team) ? team : null;

        var rows = bracket.Matches.Select(m => new Match
        {
            Id = ids[m.Id],
            OrganiserId = tournament.OrganiserId,
            TournamentId = tournament.Id,
            Game = tournament.Game,

            Side = m.Side,
            Round = m.Round,
            Position = m.Position,

            // Seeded slots resolve to real teams now; slots fed by another match
            // stay null until that match is decided.
            TeamAId = TeamOf(m.A),
            TeamBId = TeamOf(m.B),

            WinnerToMatchId = m.WinnerToMatch is { } w ? ids[w] : null,
            WinnerToSlot = m.WinnerToSlot,
            LoserToMatchId = m.LoserToMatch is { } l ? ids[l] : null,
            LoserToSlot = m.LoserToSlot,

            IsConditional = m.IsConditional,
            IsBye = m.IsBye,

            // Null is ambiguous unless resolution is stored separately. A
            // first-round phantom seed is known-empty; a later-round null is
            // still waiting for an upstream result.
            SlotAResolved = !m.IsConditional && !fedSlots.Contains((m.Id, SlotRef.A)),
            SlotBResolved = !m.IsConditional && !fedSlots.Contains((m.Id, SlotRef.B)),

            Status = MatchStatus.Scheduled,

            // Nothing has been reported yet, so there is nothing to doubt. The
            // gate that matters applies when a RESULT arrives (035).
            Integrity = MatchIntegrity.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();

        db.Matches.AddRange(rows);
        await db.SaveChangesAsync(ct);

        // Byes last, and only after the rows exist: advancing writes into the
        // match a bye routes into, which has to be there first.
        await ResolveByesAsync(db, tournament.Id, ct);
    }

    /// <summary>
    /// Plays out every bye.
    ///
    /// A bye is a walkover: the occupying team advances immediately, the match
    /// is completed with no score, and <b>it is never rated</b> — a free win
    /// must not move a rating (022, 025).
    ///
    /// Looped, because a bye can feed a match that is itself a bye in a very
    /// lopsided bracket. The guard makes a routing cycle an error instead of a
    /// hang.
    /// </summary>
    private static async Task ResolveByesAsync(NextSkillDb db, Guid tournamentId, CancellationToken ct)
    {
        for (var pass = 0; ; pass++)
        {
            if (pass > 64) throw new InvalidOperationException("Bye resolution did not terminate — routing is probably cyclic.");

            var bye = await db.Matches.FirstOrDefaultAsync(m =>
                m.TournamentId == tournamentId && m.IsBye && m.WinnerTeamId == null &&
                (m.TeamAId != null || m.TeamBId != null), ct);

            if (bye is null) return;

            await AdvanceAsync(db, bye, bye.TeamAId ?? bye.TeamBId!.Value, ct);
        }
    }

    /// <summary>
    /// Records a winner and moves both teams to wherever the bracket says they
    /// go.
    ///
    /// <b>Gated on integrity</b> (035): a match whose result we are not sure we
    /// received correctly must not move anybody. This is the single place
    /// advancement happens, so the gate cannot be bypassed by a caller that
    /// forgot about it.
    /// </summary>
    public static async Task AdvanceAsync(NextSkillDb db, Match match, Guid winnerTeamId, CancellationToken ct = default)
    {
        if (!MatchIntegrityRules.MayAdvanceBracket(match.Integrity, bothTeamsConfirmed: true, organiserTrusted: true))
            throw new InvalidOperationException(
                $"Match {match.Id} is {match.Integrity} and must not advance the bracket (035).");

        if (winnerTeamId != match.TeamAId && winnerTeamId != match.TeamBId)
            throw new InvalidOperationException("The winner must be one of the two teams in the match.");

        if (match.WinnerTeamId is not null || match.Status is MatchStatus.Forfeited or MatchStatus.Cancelled)
            throw new InvalidOperationException("A terminal match cannot advance twice.");

        match.WinnerTeamId = winnerTeamId;
        match.Status = MatchStatus.Completed;
        match.EndedAt = DateTimeOffset.UtcNow;

        var loserTeamId = winnerTeamId == match.TeamAId ? match.TeamBId : match.TeamAId;

        await PlaceAsync(db, match.WinnerToMatchId, match.WinnerToSlot, winnerTeamId, 0, ct);

        // A resolved empty loser is real information. In double elimination it
        // becomes a vacancy in the lower bracket rather than a slot that waits
        // forever for a team that cannot arrive.
        await PlaceAsync(db, match.LoserToMatchId, match.LoserToSlot, loserTeamId, 0, ct);

        await TriggerResetIfNeededAsync(db, match, winnerTeamId, ct);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The bracket reset (decisions 022, 023).
    ///
    /// <b>The row was generated up front so it could not be forgotten, and then
    /// nothing ever activated it.</b> The engine has triggered this correctly
    /// since Phase 2; the persisted bracket did not, so a real double
    /// elimination would have ended with an undefeated team eliminated on its
    /// first defeat — the exact failure 022 called the most-forgotten rule in
    /// tournament software.
    ///
    /// The grand final's slot A holds the upper-bracket winner and slot B the
    /// lower-bracket winner, so B winning is the whole condition: both teams
    /// then have exactly one loss and must play again.
    ///
    /// <b>Only from a played result.</b> A forfeit and a vacancy both reach the
    /// grand final by other paths on purpose — a team that exited the event must
    /// not be summoned back into a second final it cannot play.
    /// </summary>
    private static async Task TriggerResetIfNeededAsync(
        NextSkillDb db, Match grandFinal, Guid winnerTeamId, CancellationToken ct)
    {
        if (grandFinal is not { Side: BracketSide.GrandFinal, IsConditional: false }) return;
        if (winnerTeamId != grandFinal.TeamBId) return;

        var reset = await db.Matches.FirstOrDefaultAsync(m =>
            m.TournamentId == grandFinal.TournamentId &&
            m.Side == BracketSide.GrandFinal &&
            m.IsConditional, ct);

        // Single elimination has no reset row, and an already-filled one means
        // this ran before — both are "nothing to do" rather than errors.
        if (reset is null || reset.TeamAId is not null || reset.TeamBId is not null) return;

        // Placed directly rather than through PlaceAsync: the generator gave
        // this match Slot.Empty and no incoming routing links, precisely because
        // its occupants are decided by a condition and not by a link.
        reset.TeamAId = grandFinal.TeamAId;
        reset.TeamBId = grandFinal.TeamBId;
        reset.SlotAResolved = true;
        reset.SlotBResolved = true;
    }

    /// <summary>
    /// Records an event-level exit. The opponent advances, while the loser path
    /// receives a resolved vacancy so the forfeiting team cannot reappear in a
    /// double-elimination lower bracket.
    /// </summary>
    public static async Task ForfeitAsync(
        NextSkillDb db,
        Match match,
        Guid forfeitedTeamId,
        Guid decidedByAccountId,
        string reason,
        CancellationToken ct = default)
    {
        if (match.TeamAId is null || match.TeamBId is null)
            throw new InvalidOperationException("A forfeit needs a scheduled opponent.");

        if (forfeitedTeamId != match.TeamAId && forfeitedTeamId != match.TeamBId)
            throw new InvalidOperationException("The forfeiting team must be in the match.");

        if (match.WinnerTeamId is not null || match.Status is MatchStatus.Completed or MatchStatus.Forfeited or MatchStatus.Cancelled)
            throw new InvalidOperationException("A terminal match cannot be forfeited.");

        var registration = await db.Registrations.SingleOrDefaultAsync(r =>
            r.TournamentId == match.TournamentId &&
            r.TeamId == forfeitedTeamId &&
            r.WithdrawnAt == null,
            ct);

        if (registration is null)
            throw new InvalidOperationException("The forfeiting team is not an active tournament entrant.");

        if (registration.ForfeitedAt is not null)
            throw new InvalidOperationException("The team has already forfeited this event.");

        var now = DateTimeOffset.UtcNow;
        registration.ForfeitedAt = now;
        registration.ForfeitedByAccountId = decidedByAccountId;
        registration.ForfeitReason = reason;

        var winnerTeamId = forfeitedTeamId == match.TeamAId
            ? match.TeamBId.Value
            : match.TeamAId.Value;

        match.WinnerTeamId = winnerTeamId;
        match.ForfeitedTeamId = forfeitedTeamId;
        match.ForfeitedByAccountId = decidedByAccountId;
        match.ForfeitReason = reason;
        match.Status = MatchStatus.Forfeited;
        match.Integrity = MatchIntegrity.Confirmed;
        match.ScoreA = null;
        match.ScoreB = null;
        match.EndedAt = now;

        await PlaceAsync(db, match.WinnerToMatchId, match.WinnerToSlot, winnerTeamId, 0, ct);
        await PlaceAsync(db, match.LoserToMatchId, match.LoserToSlot, null, 0, ct);

        await DiscordAnnouncements.EnqueueResultConfirmedAsync(db, match, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task PlaceAsync(
        NextSkillDb db,
        Guid? matchId,
        SlotRef? slot,
        Guid? teamId,
        int depth,
        CancellationToken ct)
    {
        if (matchId is not { } id || slot is not { } which) return;
        if (depth > 64)
            throw new InvalidOperationException("Vacancy propagation did not terminate — routing is probably cyclic.");

        var target = await db.Matches.FirstAsync(m => m.Id == id, ct);

        if (which == SlotRef.A)
        {
            target.TeamAId = teamId;
            target.SlotAResolved = true;
        }
        else
        {
            target.TeamBId = teamId;
            target.SlotBResolved = true;
        }

        await ResolveVacancyAsync(db, target, depth + 1, ct);
    }

    /// <summary>
    /// Once both inputs are resolved, zero or one occupant is an administrative
    /// result, not an unfinished match. Propagate it immediately and unrated.
    /// </summary>
    private static async Task ResolveVacancyAsync(
        NextSkillDb db,
        Match match,
        int depth,
        CancellationToken ct)
    {
        if (!match.SlotAResolved || !match.SlotBResolved || match.IsConditional)
            return;

        if (match.WinnerTeamId is not null || match.Status is MatchStatus.Completed or MatchStatus.Forfeited or MatchStatus.Cancelled)
            return;

        var occupants = new[] { match.TeamAId, match.TeamBId }.Where(x => x is not null).ToArray();
        var now = DateTimeOffset.UtcNow;

        if (occupants.Length == 2)
            return;

        if (occupants.Length == 1)
        {
            var winner = occupants[0]!.Value;
            match.IsBye = true;
            match.WinnerTeamId = winner;
            match.Status = MatchStatus.Completed;
            match.Integrity = MatchIntegrity.Confirmed;
            match.EndedAt = now;

            await PlaceAsync(db, match.WinnerToMatchId, match.WinnerToSlot, winner, depth, ct);
            await PlaceAsync(db, match.LoserToMatchId, match.LoserToSlot, null, depth, ct);
            return;
        }

        // Two event exits can empty both sides of a later match. Nobody is
        // invented as a winner; the vacancy itself moves through both routes.
        match.Status = MatchStatus.Cancelled;
        match.EndedAt = now;
        await PlaceAsync(db, match.WinnerToMatchId, match.WinnerToSlot, null, depth, ct);
        await PlaceAsync(db, match.LoserToMatchId, match.LoserToSlot, null, depth, ct);
    }
}
