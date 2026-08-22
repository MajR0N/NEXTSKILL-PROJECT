using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Matches;

namespace NextSkill.Api.Features.Matches;

/// <summary>
/// What a referee can do about a result (decisions 035, 036, 050 §5).
///
/// Both actions touch the <b>rating</b> and neither touches the bracket, which
/// is the one rule holding this design together: human judgement gets the
/// reversible thing and never the irreversible one. Teams already sitting in a
/// server cannot be un-sat.
/// </summary>
public static class DisputeResolution
{
    /// <summary>
    /// The claim was heard and the result stands. Clearing the pause is all it
    /// takes — the sweeper picks the match up on its next pass and rates it
    /// normally.
    /// </summary>
    public static void Dismiss(Match match) => match.DisputedAt = null;

    /// <summary>
    /// The result was wrong. Records the corrected winner and rebuilds every
    /// rating on the platform from the ledger.
    ///
    /// <b>A referee override advances anything</b> (035): the integrity flag is
    /// cleared alongside the winner, because a machine's doubt about how a
    /// result arrived is not a reason to keep refusing a human who has since
    /// established what happened. That is why every use of this is audited.
    /// </summary>
    public static async Task ReverseAsync(
        NextSkillDb db,
        Match match,
        Guid? correctedWinnerTeamId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (correctedWinnerTeamId is { } winner &&
            winner != match.TeamAId && winner != match.TeamBId)
            throw new InvalidOperationException("The corrected winner must be one of the two teams in the match.");

        match.WinnerTeamId = correctedWinnerTeamId;
        match.Integrity = MatchIntegrity.Confirmed;
        match.DisputedAt = null;

        await ReplayAsync(db, now, ct);
    }

    /// <summary>
    /// The recomputation seam 025 promised, and the reason 050 made the ledger
    /// append-only in the first place.
    ///
    /// <b>Public because it has a second caller.</b> An account merge moves one
    /// person's <c>match_participants</c> onto the account that survives, which
    /// changes who every affected result was about — and the honest way to
    /// restate that is the same full replay a reversal already uses, rather
    /// than a bespoke arithmetic that patches two players' numbers.
    ///
    /// Every live row is superseded, every rating is dropped and every match is
    /// marked unrated; the sweeper then rebuilds the lot in <c>(EndedAt, Id)</c>
    /// order. Determinism is what makes that safe — the same inputs in the same
    /// order give the same numbers, which is exactly what the shuffled-order
    /// check exists to prove.
    ///
    /// <b>Global rather than scoped to the reversed match, and deliberately.</b>
    /// Reversing a semifinal changes who played the final, and recomputing only
    /// the affected team would rate it against opponents' <i>present</i> ratings
    /// rather than the ones they held at the time — arithmetic that looks
    /// careful and is wrong. Replaying everything is both simpler and correct.
    ///
    /// ponytail: O(every match on the platform) per reversal, and a reversal is
    /// a rare human decision, so this is the right trade at cup scale. Ceiling:
    /// it becomes slow once seasons accumulate. Upgrade path: snapshot each
    /// rating at season close — 051 §4 already gives seasons a closing timestamp
    /// — and replay only from the most recent snapshot.
    /// </summary>
    public static async Task ReplayAsync(NextSkillDb db, DateTimeOffset now, CancellationToken ct)
    {
        // Superseded, never deleted: the old numbers stay readable, so a player
        // can still see what their rating was and what replaced it.
        await db.RatingChanges
            .Where(r => r.SupersededAt == null && r.MatchId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SupersededAt, now), ct);

        await db.Matches
            .IgnoreQueryFilters()
            .Where(m => m.RatedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RatedAt, (DateTimeOffset?)null), ct);

        // Dropped rather than zeroed. The sweeper creates a rating on demand
        // from Unrated, so re-seeding defaults here would be a second copy of
        // that starting point, free to drift from the real one.
        await db.Ratings.ExecuteDeleteAsync(ct);
    }
}
