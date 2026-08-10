namespace NextSkill.Domain.Matches;

/// <summary>Integrity state of a match — gates bracket advancement (decision 035).</summary>
public enum MatchIntegrity { Confirmed, Suspect, Disputed }

/// <summary>What to do with an incoming event, based on its sequence number.</summary>
public enum SeqOutcome
{
    /// <summary>Next in sequence. Apply it.</summary>
    Accepted,
    /// <summary>Already seen. Idempotent no-op — this is what makes retries safe.</summary>
    DuplicateIgnored,
    /// <summary>One or more events were missed. The match becomes <c>suspect</c>.</summary>
    GapDetected,
}

/// <summary>
/// The integrity rules from decision 035, as pure functions.
///
/// Kept out of the endpoint on purpose: these are the rules that decide whether
/// a bracket may advance, and they are far easier to trust when they can be
/// exercised without HTTP, a database or a clock.
/// </summary>
public static class MatchIntegrityRules
{
    /// <summary>
    /// Layer 2 — lost, duplicated or reordered events become loud.
    ///
    /// The plugin retries until acknowledged (decision 009), so duplicates are
    /// <b>expected</b>, not exceptional: a retry after a timeout must not
    /// double-count.
    /// </summary>
    public static SeqOutcome CheckSequence(long lastAcceptedSeq, long incomingSeq)
    {
        if (incomingSeq <= lastAcceptedSeq) return SeqOutcome.DuplicateIgnored;
        if (incomingSeq == lastAcceptedSeq + 1) return SeqOutcome.Accepted;
        return SeqOutcome.GapDetected;
    }

    /// <summary>
    /// Layer 3 — reconciliation. The layer that actually fixes the silent failure.
    ///
    /// The panel does <b>not</b> trust its accumulated view, and does not trust
    /// the authoritative block alone either: it compares them. Agreement means
    /// the result is safe to act on. Disagreement means something is wrong, and
    /// "something is wrong" must never advance a bracket.
    /// </summary>
    /// <param name="accumulatedA">Score for team A derived from round events.</param>
    /// <param name="accumulatedB">Score for team B derived from round events.</param>
    /// <param name="authoritativeA">Team A's score in the plugin's final report.</param>
    /// <param name="authoritativeB">Team B's score in the plugin's final report.</param>
    /// <param name="sawGap">Whether any event was missed during the match.</param>
    public static MatchIntegrity Reconcile(
        int accumulatedA, int accumulatedB,
        int authoritativeA, int authoritativeB,
        bool sawGap)
    {
        if (authoritativeA != accumulatedA || authoritativeB != accumulatedB)
            return MatchIntegrity.Disputed;

        // The scores agree, but we know we missed events — so the agreement is
        // luck rather than evidence. Still blocked, but distinguishable from a
        // genuine contradiction, because the two need different human responses.
        if (sawGap)
            return MatchIntegrity.Suspect;

        return MatchIntegrity.Confirmed;
    }

    /// <summary>
    /// Whether a bracket may advance from this result.
    ///
    /// A trusted organiser skips the team-confirmation gate, but <b>never</b> the
    /// integrity gate — trust is about intent, and integrity is about whether we
    /// received the result correctly at all (decision 007, CONTEXT §3b).
    /// </summary>
    public static bool MayAdvanceBracket(
        MatchIntegrity integrity,
        bool bothTeamsConfirmed,
        bool organiserTrusted,
        bool refereeOverride = false)
    {
        // An explicit, audited human decision can advance anything (036).
        if (refereeOverride) return true;

        if (integrity != MatchIntegrity.Confirmed) return false;

        return organiserTrusted || bothTeamsConfirmed;
    }
}
