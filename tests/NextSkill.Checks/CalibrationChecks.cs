using NextSkill.Domain.Integrity;

namespace NextSkill.Checks;

/// <summary>
/// V14 arithmetic — CONTEXT §2.12, decision 058 §6c.
///
/// These pin the numbers the documents quote. If someone later "simplifies" the
/// calibration maths, the docs and the code stop agreeing silently, and the
/// consequence of that is banning innocent players.
/// </summary>
public static class CalibrationChecks
{
    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        check("judging everyone: a good detector still bans mostly innocents", () =>
        {
            // p=2%, recall 90%, FPR 1% -> 64.7%. The number 058 quotes.
            var precision = DetectionCalibration.Precision(0.02, 0.90, 0.01);
            that(Math.Abs(precision - 0.647) < 0.001,
                $"expected ~64.7% precision over the whole population, got {precision:P1}");
            that(precision < 0.70,
                "the whole point: over a third of these bans are wrong, so this must not read as safe");
        });

        check("screening the pool first makes the SAME detector safe", () =>
        {
            // Identical recall and FPR. Only the pool changed, because the meter filtered it.
            var unscreened = DetectionCalibration.Precision(0.02, 0.90, 0.01);
            var screened = DetectionCalibration.Precision(0.50, 0.90, 0.01);

            that(Math.Abs(screened - 0.989) < 0.001,
                $"expected ~98.9% precision on a screened pool, got {screened:P1}");
            that(screened > unscreened + 0.30,
                "the suspicion meter must be worth far more than a rounding difference, or it is decoration");
        });

        check("population-wide auto-ban needs a false-positive rate of about 1 in 5400", () =>
        {
            var maxFpr = DetectionCalibration.MaxFalsePositiveRate(0.02, 0.90, 0.99);
            var oneIn = 1.0 / maxFpr;
            that(Math.Abs(oneIn - 5389) < 5, $"expected ~1 in 5389, got 1 in {oneIn:F0}");
        });

        check("screening relaxes that requirement by roughly fifty times", () =>
        {
            // 1.86e-4 unscreened vs 9.09e-3 screened: about 49x. The gap is the whole
            // argument for the monitoring phase, so it is pinned rather than described.
            var wide = DetectionCalibration.MaxFalsePositiveRate(0.02, 0.90, 0.99);
            var screened = DetectionCalibration.MaxFalsePositiveRate(0.50, 0.90, 0.99);
            var ratio = screened / wide;
            that(ratio > 40 && ratio < 60,
                $"expected screening to loosen the requirement ~49x, got {ratio:F1}x ({wide:E2} vs {screened:E2})");
        });

        check("no false positives yet is not evidence until the sample is large", () =>
        {
            // The trap this exists to catch: 50 clean flags observed proves almost nothing.
            var needed = DetectionCalibration.CleanObservationsNeeded(1.0 / 5389);
            that(needed >= 16_000,
                $"claiming 1-in-5389 needs ~16k clean observations, calculated {needed}");

            // Screened, the bar is reachable in a season rather than a decade.
            var screenedNeeded = DetectionCalibration.CleanObservationsNeeded(0.01);
            that(screenedNeeded == 300, $"expected 300 clean observations, got {screenedNeeded}");
        });

        check("degenerate inputs do not silently produce a licence to ban", () =>
        {
            // A detector that flags nobody has banned nobody wrongly. It must not report 0%.
            that(DetectionCalibration.Precision(0.0, 0.0, 0.0) == 1.0,
                "flagging nobody must not read as 'every ban was wrong'");

            var threw = false;
            try { DetectionCalibration.Precision(1.5, 0.9, 0.01); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            that(threw, "a prevalence above 1 must throw, not quietly return a plausible number");
        });
    }
}
