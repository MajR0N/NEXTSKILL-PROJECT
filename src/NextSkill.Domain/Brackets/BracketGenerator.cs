namespace NextSkill.Domain.Brackets;

/// <summary>
/// Generates a complete bracket up front — every match row, empty, with explicit
/// routing links (decision 023).
///
/// Generating everything at the start means: the empty bracket is renderable
/// before a match is played, byes resolve here rather than as a runtime special
/// case, routing is decided once in tested code, and the grand-final reset
/// already exists instead of needing to be remembered.
///
/// Pure: no database, no clock, no I/O. That is what makes it cheap to test
/// exhaustively (decision 034).
/// </summary>
public static class BracketGenerator
{
    /// <summary>
    /// Standard tournament seeding for a power-of-two bracket: 1 plays the
    /// lowest seed, and the top two seeds can only meet in the final.
    /// </summary>
    /// <remarks>
    /// Builds by repeated mirroring: [1] → [1,2] → [1,4,2,3] → [1,8,4,5,2,7,3,6].
    /// Each pass pairs every existing seed with its complement, which is what
    /// keeps strong seeds apart for as long as the format allows.
    /// </remarks>
    public static int[] SeedOrder(int bracketSize)
    {
        var order = new List<int> { 1 };
        while (order.Count < bracketSize)
        {
            var size = order.Count * 2;
            var next = new List<int>(size);
            foreach (var seed in order)
            {
                next.Add(seed);
                next.Add(size + 1 - seed);
            }
            order = next;
        }
        return [.. order];
    }

    public static int NextPowerOfTwo(int n)
    {
        var size = 1;
        while (size < n) size *= 2;
        return size;
    }

    /// <summary>
    /// Single elimination. Byes go to the top seeds, which is the point of
    /// seeding: the reward for finishing high is a free first round.
    /// </summary>
    public static Bracket GenerateSingleElimination(int teamCount)
    {
        if (teamCount < 2) throw new ArgumentOutOfRangeException(nameof(teamCount), "A bracket needs at least two teams.");

        var size = NextPowerOfTwo(teamCount);
        var order = SeedOrder(size);
        var matches = new List<BracketMatch>();
        var id = 1;

        // Round 1 — pair the seed order two at a time. A seed above teamCount is
        // a phantom, so its opponent has a bye.
        var round1 = new List<BracketMatch>();
        for (var i = 0; i < size; i += 2)
        {
            var a = order[i];
            var b = order[i + 1];
            var aReal = a <= teamCount;
            var bReal = b <= teamCount;

            round1.Add(new BracketMatch
            {
                Id = id++,
                Side = BracketSide.Upper,
                Round = 1,
                Position = i / 2,
                A = aReal ? Slot.Seeded(a) : Slot.Empty,
                B = bReal ? Slot.Seeded(b) : Slot.Empty,
                IsBye = !(aReal && bReal),
            });
        }
        matches.AddRange(round1);

        // Later rounds — each match is fed by two from the round before.
        var previous = round1;
        var round = 2;
        while (previous.Count > 1)
        {
            var current = new List<BracketMatch>();
            for (var i = 0; i < previous.Count; i += 2)
            {
                current.Add(new BracketMatch
                {
                    Id = id++,
                    Side = BracketSide.Upper,
                    Round = round,
                    Position = i / 2,
                    A = Slot.WinnerOf(previous[i].Id),
                    B = Slot.WinnerOf(previous[i + 1].Id),
                });
            }
            matches.AddRange(current);
            previous = current;
            round++;
        }

        return new Bracket(LinkWinnersForward(matches), teamCount, size);
    }

    /// <summary>
    /// Double elimination: an upper bracket, a lower bracket, a grand final, and
    /// a conditional reset.
    ///
    /// Lower-bracket structure for R upper rounds is 2R−2 rounds, alternating:
    /// odd rounds are lower-bracket survivors playing each other, even rounds
    /// merge in that round's upper-bracket losers.
    /// </summary>
    public static Bracket GenerateDoubleElimination(int teamCount)
    {
        var upper = GenerateSingleElimination(teamCount);
        var matches = new List<BracketMatch>(upper.Matches);
        var size = upper.BracketSize;
        var upperRounds = (int)Math.Log2(size);
        var id = matches.Max(m => m.Id) + 1;

        var lowerRounds = new List<List<BracketMatch>>();
        var perRound = size / 4;

        for (var lr = 1; lr <= 2 * upperRounds - 2; lr++)
        {
            var current = new List<BracketMatch>();
            for (var pos = 0; pos < perRound; pos++)
            {
                current.Add(new BracketMatch
                {
                    Id = id++,
                    Side = BracketSide.Lower,
                    Round = lr,
                    Position = pos,
                    A = Slot.Empty,
                    B = Slot.Empty,
                });
            }
            lowerRounds.Add(current);
            matches.AddRange(current);
            if (lr % 2 == 0) perRound /= 2;
        }

        var grandFinal = new BracketMatch
        {
            Id = id++,
            Side = BracketSide.GrandFinal,
            Round = 1,
            Position = 0,
            A = Slot.WinnerOf(upper.Final.Id),
            B = Slot.WinnerOf(lowerRounds[^1][0].Id),
        };
        matches.Add(grandFinal);

        // Generated now, inactive until needed. If the lower-bracket team wins
        // the grand final both teams have one loss, so they must play again —
        // otherwise an undefeated team is eliminated on its first loss, which is
        // not double elimination at all (decision 022).
        matches.Add(new BracketMatch
        {
            Id = id,
            Side = BracketSide.GrandFinal,
            Round = 2,
            Position = 0,
            A = Slot.Empty,
            B = Slot.Empty,
            IsConditional = true,
        });

        return new Bracket(LinkDoubleElimination(matches, upperRounds, lowerRounds, grandFinal.Id), teamCount, size);
    }

    /// <summary>Fills in winner routing for a single-elimination tree.</summary>
    private static List<BracketMatch> LinkWinnersForward(List<BracketMatch> matches)
    {
        var byId = matches.ToDictionary(m => m.Id);
        var linked = new List<BracketMatch>();

        foreach (var m in matches)
        {
            var target = matches.FirstOrDefault(t =>
                (t.A.FromMatch == m.Id && !t.A.FromLoser) || (t.B.FromMatch == m.Id && !t.B.FromLoser));

            linked.Add(target is null ? m : m with
            {
                WinnerToMatch = target.Id,
                WinnerToSlot = target.A.FromMatch == m.Id && !target.A.FromLoser ? SlotRef.A : SlotRef.B,
            });
        }
        _ = byId;
        return linked;
    }

    /// <summary>
    /// Wires losers from the upper bracket into the lower bracket, and lower
    /// winners forward.
    ///
    /// Upper round 1 losers drop into lower round 1. Upper round k (k ≥ 2)
    /// losers drop into lower round 2(k−1).
    /// </summary>
    private static List<BracketMatch> LinkDoubleElimination(
        List<BracketMatch> matches, int upperRounds, List<List<BracketMatch>> lowerRounds, int grandFinalId)
    {
        var result = matches.ToDictionary(m => m.Id, m => m);

        void SetSlot(int matchId, SlotRef slot, Slot value)
        {
            var m = result[matchId];
            result[matchId] = slot == SlotRef.A ? m with { A = value } : m with { B = value };
        }

        void RouteWinner(int from, int to, SlotRef slot)
        {
            result[from] = result[from] with { WinnerToMatch = to, WinnerToSlot = slot };
            SetSlot(to, slot, Slot.WinnerOf(from));
        }

        void RouteLoser(int from, int to, SlotRef slot)
        {
            result[from] = result[from] with { LoserToMatch = to, LoserToSlot = slot };
            SetSlot(to, slot, Slot.LoserOf(from));
        }

        // Upper round 1 losers → lower round 1, both slots.
        var upper1 = matches.Where(m => m.Side == BracketSide.Upper && m.Round == 1)
                            .OrderBy(m => m.Position).ToList();
        var lower1 = lowerRounds[0];
        for (var i = 0; i < upper1.Count; i++)
            RouteLoser(upper1[i].Id, lower1[i / 2].Id, i % 2 == 0 ? SlotRef.A : SlotRef.B);

        // Lower odd rounds feed the next (even) round's slot A.
        // Even rounds take that round's upper-bracket dropdowns in slot B.
        for (var lr = 1; lr < lowerRounds.Count; lr++)
        {
            var from = lowerRounds[lr - 1];
            var to = lowerRounds[lr];
            var oddToEven = lr % 2 == 1;   // 0-based index: lr=1 is lower round 2

            for (var i = 0; i < from.Count; i++)
            {
                var targetIndex = oddToEven ? i : i / 2;
                var slot = oddToEven ? SlotRef.A : (i % 2 == 0 ? SlotRef.A : SlotRef.B);
                RouteWinner(from[i].Id, to[targetIndex].Id, slot);
            }

            if (oddToEven)
            {
                // Which upper round drops in here?
                //
                // `lr` is a 0-based index, so the target is lower round lr+1.
                // Lower round 2 takes upper round 2, lower round 4 takes upper
                // round 3, lower round 6 takes upper round 4 — that is
                // (lr+1)/2 + 1.
                //
                // Getting this wrong is silent: an off-by-one leaves one upper
                // round's losers routed nowhere and another routed twice, and
                // the bracket still renders perfectly.
                //
                // Losers join in REVERSE position order, which is what avoids an
                // immediate rematch between teams that just played each other.
                //
                // ponytail: simple reversal, not full anti-rematch analysis.
                // Ceiling: exotic seedings can still produce an early rematch.
                // Upgrade path: proper cross-placement tables, only if an
                // organiser ever complains.
                var upperRound = (lr + 1) / 2 + 1;
                var dropping = matches.Where(m => m.Side == BracketSide.Upper && m.Round == upperRound)
                                      .OrderByDescending(m => m.Position).ToList();
                for (var i = 0; i < dropping.Count && i < to.Count; i++)
                    RouteLoser(dropping[i].Id, to[i].Id, SlotRef.B);
            }
        }

        // The upper final's loser is already routed by the loop above: the last
        // lower round is always even-numbered, so it takes the last upper
        // round's dropdown. Only the winner paths to the grand final are left.
        var upperFinal = matches.Single(m => m.Side == BracketSide.Upper && m.Round == upperRounds);
        var lowerFinal = lowerRounds[^1][0];
        result[upperFinal.Id] = result[upperFinal.Id] with { WinnerToMatch = grandFinalId, WinnerToSlot = SlotRef.A };
        result[lowerFinal.Id] = result[lowerFinal.Id] with { WinnerToMatch = grandFinalId, WinnerToSlot = SlotRef.B };

        return [.. result.Values.OrderBy(m => m.Id)];
    }
}
