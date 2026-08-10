namespace NextSkill.Domain.Ratings;

/// <summary>
/// A team's rating: the number, how sure we are, and how erratic they've been.
/// </summary>
/// <param name="Rating">Displayed value, e.g. 1847.</param>
/// <param name="Deviation">
/// Rating deviation (RD) — the ± shown in the UI. Shrinks with play, grows with
/// inactivity. Shown openly because hiding uncertainty is what makes a returning
/// team's rating jump look arbitrary (decision 025).
/// </param>
/// <param name="Volatility">How erratic results have been. Rarely surfaced.</param>
public readonly record struct Glicko2Rating(double Rating, double Deviation, double Volatility)
{
    /// <summary>A new, unrated entrant: wide RD, so early results move them a lot.</summary>
    public static readonly Glicko2Rating Unrated = new(1500, 350, 0.06);

    /// <summary>
    /// Provisional until we are reasonably sure. Provisional teams are rated
    /// internally but kept off public leaderboards, which is what stops
    /// "played two matches, ranked #1" (decision 025).
    /// </summary>
    public bool IsProvisional => Deviation > 110;
}

/// <summary>One opponent faced during a rating period.</summary>
/// <param name="Opponent">Their rating at the time.</param>
/// <param name="Score">1 = win, 0.5 = draw, 0 = loss.</param>
public readonly record struct Outcome(Glicko2Rating Opponent, double Score);

/// <summary>
/// Glicko-2, per Glickman's specification.
///
/// Chosen over Elo because it models <b>uncertainty and inactivity</b>, which is
/// the community-scene pattern: play a tournament, disappear for two months,
/// come back. Elo handles that badly; Glicko-2 handles it by design
/// (decision 025).
///
/// Pure: same inputs always give the same outputs, which is what makes
/// recomputing ratings after a reversed result trustworthy.
/// </summary>
public static class Glicko2
{
    /// <summary>Glicko-2 scale factor (173.7178).</summary>
    private const double Scale = 173.7178;

    /// <summary>
    /// System constant τ: how much volatility may change per period. Glickman
    /// suggests 0.3–1.2; smaller is more stable. 0.5 is the common default.
    /// </summary>
    private const double Tau = 0.5;

    private const double Epsilon = 0.000001;

    /// <summary>
    /// Applies one rating period.
    ///
    /// With no outcomes the rating is <b>unchanged</b> and only RD grows —
    /// decision 025 is explicit that ratings never decay. Decaying punishes
    /// people for having lives; growing RD expresses "we're less sure now"
    /// without taking anything away.
    /// </summary>
    public static Glicko2Rating Apply(Glicko2Rating current, IReadOnlyList<Outcome> outcomes)
    {
        var mu = (current.Rating - 1500) / Scale;
        var phi = current.Deviation / Scale;
        var sigma = current.Volatility;

        if (outcomes.Count == 0)
        {
            var phiInactive = Math.Sqrt(phi * phi + sigma * sigma);
            return current with { Deviation = Math.Min(phiInactive * Scale, 350) };
        }

        // v — estimated variance of the rating based on who was played.
        var vInverse = 0d;
        var deltaSum = 0d;
        foreach (var o in outcomes)
        {
            var muJ = (o.Opponent.Rating - 1500) / Scale;
            var phiJ = o.Opponent.Deviation / Scale;
            var g = G(phiJ);
            var e = E(mu, muJ, phiJ);
            vInverse += g * g * e * (1 - e);
            deltaSum += g * (o.Score - e);
        }
        var v = 1 / vInverse;
        var delta = v * deltaSum;

        var sigmaPrime = NewVolatility(phi, v, delta, sigma);

        // Pre-rating-period value: uncertainty grows, then the results shrink it.
        var phiStar = Math.Sqrt(phi * phi + sigmaPrime * sigmaPrime);
        var phiPrime = 1 / Math.Sqrt(1 / (phiStar * phiStar) + 1 / v);
        var muPrime = mu + phiPrime * phiPrime * deltaSum;

        return new Glicko2Rating(
            Rating: muPrime * Scale + 1500,
            Deviation: Math.Min(phiPrime * Scale, 350),
            Volatility: sigmaPrime);
    }

    /// <summary>Weights an opponent by how certain their own rating is.</summary>
    private static double G(double phi) => 1 / Math.Sqrt(1 + 3 * phi * phi / (Math.PI * Math.PI));

    /// <summary>Expected score against one opponent.</summary>
    private static double E(double mu, double muJ, double phiJ) =>
        1 / (1 + Math.Exp(-G(phiJ) * (mu - muJ)));

    /// <summary>
    /// Solves for the new volatility using the Illinois variant of regula falsi,
    /// as specified by Glickman. Converges in a handful of iterations.
    /// </summary>
    private static double NewVolatility(double phi, double v, double delta, double sigma)
    {
        var a = Math.Log(sigma * sigma);

        double F(double x)
        {
            var ex = Math.Exp(x);
            var d2 = delta * delta;
            var p2 = phi * phi;
            var numerator = ex * (d2 - p2 - v - ex);
            var denominator = 2 * Math.Pow(p2 + v + ex, 2);
            return numerator / denominator - (x - a) / (Tau * Tau);
        }

        var A = a;
        double B;

        if (delta * delta > phi * phi + v)
        {
            B = Math.Log(delta * delta - phi * phi - v);
        }
        else
        {
            var k = 1;
            while (F(a - k * Tau) < 0) k++;
            B = a - k * Tau;
        }

        var fA = F(A);
        var fB = F(B);

        var guard = 0;
        while (Math.Abs(B - A) > Epsilon)
        {
            if (++guard > 100) break;   // the spec converges fast; this is belt-and-braces

            var C = A + (A - B) * fA / (fB - fA);
            var fC = F(C);

            if (fC * fB <= 0)
            {
                A = B;
                fA = fB;
            }
            else
            {
                fA /= 2;   // the Illinois adjustment — prevents one-sided stalling
            }

            B = C;
            fB = fC;
        }

        return Math.Exp(A / 2);
    }
}
