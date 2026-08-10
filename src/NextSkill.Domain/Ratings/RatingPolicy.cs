namespace NextSkill.Domain.Ratings;

/// <summary>Integrity state of a match, mirrored from decision 035.</summary>
public enum ResultIntegrity { Confirmed, Suspect, Disputed }

/// <summary>The facts about a match that decide whether it counts toward ratings.</summary>
/// <param name="IsBye">A walkover — nobody played.</param>
/// <param name="Integrity">Whether the result can be trusted yet.</param>
/// <param name="ChallengeWindowClosed">
/// Whether the period in which a captain may dispute has elapsed
/// (decision 007).
/// </param>
/// <param name="IsForfeit">A no-show rather than a contest.</param>
public readonly record struct MatchFacts(
    bool IsBye,
    ResultIntegrity Integrity,
    bool ChallengeWindowClosed,
    bool IsForfeit = false);

/// <summary>
/// Decides which matches count toward a rating (decision 025).
///
/// Kept separate from the maths on purpose: <b>this is policy, and policy is
/// what people argue about.</b> Isolating it means the arguing happens in one
/// small, tested function rather than being scattered through the codebase.
/// </summary>
public static class RatingPolicy
{
    /// <summary>Reasons a match does not affect ratings. Empty means it does.</summary>
    public static string? ExcludedBecause(MatchFacts facts)
    {
        // A free win must never inflate a rating (decision 022).
        if (facts.IsBye)
            return "bye — nobody played";

        // Never rate a result the system does not yet trust (decision 035).
        if (facts.Integrity != ResultIntegrity.Confirmed)
            return $"integrity is {facts.Integrity} — not confirmed";

        // Ratings must not settle before results do. Applying a change that
        // later has to be reversed is worse than applying it a few hours late
        // (decision 025).
        if (!facts.ChallengeWindowClosed)
            return "challenge window still open";

        // A forfeit measures attendance, not skill. Configurable per tournament;
        // unrated by default.
        if (facts.IsForfeit)
            return "forfeit — measures attendance, not skill";

        return null;
    }

    public static bool IsRated(MatchFacts facts) => ExcludedBecause(facts) is null;
}
