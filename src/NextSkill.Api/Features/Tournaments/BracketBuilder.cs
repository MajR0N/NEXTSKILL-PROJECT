using Microsoft.EntityFrameworkCore;
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

        match.WinnerTeamId = winnerTeamId;
        match.Status = match.IsBye ? MatchStatus.Completed : MatchStatus.Completed;
        match.EndedAt = DateTimeOffset.UtcNow;

        var loserTeamId = winnerTeamId == match.TeamAId ? match.TeamBId : match.TeamAId;

        await PlaceAsync(db, match.WinnerToMatchId, match.WinnerToSlot, winnerTeamId, ct);

        // A bye has no loser to send anywhere, and a loser slot fed by an empty
        // side would put a null into the lower bracket.
        if (loserTeamId is { } loser)
            await PlaceAsync(db, match.LoserToMatchId, match.LoserToSlot, loser, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task PlaceAsync(NextSkillDb db, Guid? matchId, SlotRef? slot, Guid teamId, CancellationToken ct)
    {
        if (matchId is not { } id || slot is not { } which) return;

        var target = await db.Matches.FirstAsync(m => m.Id == id, ct);

        if (which == SlotRef.A) target.TeamAId = teamId;
        else target.TeamBId = teamId;
    }
}
