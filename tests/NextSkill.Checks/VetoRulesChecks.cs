using NextSkill.Domain.Veto;

namespace NextSkill.Checks;

public static class VetoRulesChecks
{
    private static readonly string[] Pool = ["a", "b", "c", "d", "e", "f", "g"];

    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();

        IReadOnlyList<VetoMapDecision> Play(int bestOf)
        {
            var actions = new List<VetoAction>();
            while (VetoRules.Next(bestOf, teamA, teamB, Pool, actions) is { } turn)
            {
                actions.Add(turn.Kind == VetoActionKind.ChooseSide
                    ? VetoRules.ChooseSide(turn, turn.TeamId, VetoStartingSide.Ct)
                    : VetoRules.Apply(turn, turn.TeamId, turn.LegalMaps[0]));
            }

            return VetoRules.Decide(bestOf, teamA, teamB, Pool, actions, VetoStartingSide.Ct);
        }

        check("veto rules: BO1 produces its decider and an immutable starting side", () =>
        {
            var result = Play(1);
            that(result.Count == 1 && result[0] == new VetoMapDecision("g", VetoStartingSide.Ct),
                "six bans must leave one BO1 decider with its decided starting side");
        });

        check("veto rules: BO3 preserves pick order, then appends the decider", () =>
        {
            var result = Play(3);
            that(result.Select(x => x.Map).SequenceEqual(["c", "d", "g"]),
                "BO3 must play both picks in pick order and the remaining map last");
            that(result.Select(x => x.TeamAStartsAs).SequenceEqual(
                    [VetoStartingSide.T, VetoStartingSide.Ct, VetoStartingSide.Ct]),
                "the non-picker's choice must be converted into Team A's starting side");
        });

        check("veto rules: BO5 preserves all four picks, then appends the decider", () =>
        {
            var result = Play(5);
            that(result.Select(x => x.Map).SequenceEqual(["c", "d", "e", "f", "g"]),
                "BO5 must preserve four picks and put the remaining decider last");
            that(result.Count == 5, "the decided map list length must equal best-of");
        });
    }
}
