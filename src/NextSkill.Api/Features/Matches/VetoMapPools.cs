using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Features.Matches;

/// <summary>Frozen starter pools until tournament-specific pools join setup (015).</summary>
public static class VetoMapPools
{
    public static IReadOnlyList<string> For(Game game) => game switch
    {
        Game.Cs2 => ["de_ancient", "de_anubis", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_train"],
        Game.Cs16 => ["de_cache", "de_dust2", "de_inferno", "de_mirage", "de_nuke", "de_train", "de_tuscan"],
        _ => throw new ArgumentOutOfRangeException(nameof(game)),
    };
}
