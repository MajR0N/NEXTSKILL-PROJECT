namespace NextSkill.Domain.Brackets;

/// <summary>
/// Plays a generated bracket: holds which seed occupies each slot, applies
/// results, and follows the explicit routing links.
///
/// Advancement is a <b>lookup</b>, not a calculation — the routing was decided
/// once at generation time (decision 023). That is what keeps the wrong team
/// from silently advancing.
/// </summary>
public sealed class BracketRun
{
    private readonly Bracket _bracket;
    private readonly Dictionary<int, int?> _slotA = [];
    private readonly Dictionary<int, int?> _slotB = [];
    private readonly Dictionary<int, int> _winner = [];

    /// <summary>Set when the lower-bracket team wins the first grand final.</summary>
    public bool ResetTriggered { get; private set; }

    public BracketRun(Bracket bracket)
    {
        _bracket = bracket;

        foreach (var m in bracket.Matches)
        {
            _slotA[m.Id] = m.A.Seed;
            _slotB[m.Id] = m.B.Seed;
        }

        // Byes resolve immediately: the occupying seed advances without playing.
        // Doing this at start means the rest of the engine never needs a special
        // case for "match with one team".
        foreach (var m in bracket.Matches.Where(m => m.IsBye))
        {
            var occupant = _slotA[m.Id] ?? _slotB[m.Id];
            if (occupant is not null) Advance(m, occupant.Value, loser: null);
        }
    }

    public int? SlotA(int matchId) => _slotA[matchId];
    public int? SlotB(int matchId) => _slotB[matchId];
    public int? WinnerOf(int matchId) => _winner.TryGetValue(matchId, out var w) ? w : null;

    /// <summary>Matches with both slots filled and no result yet.</summary>
    public IEnumerable<BracketMatch> Playable => _bracket.Matches.Where(m =>
        !_winner.ContainsKey(m.Id)
        && (!m.IsConditional || ResetTriggered)
        && _slotA[m.Id] is not null && _slotB[m.Id] is not null);

    /// <summary>Matches that were actually contested — byes are excluded, so they are never rated.</summary>
    public int PlayedCount => _winner.Count(kv => !_bracket[kv.Key].IsBye);

    /// <summary>
    /// The champion, or null while the tournament is still running.
    /// If a reset was triggered, the reset match decides it.
    /// </summary>
    public int? Champion
    {
        get
        {
            var reset = _bracket.Matches.FirstOrDefault(m => m.IsConditional);
            if (ResetTriggered && reset is not null)
                return WinnerOf(reset.Id);

            var final = _bracket.Final;
            return ResetTriggered ? null : WinnerOf(final.Id);
        }
    }

    /// <summary>Records a result and pushes both teams down their routing links.</summary>
    public BracketRun Report(int matchId, int winnerSeed)
    {
        var match = _bracket[matchId];

        if (_winner.ContainsKey(matchId))
            throw new InvalidOperationException($"Match {matchId} already has a result.");

        var a = _slotA[matchId];
        var b = _slotB[matchId];
        if (a is null || b is null)
            throw new InvalidOperationException($"Match {matchId} is not ready: both slots must be filled.");
        if (winnerSeed != a && winnerSeed != b)
            throw new InvalidOperationException($"Seed {winnerSeed} is not in match {matchId}.");

        var loser = winnerSeed == a ? b.Value : a.Value;

        // The bracket reset (decision 022). The grand final's slot A holds the
        // upper-bracket winner and slot B the lower-bracket winner. If B wins,
        // both teams now have exactly one loss, so a second grand final is
        // required — otherwise an undefeated team would be eliminated on its
        // first defeat.
        if (match is { Side: BracketSide.GrandFinal, IsConditional: false } && winnerSeed == b)
        {
            ResetTriggered = true;
            var reset = _bracket.Matches.First(m => m.IsConditional);
            _slotA[reset.Id] = a;
            _slotB[reset.Id] = b;
        }

        Advance(match, winnerSeed, loser);
        return this;
    }

    private void Advance(BracketMatch match, int winner, int? loser)
    {
        _winner[match.Id] = winner;

        if (match.WinnerToMatch is { } wTo && match.WinnerToSlot is { } wSlot)
            Place(wTo, wSlot, winner);

        if (loser is not null && match.LoserToMatch is { } lTo && match.LoserToSlot is { } lSlot)
            Place(lTo, lSlot, loser.Value);
    }

    private void Place(int matchId, SlotRef slot, int seed)
    {
        if (slot == SlotRef.A) _slotA[matchId] = seed; else _slotB[matchId] = seed;

        // A bye further along can now resolve — e.g. a seed with no opponent
        // because the feeding match was itself a bye.
        var target = _bracket[matchId];
        if (target.IsBye && !_winner.ContainsKey(matchId))
        {
            var occupant = _slotA[matchId] ?? _slotB[matchId];
            if (occupant is not null) Advance(target, occupant.Value, loser: null);
        }
    }
}
