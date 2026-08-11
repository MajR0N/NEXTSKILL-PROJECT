namespace NextSkill.Domain.Tournaments;

/// <summary>One registered team, as seeding needs it.</summary>
/// <param name="Rating">Glicko-2 value, or null if the team has never been rated.</param>
/// <param name="Provisional">True while the rating is still too uncertain to rank on (025).</param>
public readonly record struct Entrant(Guid TeamId, double? Rating, bool Provisional, DateTimeOffset RegisteredAt);

/// <summary>
/// Turning a registration list into seed numbers (decision 046).
///
/// <b>Why seeding is not just "whoever signed up first".</b> Seed 1 and seed 2
/// start in opposite halves of the bracket ([023]), so seeding decides whether
/// the two strongest teams meet in the final or in round one. Getting it wrong
/// does not break anything — it just quietly ruins the event.
/// </summary>
public static class Seeding
{
    /// <summary>
    /// Strongest first, with unrated and provisional teams behind everyone
    /// settled.
    ///
    /// <b>A provisional team is not seeded on its rating even when that rating
    /// is high</b> (025). Two lucky wins would otherwise buy a top seed and a
    /// bye, which is the same "played two matches, ranked #1" problem the
    /// ranking page already refuses.
    ///
    /// Ties break on registration time — arbitrary, but <b>deterministic</b>,
    /// which matters more: re-seeding must never silently reshuffle a bracket
    /// an organiser has already shown people.
    /// </summary>
    public static IReadOnlyList<Guid> Order(IEnumerable<Entrant> entrants) =>
    [
        .. entrants
            .OrderBy(e => e.Rating is null || e.Provisional)   // settled ratings first
            .ThenByDescending(e => e.Rating ?? 0)
            .ThenBy(e => e.RegisteredAt)
            .ThenBy(e => e.TeamId)                             // total order, always
            .Select(e => e.TeamId)
    ];
}
