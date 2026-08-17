namespace NextSkill.Domain.Veto;

public enum VetoActionKind { Ban, Pick, ChooseSide }
public enum VetoStartingSide { Ct, T }

public sealed record VetoAction(
    int Sequence,
    Guid TeamId,
    VetoActionKind Kind,
    string Map,
    VetoStartingSide? Side = null,
    bool Automatic = false);

public sealed record VetoTurn(
    int Sequence,
    Guid TeamId,
    VetoActionKind Kind,
    string? Map,
    IReadOnlyList<string> LegalMaps);

public sealed record VetoMapDecision(string Map, VetoStartingSide TeamAStartsAs);

/// <summary>
/// The transport-independent veto state machine (decision 028). Callers supply
/// the frozen match inputs and durable action history; the module returns the
/// only legal next turn or a validated next action.
/// </summary>
public static class VetoRules
{
    public static readonly TimeSpan TurnDuration = TimeSpan.FromSeconds(30);

    public static VetoTurn? Next(
        int bestOf,
        Guid teamAId,
        Guid teamBId,
        IReadOnlyList<string> mapPool,
        IReadOnlyList<VetoAction> actions)
    {
        if (mapPool.Count != 7)
            throw new InvalidOperationException("The standard veto presets require exactly seven maps.");

        var preset = MapPreset(bestOf, teamAId, teamBId);
        var mapActions = actions.Where(a => a.Kind is VetoActionKind.Ban or VetoActionKind.Pick).ToArray();
        if (mapActions.Length < preset.Count)
        {
            var (teamId, kind) = preset[mapActions.Length];
            var used = mapActions.Select(a => a.Map).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var legal = mapPool.Where(map => !used.Contains(map)).ToArray();
            return new VetoTurn(actions.Count, teamId, kind, null, legal);
        }

        var pickedMaps = mapActions.Where(a => a.Kind == VetoActionKind.Pick).ToArray();
        var sideActions = actions.Where(a => a.Kind == VetoActionKind.ChooseSide).ToArray();
        if (sideActions.Length >= pickedMaps.Length) return null;

        var pick = pickedMaps[sideActions.Length];
        var chooser = pick.TeamId == teamAId ? teamBId : teamAId;
        return new VetoTurn(actions.Count, chooser, VetoActionKind.ChooseSide, pick.Map, []);
    }

    public static VetoAction Apply(VetoTurn turn, Guid teamId, string map, bool automatic = false)
    {
        if (turn.Kind == VetoActionKind.ChooseSide)
            throw new InvalidOperationException("This turn requires a starting-side choice.");
        if (teamId != turn.TeamId)
            throw new InvalidOperationException("It is the other team's turn.");

        var normalized = map.Trim().ToLowerInvariant();
        if (!turn.LegalMaps.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("That map is not a legal choice in the current veto state.");

        return new VetoAction(turn.Sequence, teamId, turn.Kind, normalized, Automatic: automatic);
    }

    public static VetoAction ChooseSide(
        VetoTurn turn,
        Guid teamId,
        VetoStartingSide side,
        bool automatic = false)
    {
        if (turn.Kind != VetoActionKind.ChooseSide || turn.Map is null)
            throw new InvalidOperationException("This turn requires a map choice.");
        if (teamId != turn.TeamId)
            throw new InvalidOperationException("It is the other team's turn.");

        return new VetoAction(turn.Sequence, teamId, turn.Kind, turn.Map, side, automatic);
    }

    public static IReadOnlyList<VetoMapDecision> Decide(
        int bestOf,
        Guid teamAId,
        Guid teamBId,
        IReadOnlyList<string> mapPool,
        IReadOnlyList<VetoAction> actions,
        VetoStartingSide deciderTeamAStartsAs)
    {
        if (Next(bestOf, teamAId, teamBId, mapPool, actions) is not null)
            throw new InvalidOperationException("The veto is not complete.");

        var picks = actions.Where(a => a.Kind == VetoActionKind.Pick).ToArray();
        var used = actions.Where(a => a.Kind is VetoActionKind.Ban or VetoActionKind.Pick)
            .Select(a => a.Map).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var decider = mapPool.Single(map => !used.Contains(map));
        var result = new List<VetoMapDecision>(bestOf);

        foreach (var pick in picks)
        {
            var choice = actions.Single(a =>
                a.Kind == VetoActionKind.ChooseSide &&
                string.Equals(a.Map, pick.Map, StringComparison.OrdinalIgnoreCase));
            var teamAStartsAs = choice.TeamId == teamAId
                ? choice.Side!.Value
                : Opposite(choice.Side!.Value);
            result.Add(new VetoMapDecision(pick.Map, teamAStartsAs));
        }

        result.Add(new VetoMapDecision(decider, deciderTeamAStartsAs));
        if (result.Count != bestOf)
            throw new InvalidOperationException("The veto result does not match the series length.");
        return result;
    }

    private static VetoStartingSide Opposite(VetoStartingSide side) =>
        side == VetoStartingSide.Ct ? VetoStartingSide.T : VetoStartingSide.Ct;

    private static IReadOnlyList<(Guid TeamId, VetoActionKind Kind)> MapPreset(
        int bestOf,
        Guid teamAId,
        Guid teamBId) => bestOf switch
        {
            1 =>
            [
                (teamAId, VetoActionKind.Ban), (teamBId, VetoActionKind.Ban),
                (teamAId, VetoActionKind.Ban), (teamBId, VetoActionKind.Ban),
                (teamAId, VetoActionKind.Ban), (teamBId, VetoActionKind.Ban),
            ],
            3 =>
            [
                (teamAId, VetoActionKind.Ban), (teamBId, VetoActionKind.Ban),
                (teamAId, VetoActionKind.Pick), (teamBId, VetoActionKind.Pick),
                (teamAId, VetoActionKind.Ban), (teamBId, VetoActionKind.Ban),
            ],
            5 =>
            [
                (teamAId, VetoActionKind.Ban), (teamBId, VetoActionKind.Ban),
                (teamAId, VetoActionKind.Pick), (teamBId, VetoActionKind.Pick),
                (teamAId, VetoActionKind.Pick), (teamBId, VetoActionKind.Pick),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(bestOf), "Best-of must be 1, 3 or 5."),
        };
}
