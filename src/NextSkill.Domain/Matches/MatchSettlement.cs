namespace NextSkill.Domain.Matches;

/// <summary>
/// When a received result becomes a <b>final</b> one (decisions 007, 050).
///
/// A match has two endings and they are not the same event. <i>Result
/// received</i> is what <see cref="MatchIntegrityRules"/> reconciles, and it
/// moves the bracket. <i>Result final</i> is what this decides, and it is the
/// only thing that may move a rating — 025 forbids rating at match end
/// precisely so a change never has to be reversed.
/// </summary>
public static class MatchSettlement
{
    /// <summary>
    /// How long a captain has to dispute before the result is deemed accepted.
    ///
    /// <b>Two hours</b>, decided 2026-08-17 against 007's open question. A
    /// Saturday-night cup settles before everyone goes to bed, which is when the
    /// screenshots get posted; a rating that arrives on Sunday evening reads as
    /// made up. It is also the only branch that ever waits — both captains
    /// agreeing closes it immediately, and a trusted organiser skips it.
    /// </summary>
    public static readonly TimeSpan ChallengeWindow = TimeSpan.FromHours(2);

    /// <summary>
    /// Whether the challenge period is over, by agreement or by expiry.
    /// </summary>
    /// <param name="nobodyCanConfirm">
    /// A bye or a forfeit. A team that did not show up cannot click a button, so
    /// waiting for its confirmation would wait forever (050).
    /// </param>
    public static bool WindowClosed(
        DateTimeOffset endedAt,
        DateTimeOffset now,
        bool bothCaptainsConfirmed,
        bool organiserTrusted,
        bool nobodyCanConfirm) =>
        bothCaptainsConfirmed
        || organiserTrusted
        || nobodyCanConfirm
        || now >= endedAt + ChallengeWindow;
}
