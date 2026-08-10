using NextSkill.Domain.Brackets;

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
Console.WriteLine($"{passed} passed, {failed} failed");
Console.WriteLine();
return failed == 0 ? 0 : 1;
