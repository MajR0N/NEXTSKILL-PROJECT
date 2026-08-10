namespace NextSkill.Domain.Brackets;

/// <summary>Which half of a double-elimination bracket a match belongs to.</summary>
public enum BracketSide { Upper, Lower, GrandFinal }

/// <summary>Which of a match's two slots is being referred to.</summary>
public enum SlotRef { A, B }

/// <summary>
/// Where a slot's team comes from: a seed, the winner of a match, or the loser
/// of a match. Empty means "not yet determined".
/// </summary>
public readonly record struct Slot(int? Seed, int? FromMatch, bool FromLoser)
{
    public static Slot Seeded(int seed) => new(seed, null, false);
    public static Slot WinnerOf(int matchId) => new(null, matchId, false);
    public static Slot LoserOf(int matchId) => new(null, matchId, true);
    public static readonly Slot Empty = new(null, null, false);

    public bool IsEmpty => Seed is null && FromMatch is null;
}

/// <summary>
/// One match in a generated bracket.
///
/// <b>Routing is explicit</b> (<see cref="WinnerToMatch"/> / <see cref="LoserToMatch"/>)
/// rather than computed from round and position when a result lands
/// (decision 023). Single elimination has regular maths; double-elimination
/// loser routing does not, and re-deriving it on every result is exactly where
/// the wrong team silently advances.
/// </summary>
public sealed record BracketMatch
{
    public required int Id { get; init; }
    public required BracketSide Side { get; init; }
    public required int Round { get; init; }
    public required int Position { get; init; }

    public required Slot A { get; init; }
    public required Slot B { get; init; }

    public int? WinnerToMatch { get; init; }
    public SlotRef? WinnerToSlot { get; init; }
    public int? LoserToMatch { get; init; }
    public SlotRef? LoserToSlot { get; init; }

    /// <summary>
    /// The grand-final reset. Generated up front but inactive unless the
    /// lower-bracket team wins the first grand final — so the most commonly
    /// forgotten rule in tournament software cannot be forgotten, because it
    /// already exists (decision 022).
    /// </summary>
    public bool IsConditional { get; init; }

    /// <summary>
    /// A walkover: one side has no opponent. The occupying seed advances
    /// automatically and <b>the match is never rated</b> — a free win must not
    /// inflate a rating (decisions 022, 025).
    /// </summary>
    public bool IsBye { get; init; }
}

/// <summary>A generated bracket. Immutable; results are applied by <see cref="BracketRun"/>.</summary>
public sealed record Bracket(IReadOnlyList<BracketMatch> Matches, int TeamCount, int BracketSize)
{
    public BracketMatch this[int id] => Matches.First(m => m.Id == id);

    /// <summary>The match that decides the tournament (excluding a possible reset).</summary>
    public BracketMatch Final => Matches.Last(m => !m.IsConditional);
}
