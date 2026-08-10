using System.Text;
using NextSkill.Contracts;
using NextSkill.Domain.Matches;

namespace NextSkill.Checks;

/// <summary>
/// Signing and integrity — decisions 009, 012, 035.
///
/// These are the rules that decide whether a bracket may advance. They are pure
/// functions precisely so they can be checked exhaustively without HTTP.
/// </summary>
public static class IntegrityChecks
{
    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        const string secret = "server-secret-abc";
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var body = Encoding.UTF8.GetBytes("""{"contract":1,"seq":1}""");

        // ── signing (012) ────────────────────────────────────────────────
        check("a correctly signed request verifies", () =>
        {
            var sig = RequestSignature.Compute(body, ts, secret);
            that(RequestSignature.Verify(body, sig, ts.ToString(), secret, now) == SignatureResult.Valid,
                "an honest request must be accepted");
        });

        check("a TAMPERED body fails even with a valid-looking signature", () =>
        {
            var sig = RequestSignature.Compute(body, ts, secret);
            var tampered = Encoding.UTF8.GetBytes("""{"contract":1,"seq":9999}""");
            that(RequestSignature.Verify(tampered, sig, ts.ToString(), secret, now) == SignatureResult.Mismatch,
                "the signature covers the body, so altering it must invalidate the signature");
        });

        check("the WRONG SECRET fails", () =>
        {
            var sig = RequestSignature.Compute(body, ts, "some-other-server-secret");
            that(RequestSignature.Verify(body, sig, ts.ToString(), secret, now) == SignatureResult.Mismatch,
                "one server's secret must not authenticate another server's traffic");
        });

        check("a REPLAYED request outside the window is rejected", () =>
        {
            var oldTs = now.AddMinutes(-30).ToUnixTimeSeconds();
            var sig = RequestSignature.Compute(body, oldTs, secret);
            that(RequestSignature.Verify(body, sig, oldTs.ToString(), secret, now) == SignatureResult.Expired,
                "a captured request replayed later must be refused, even though its signature is genuine");
        });

        check("a timestamp implausibly far in the FUTURE is also rejected", () =>
        {
            var futureTs = now.AddMinutes(30).ToUnixTimeSeconds();
            var sig = RequestSignature.Compute(body, futureTs, secret);
            that(RequestSignature.Verify(body, sig, futureTs.ToString(), secret, now) == SignatureResult.Expired,
                "only checking 'too old' would let a forward-dated request stay valid indefinitely");
        });

        check("a missing signature or timestamp is malformed, not merely invalid", () =>
        {
            that(RequestSignature.Verify(body, null, ts.ToString(), secret, now) == SignatureResult.Malformed, "no signature");
            that(RequestSignature.Verify(body, "abc", null, secret, now) == SignatureResult.Malformed, "no timestamp");
            that(RequestSignature.Verify(body, "abc", "not-a-number", secret, now) == SignatureResult.Malformed, "unparseable timestamp");
        });

        // ── sequencing, layer 2 (035) ────────────────────────────────────
        check("sequence: in-order accepted, repeat is an idempotent no-op, gap is detected", () =>
        {
            that(MatchIntegrityRules.CheckSequence(41, 42) == SeqOutcome.Accepted, "42 follows 41");
            that(MatchIntegrityRules.CheckSequence(42, 42) == SeqOutcome.DuplicateIgnored, "a repeat must be ignored, not applied twice");
            that(MatchIntegrityRules.CheckSequence(42, 40) == SeqOutcome.DuplicateIgnored, "an older event must be ignored");
            that(MatchIntegrityRules.CheckSequence(41, 45) == SeqOutcome.GapDetected, "events 42-44 were missed");
        });

        check("duplicates must be EXPECTED, because the plugin retries until acknowledged", () =>
        {
            // The plugin holds events on disk and retries after a timeout (009).
            // If a retry double-counted, every network hiccup would corrupt a score.
            var last = 42L;
            var outcomes = new[] { 43L, 43L, 43L }.Select(seq =>
            {
                var o = MatchIntegrityRules.CheckSequence(last, seq);
                if (o == SeqOutcome.Accepted) last = seq;
                return o;
            }).ToArray();

            that(outcomes[0] == SeqOutcome.Accepted, "first delivery applies");
            that(outcomes[1] == SeqOutcome.DuplicateIgnored && outcomes[2] == SeqOutcome.DuplicateIgnored,
                "retries must be no-ops");
            that(last == 43, "the sequence advanced exactly once");
        });

        // ── reconciliation, layer 3 (035) ────────────────────────────────
        check("reconciliation: agreement with no gaps → CONFIRMED", () =>
        {
            that(MatchIntegrityRules.Reconcile(13, 11, 13, 11, sawGap: false) == MatchIntegrity.Confirmed,
                "events and the authoritative report agree");
        });

        check("reconciliation: MISMATCH → DISPUTED (the silent failure, caught)", () =>
        {
            // This is the bug 035 exists for: a wrong score that looks completely
            // normal. Here it becomes a blocked bracket instead.
            that(MatchIntegrityRules.Reconcile(13, 11, 11, 13, sawGap: false) == MatchIntegrity.Disputed,
                "a reversed result must be disputed");
            that(MatchIntegrityRules.Reconcile(13, 11, 13, 12, sawGap: false) == MatchIntegrity.Disputed,
                "even a one-round difference must be disputed");
        });

        check("reconciliation: scores agree but events were MISSED → SUSPECT, not confirmed", () =>
        {
            // Agreement while knowing we missed events is luck, not evidence.
            // Distinct from Disputed because the two need different human responses.
            that(MatchIntegrityRules.Reconcile(13, 11, 13, 11, sawGap: true) == MatchIntegrity.Suspect,
                "a gap must downgrade confidence even when the totals happen to match");
        });

        // ── the gate itself ──────────────────────────────────────────────
        check("bracket advancement is BLOCKED unless integrity is confirmed", () =>
        {
            that(!MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Disputed, true, true),
                "disputed must never advance, even for a trusted organiser with both teams agreeing");
            that(!MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Suspect, true, true),
                "suspect must never advance either");
        });

        check("a TRUSTED organiser skips team confirmation but NOT the integrity gate", () =>
        {
            that(MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Confirmed, bothTeamsConfirmed: false, organiserTrusted: true),
                "trust skips the confirmation gate (CONTEXT 3b)");
            that(!MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Disputed, bothTeamsConfirmed: false, organiserTrusted: true),
                "trust is about INTENT; integrity is about whether we received the result correctly. Trust must not bypass it");
        });

        check("an untrusted organiser needs both teams to confirm", () =>
        {
            that(!MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Confirmed, bothTeamsConfirmed: false, organiserTrusted: false),
                "no confirmation, no advancement");
            that(MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Confirmed, bothTeamsConfirmed: true, organiserTrusted: false),
                "both teams agreeing is enough");
        });

        check("a referee override can advance anything — and is audited", () =>
        {
            that(MatchIntegrityRules.MayAdvanceBracket(MatchIntegrity.Disputed, false, false, refereeOverride: true),
                "a recorded human decision is the escape hatch (036). Without one, a genuine dispute could deadlock a tournament");
        });
    }
}
