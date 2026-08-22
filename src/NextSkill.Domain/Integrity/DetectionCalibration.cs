namespace NextSkill.Domain.Integrity;

/// <summary>
/// The arithmetic behind V14 (CONTEXT §2.12, decision 058 §6c).
///
/// The question "does the anti-cheat decide 65% of cases?" measures COVERAGE.
/// Coverage is not what gates an automatic ban — PRECISION is, and precision is
/// not a property of the detector. It depends on how common cheating is in the
/// pool being judged.
///
/// The same detector that ruins one player in three when applied to everybody
/// is safe when applied to a screened pool. That is what the suspicion meter is
/// for: it is a prevalence filter, and prevalence is what makes an automatic
/// ban defensible at all.
/// </summary>
public static class DetectionCalibration
{
    /// <summary>
    /// Of everyone banned, the fraction who actually cheated.
    /// </summary>
    /// <param name="prevalence">Share of the judged pool who are actually cheating.</param>
    /// <param name="recall">Share of cheaters the detector catches.</param>
    /// <param name="falsePositiveRate">Share of clean players the detector flags.</param>
    public static double Precision(double prevalence, double recall, double falsePositiveRate)
    {
        Require(prevalence, nameof(prevalence));
        Require(recall, nameof(recall));
        Require(falsePositiveRate, nameof(falsePositiveRate));

        var truePositives = prevalence * recall;
        var falsePositives = (1 - prevalence) * falsePositiveRate;
        var flagged = truePositives + falsePositives;

        // Nothing flagged at all: no bans, so no wrong ones. Reporting 0 here would
        // read as "every ban was wrong", which is the opposite of what happened.
        return flagged == 0 ? 1.0 : truePositives / flagged;
    }

    /// <summary>
    /// The false-positive rate the detector must not exceed to reach
    /// <paramref name="requiredPrecision"/> at this prevalence and recall.
    ///
    /// Returns <see cref="double.PositiveInfinity"/> when the requirement is met at any
    /// rate — i.e. the constraint does not bind.
    /// </summary>
    public static double MaxFalsePositiveRate(double prevalence, double recall, double requiredPrecision)
    {
        Require(prevalence, nameof(prevalence));
        Require(recall, nameof(recall));
        Require(requiredPrecision, nameof(requiredPrecision));

        if (requiredPrecision == 0) return double.PositiveInfinity;
        if (prevalence == 1) return double.PositiveInfinity;   // no clean players to misjudge

        var truePositives = prevalence * recall;
        return truePositives * (1 - requiredPrecision) / (requiredPrecision * (1 - prevalence));
    }

    /// <summary>
    /// How many clean observations must pass without a single false positive before we
    /// may claim the rate is at or below <paramref name="targetRate"/>.
    ///
    /// Uses the rule of three: zero events in n trials gives a 95% upper bound of ~3/n.
    /// This is the check that stops "we have had no false positives yet" from being
    /// mistaken for evidence — with 50 clean flags observed, the true rate could still
    /// be 1 in 17.
    ///
    /// ponytail: rule of three, not an exact Clopper-Pearson bound. Ceiling: it is
    /// only valid for ZERO observed events and only at 95%. Upgrade path is a real
    /// binomial interval, worth it once any false positive is actually observed —
    /// at which point this returns nothing useful anyway.
    /// </summary>
    public static long CleanObservationsNeeded(double targetRate)
    {
        if (targetRate <= 0 || targetRate > 1)
            throw new ArgumentOutOfRangeException(nameof(targetRate), "Target rate must be in (0, 1].");

        return (long)Math.Ceiling(3.0 / targetRate);
    }

    private static void Require(double value, string name)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(name, "Must be a probability in [0, 1].");
    }
}
