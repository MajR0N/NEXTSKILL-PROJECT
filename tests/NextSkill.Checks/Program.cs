using NextSkill.Domain.Brackets;
using NextSkill.Domain.Ratings;

// NextSkill.Checks — console assert runner. No test framework, per decision 034.
// Exits non-zero on failure so CI can gate on it.
//
// These check the things that fail SILENTLY: a bracket where the wrong team
// advances looks completely normal until the final.

var failed = 0;
var passed = 0;

void Check(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}");
        Console.WriteLine($"        {ex.Message}");
    }
}

void That(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

void Equal<T>(T expected, T actual, string what)
{
    if (!Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}");
}

/// <summary>Plays a bracket to completion with the better seed always winning.</summary>
BracketRun PlayFavouredSeeds(Bracket bracket)
{
    var run = new BracketRun(bracket);
    var guard = 0;
    while (true)
    {
        var next = run.Playable.FirstOrDefault();
        if (next is null) break;
        if (++guard > 500) throw new Exception("Bracket did not terminate — routing is probably cyclic.");
        var a = run.SlotA(next.Id)!.Value;
        var b = run.SlotB(next.Id)!.Value;
        run.Report(next.Id, Math.Min(a, b));   // lower seed number = stronger
    }
    return run;
}

Console.WriteLine();
Console.WriteLine("BRACKETS  (decisions 022, 023)");

Check("seed order keeps 1 and 2 apart until the final", () =>
{
    var order = BracketGenerator.SeedOrder(8);
    Equal(8, order.Length, "bracket size");
    Equal(1, order[0], "first seed");
    Equal(8, order[1], "seed 1's first opponent");
    // 1 is in the first half, 2 in the second: they cannot meet before the final.
    var posOf1 = Array.IndexOf(order, 1);
    var posOf2 = Array.IndexOf(order, 2);
    That(posOf1 < 4 && posOf2 >= 4, "seeds 1 and 2 must start in opposite halves");
});

Check("8-team single elim: 7 matches, correct champion", () =>
{
    var b = BracketGenerator.GenerateSingleElimination(8);
    Equal(7, b.Matches.Count, "match count");
    var run = PlayFavouredSeeds(b);
    Equal(1, run.Champion, "champion");
    Equal(7, run.PlayedCount, "matches actually played");
});

Check("13-team single elim: byes go to top seeds and are NOT rated", () =>
{
    var b = BracketGenerator.GenerateSingleElimination(13);
    Equal(16, b.BracketSize, "bracket size");

    var byes = b.Matches.Where(m => m.IsBye).ToList();
    Equal(3, byes.Count, "bye count (16 - 13)");

    // The seed sitting in each bye must be one of the top 3.
    foreach (var bye in byes)
    {
        var occupant = bye.A.Seed ?? bye.B.Seed;
        That(occupant is >= 1 and <= 3, $"bye went to seed {occupant}, expected a top-3 seed");
    }

    var run = PlayFavouredSeeds(b);
    Equal(1, run.Champion, "champion");
    // 13 teams => 12 real matches. A bye is a free win and must never be rated
    // or counted as played (decisions 022, 025).
    Equal(12, run.PlayedCount, "matches actually played (byes excluded)");
});

Check("8-team double elim: structure and no dangling routing links", () =>
{
    var b = BracketGenerator.GenerateDoubleElimination(8);

    Equal(7, b.Matches.Count(m => m.Side == BracketSide.Upper), "upper matches");
    Equal(6, b.Matches.Count(m => m.Side == BracketSide.Lower), "lower matches");
    Equal(2, b.Matches.Count(m => m.Side == BracketSide.GrandFinal), "grand final + reset");

    var ids = b.Matches.Select(m => m.Id).ToHashSet();
    foreach (var m in b.Matches)
    {
        if (m.WinnerToMatch is { } w) That(ids.Contains(w), $"match {m.Id} routes its winner to {w}, which does not exist");
        if (m.LoserToMatch is { } l) That(ids.Contains(l), $"match {m.Id} routes its loser to {l}, which does not exist");
    }

    // Every upper match except the final must send its loser somewhere, or a
    // team would be eliminated on a single defeat — which is single elimination.
    foreach (var m in b.Matches.Where(m => m.Side == BracketSide.Upper && m.Round < 3))
        That(m.LoserToMatch is not null, $"upper match {m.Id} drops its loser nowhere");
});

Check("8-team double elim: favoured seeds, no reset, 14 matches", () =>
{
    var b = BracketGenerator.GenerateDoubleElimination(8);
    var run = PlayFavouredSeeds(b);
    Equal(1, run.Champion, "champion");
    That(!run.ResetTriggered, "seed 1 never lost, so no reset should have been created");
    Equal(14, run.PlayedCount, "matches played (2n-2 for n=8)");
});

// ── THE ONE MOST LIKELY TO BE MISSING (decision 022) ───────────────────────
Check("BRACKET RESET: lower-bracket winner takes grand final #1 → a second one is created", () =>
{
    var b = BracketGenerator.GenerateDoubleElimination(8);
    var run = new BracketRun(b);

    // Play everything except the grand final with favoured seeds.
    var grandFinal = b.Matches.First(m => m.Side == BracketSide.GrandFinal && !m.IsConditional);
    while (true)
    {
        var next = run.Playable.FirstOrDefault(m => m.Id != grandFinal.Id);
        if (next is null) break;
        var a = run.SlotA(next.Id)!.Value;
        var b2 = run.SlotB(next.Id)!.Value;
        run.Report(next.Id, Math.Min(a, b2));
    }

    var upperWinner = run.SlotA(grandFinal.Id)!.Value;
    var lowerWinner = run.SlotB(grandFinal.Id)!.Value;
    That(upperWinner != lowerWinner, "grand final must have two different teams");

    // The lower-bracket team wins. Both now have exactly one loss.
    run.Report(grandFinal.Id, lowerWinner);

    That(run.ResetTriggered, "a reset MUST be triggered when the lower-bracket team wins");
    That(run.Champion is null, "there must be no champion yet — the reset has not been played");

    var reset = b.Matches.First(m => m.IsConditional);
    Equal(upperWinner, run.SlotA(reset.Id), "reset slot A");
    Equal(lowerWinner, run.SlotB(reset.Id), "reset slot B");

    run.Report(reset.Id, lowerWinner);
    Equal(lowerWinner, run.Champion, "champion after the reset");
});

Check("NO reset when the upper-bracket team wins grand final #1", () =>
{
    var b = BracketGenerator.GenerateDoubleElimination(8);
    var run = new BracketRun(b);
    var grandFinal = b.Matches.First(m => m.Side == BracketSide.GrandFinal && !m.IsConditional);

    while (true)
    {
        var next = run.Playable.FirstOrDefault(m => m.Id != grandFinal.Id);
        if (next is null) break;
        var a = run.SlotA(next.Id)!.Value;
        var b2 = run.SlotB(next.Id)!.Value;
        run.Report(next.Id, Math.Min(a, b2));
    }

    var upperWinner = run.SlotA(grandFinal.Id)!.Value;
    run.Report(grandFinal.Id, upperWinner);

    That(!run.ResetTriggered, "the upper-bracket team was undefeated — no reset");
    Equal(upperWinner, run.Champion, "champion immediately");
});

Check("16-team double elim terminates with a champion", () =>
{
    var b = BracketGenerator.GenerateDoubleElimination(16);
    var run = PlayFavouredSeeds(b);
    Equal(1, run.Champion, "champion");
    Equal(30, run.PlayedCount, "matches played (2n-2 for n=16)");
});

Console.WriteLine();
Console.WriteLine("RATINGS  (decision 025)");

void Close(double expected, double actual, double tolerance, string what)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new Exception($"{what}: expected ~{expected:F2}, got {actual:F2}");
}

Check("Glickman's worked example reproduces the published result", () =>
{
    // From the Glicko-2 paper: 1500/200 plays three opponents, W L L.
    // Published answer: rating ~1464.06, RD ~151.52.
    // If this drifts, the implementation is wrong regardless of how sensible
    // the other checks look.
    var player = new Glicko2Rating(1500, 200, 0.06);
    var result = Glicko2.Apply(player,
    [
        new Outcome(new Glicko2Rating(1400, 30, 0.06), 1),
        new Outcome(new Glicko2Rating(1550, 100, 0.06), 0),
        new Outcome(new Glicko2Rating(1700, 300, 0.06), 0),
    ]);

    Close(1464.06, result.Rating, 0.5, "rating");
    Close(151.52, result.Deviation, 0.5, "deviation");
});

Check("inactivity: rating UNCHANGED, RD grows (ratings never decay)", () =>
{
    var team = new Glicko2Rating(1847, 32, 0.06);
    var after = Glicko2.Apply(team, []);

    Close(1847, after.Rating, 0.0001, "rating must not move");
    That(after.Deviation > team.Deviation,
        $"RD must grow while inactive: was {team.Deviation:F2}, now {after.Deviation:F2}");
});

Check("RD is capped so an absent team never exceeds an unrated one", () =>
{
    var team = new Glicko2Rating(1600, 340, 0.06);
    var after = team;
    for (var i = 0; i < 50; i++) after = Glicko2.Apply(after, []);
    That(after.Deviation <= 350.0001, $"RD ran away to {after.Deviation:F2}");
});

Check("anti-farming: beating a much weaker team gains almost nothing", () =>
{
    var strong = new Glicko2Rating(1847, 40, 0.06);
    var weak = new Glicko2Rating(1200, 40, 0.06);
    var equal = new Glicko2Rating(1847, 40, 0.06);

    var farmGain = Glicko2.Apply(strong, [new Outcome(weak, 1)]).Rating - strong.Rating;
    var fairGain = Glicko2.Apply(strong, [new Outcome(equal, 1)]).Rating - strong.Rating;

    That(farmGain > 0, "beating anyone should still be positive");
    That(farmGain < 3, $"farming gain should be near zero, was {farmGain:F2}");
    That(fairGain > farmGain * 3,
        $"beating an equal ({fairGain:F2}) must be worth far more than farming ({farmGain:F2})");
});

Check("an upset loss costs far more than the win would have gained", () =>
{
    // Asserted as a RELATIONSHIP, not an absolute number. An earlier version of
    // this check demanded "worse than -20 points" and failed at -9.51 — but the
    // code was right and the check was wrong: RD 40 means a very settled rating,
    // and small movements are precisely what low RD is supposed to produce.
    // Glickman's published example passing is what proves the maths; this check
    // only needs to pin the asymmetry.
    var strong = new Glicko2Rating(1847, 40, 0.06);
    var weak = new Glicko2Rating(1200, 40, 0.06);

    var gain = Glicko2.Apply(strong, [new Outcome(weak, 1)]).Rating - strong.Rating;
    var loss = Glicko2.Apply(strong, [new Outcome(weak, 0)]).Rating - strong.Rating;

    That(loss < 0, $"losing must reduce the rating, was {loss:F2}");
    That(Math.Abs(loss) > gain * 3,
        $"an upset loss ({loss:F2}) must cost far more than the win gains ({gain:F2})");
});

Check("RD controls how much one result moves a rating", () =>
{
    // The same upset, once for a settled team and once for an uncertain one.
    // This is why a returning team's rating moves in bigger steps (025).
    var weak = new Glicko2Rating(1200, 40, 0.06);

    var settled = new Glicko2Rating(1847, 40, 0.06);
    var uncertain = new Glicko2Rating(1847, 200, 0.06);

    var settledSwing = Math.Abs(Glicko2.Apply(settled, [new Outcome(weak, 0)]).Rating - settled.Rating);
    var uncertainSwing = Math.Abs(Glicko2.Apply(uncertain, [new Outcome(weak, 0)]).Rating - uncertain.Rating);

    That(uncertainSwing > settledSwing * 2,
        $"an uncertain rating must move more: settled {settledSwing:F2} vs uncertain {uncertainSwing:F2}");
});

Check("provisional: a new team stays off the leaderboard until RD settles", () =>
{
    That(Glicko2Rating.Unrated.IsProvisional, "a brand-new team must be provisional");

    var team = Glicko2Rating.Unrated;
    for (var i = 0; i < 12; i++)
        team = Glicko2.Apply(team, [new Outcome(new Glicko2Rating(1500, 60, 0.06), i % 2 == 0 ? 1 : 0)]);

    That(!team.IsProvisional, $"after 12 matches a team should be ranked, RD was {team.Deviation:F2}");
});

Check("DETERMINISTIC: replaying the same sequence gives an identical rating", () =>
{
    // This is what makes recomputation after a reversed result trustworthy.
    Glicko2Rating Play()
    {
        var r = Glicko2Rating.Unrated;
        foreach (var (opp, score) in new[] { (1500d, 1d), (1600d, 0d), (1450d, 1d), (1700d, 0.5d) })
            r = Glicko2.Apply(r, [new Outcome(new Glicko2Rating(opp, 80, 0.06), score)]);
        return r;
    }

    var first = Play();
    var second = Play();
    Equal(first, second, "identical replay must give an identical rating");
});

Check("policy: byes, disputed results and open challenge windows are NOT rated", () =>
{
    var ok = new MatchFacts(IsBye: false, ResultIntegrity.Confirmed, ChallengeWindowClosed: true);
    That(RatingPolicy.IsRated(ok), "a clean confirmed result must be rated");

    That(!RatingPolicy.IsRated(ok with { IsBye = true }), "a bye must never be rated");
    That(!RatingPolicy.IsRated(ok with { Integrity = ResultIntegrity.Disputed }), "disputed must not be rated");
    That(!RatingPolicy.IsRated(ok with { Integrity = ResultIntegrity.Suspect }), "suspect must not be rated");
    That(!RatingPolicy.IsRated(ok with { ChallengeWindowClosed = false }), "an open challenge window must not be rated");
    That(!RatingPolicy.IsRated(ok with { IsForfeit = true }), "a forfeit must not be rated by default");

    // The reason is surfaced, not just a boolean — an organiser asking
    // "why didn't this count?" deserves an answer.
    That(RatingPolicy.ExcludedBecause(ok with { IsBye = true })!.Contains("bye"), "reason should mention the bye");
});

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
Console.WriteLine();
return failed == 0 ? 0 : 1;
