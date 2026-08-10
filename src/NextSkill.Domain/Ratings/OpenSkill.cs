namespace NextSkill.Domain.Ratings;

/// <summary>
/// One player's skill estimate: a mean and an uncertainty.
/// </summary>
/// <param name="Mu">Estimated skill. Not shown directly.</param>
/// <param name="Sigma">Uncertainty. Shrinks with games played.</param>
public readonly record struct PlayerRating(double Mu, double Sigma)
{
    public static readonly PlayerRating Unrated = new(OpenSkill.DefaultMu, OpenSkill.DefaultSigma);

    /// <summary>
    /// The number actually displayed: a <b>conservative</b> estimate, three
    /// standard deviations below the mean.
    ///
    /// Conservative on purpose — a new player with a huge sigma ranks low until
    /// they have proved something, which is the same instinct as Glicko-2's
    /// provisional rule for teams (decision 025).
    /// </summary>
    public double Ordinal => Mu - 3 * Sigma;

    public bool IsProvisional => Sigma > OpenSkill.DefaultSigma * 0.6;
}

/// <summary>
/// OpenSkill (Bradley–Terry full pairing) for individual player ratings.
///
/// Chosen over TrueSkill because TrueSkill is proprietary, and over a
/// performance-based rating (kills, damage, utility) for a decisive reason:
/// <b>OpenSkill needs only who was on which team and who won.</b> CS 1.6 exposes
/// far less stat data than CS2, so a stats-based rating would have been CS2-only
/// and left 1.6 players without a number — breaking the promise that both games
/// are first-class (decisions 001, 025).
///
/// Pure: no database, no clock, no I/O.
/// </summary>
public static class OpenSkill
{
    public const double DefaultMu = 25.0;
    public const double DefaultSigma = DefaultMu / 3.0;

    /// <summary>Skill difference that gives roughly a 76% win chance.</summary>
    private const double Beta = DefaultSigma / 2.0;

    /// <summary>
    /// Additive dynamics: a small amount of uncertainty is restored each match,
    /// so a rating never becomes so certain that it stops responding.
    /// </summary>
    private const double Tau = DefaultSigma / 100.0;

    private const double Epsilon = 0.0001;

    /// <summary>
    /// Rates one match between two teams.
    /// </summary>
    /// <param name="winners">The winning team's roster.</param>
    /// <param name="losers">The losing team's roster.</param>
    /// <returns>Updated ratings, in the same order as the inputs.</returns>
    public static (PlayerRating[] Winners, PlayerRating[] Losers) Rate(
        IReadOnlyList<PlayerRating> winners, IReadOnlyList<PlayerRating> losers)
    {
        if (winners.Count == 0 || losers.Count == 0)
            throw new ArgumentException("Both teams need at least one player.");

        // Restore a little uncertainty before rating, so ratings stay responsive.
        var w = winners.Select(WithDynamics).ToArray();
        var l = losers.Select(WithDynamics).ToArray();

        var muW = w.Sum(p => p.Mu);
        var muL = l.Sum(p => p.Mu);
        var varW = w.Sum(p => p.Sigma * p.Sigma);
        var varL = l.Sum(p => p.Sigma * p.Sigma);

        // c: combined uncertainty of the matchup, including inherent randomness.
        var c = Math.Sqrt(varW + varL + 2 * Beta * Beta);

        // Expected win probability for each side.
        var pW = 1 / (1 + Math.Exp((muL - muW) / c));
        var pL = 1 / (1 + Math.Exp((muW - muL) / c));

        // Team-level adjustment, then shared out per player by their own share
        // of the team's uncertainty: the least certain player moves most, since
        // the result says most about them.
        var gammaW = Math.Sqrt(varW) / c;
        var gammaL = Math.Sqrt(varL) / c;

        var omegaW = varW / c * (1 - pW);           // winners scored 1
        var deltaW = gammaW * varW / (c * c) * pW * (1 - pW);

        var omegaL = varL / c * (0 - pL);           // losers scored 0
        var deltaL = gammaL * varL / (c * c) * pL * (1 - pL);

        return (
            Winners: [.. w.Select(p => Update(p, varW, omegaW, deltaW))],
            Losers: [.. l.Select(p => Update(p, varL, omegaL, deltaL))]);
    }

    private static PlayerRating WithDynamics(PlayerRating p) =>
        p with { Sigma = Math.Sqrt(p.Sigma * p.Sigma + Tau * Tau) };

    private static PlayerRating Update(PlayerRating p, double teamVariance, double omega, double delta)
    {
        var share = p.Sigma * p.Sigma / teamVariance;
        var mu = p.Mu + share * omega;
        var sigma = p.Sigma * Math.Sqrt(Math.Max(1 - share * delta, Epsilon));
        return new PlayerRating(mu, sigma);
    }

    /// <summary>
    /// Probability that <paramref name="a"/> beats <paramref name="b"/>.
    /// Useful for seeding and for showing an honest expectation before a match.
    /// </summary>
    public static double WinProbability(IReadOnlyList<PlayerRating> a, IReadOnlyList<PlayerRating> b)
    {
        var muA = a.Sum(p => p.Mu);
        var muB = b.Sum(p => p.Mu);
        var c = Math.Sqrt(a.Sum(p => p.Sigma * p.Sigma) + b.Sum(p => p.Sigma * p.Sigma) + 2 * Beta * Beta);
        return 1 / (1 + Math.Exp((muB - muA) / c));
    }
}
