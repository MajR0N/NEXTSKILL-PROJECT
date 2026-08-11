using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Ratings;
using NextSkill.Domain.Teams;
using DbGame = NextSkill.Api.Shared.Persistence.Game;
using NextSkill.Domain.Tournaments;
using DbFormat = NextSkill.Domain.Tournaments.TournamentFormat;

namespace NextSkill.Checks;

/// <summary>
/// Completeness checks — every enum value must have a decided answer.
///
/// These catch the thing code review misses. When someone adds an enum member,
/// each individual change looks reasonable in isolation; nobody notices that a
/// question went unanswered. Iterating the enum is what notices.
///
/// The pattern: for each value, assert an explicit decision exists. A new member
/// then <b>fails the build</b> until somebody decides, rather than defaulting to
/// whatever the code happens to do.
/// </summary>
public static class CompletenessChecks
{
    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        // ── every operation must be reachable by SOMEBODY ────────────────
        check("every Operation is grantable by some role (no dead operations)", () =>
        {
            // The mirror image of "a roleless player can do nothing": an
            // operation nobody can perform is equally a bug — usually a rule
            // that was tightened until it locked everyone out.
            var org = Guid.NewGuid();
            var team = Guid.NewGuid();

            var candidates = new Actor[]
            {
                new(Guid.NewGuid(), Platform: PlatformRole.Owner),
                new(Guid.NewGuid(), Platform: PlatformRole.Staff),
                new(Guid.NewGuid(), Platform: PlatformRole.Support),
                new(Guid.NewGuid(), Organisers: new Dictionary<Guid, OrganiserRole> { [org] = OrganiserRole.Owner }),
                new(Guid.NewGuid(), Organisers: new Dictionary<Guid, OrganiserRole> { [org] = OrganiserRole.Admin }),
                new(Guid.NewGuid(), Organisers: new Dictionary<Guid, OrganiserRole> { [org] = OrganiserRole.Referee }),
                new(Guid.NewGuid(), Teams: new Dictionary<Guid, TeamRole> { [team] = TeamRole.Captain }),
                new(Guid.NewGuid(), RestorableTeams: new HashSet<Guid> { team }),
            };

            // ReadSecret is the one deliberate exception: it exists precisely so
            // the denial is explicit and testable (007, 012).
            var deliberatelyUngrantable = new[] { Operation.ReadSecret };

            foreach (var op in Enum.GetValues<Operation>())
            {
                var reachable = candidates.Any(a => AccessPolicy.Can(a, op, new ResourceRef(org, team)));
                if (deliberatelyUngrantable.Contains(op))
                    that(!reachable, $"{op} must remain ungrantable to everyone");
                else
                    that(reachable, $"{op} can be performed by NOBODY — either a role is missing or the rule locked everyone out");
            }
        });

        // ── every integrity state must have a decided bracket answer ─────
        check("every MatchIntegrity state has a decided bracket-advancement answer", () =>
        {
            foreach (var state in Enum.GetValues<MatchIntegrity>())
            {
                var allowed = MatchIntegrityRules.MayAdvanceBracket(state, bothTeamsConfirmed: true, organiserTrusted: true);
                var expected = state == MatchIntegrity.Confirmed;
                that(allowed == expected,
                    $"{state}: expected advancement={expected}, got {allowed}. A new integrity state must not default to advancing");
            }
        });

        // NOTE: a drift check between a persistence copy and a domain copy of
        // MatchIntegrity used to live here. Writing it revealed that the enum was
        // genuinely defined twice — so the better fix was to delete one
        // definition rather than to monitor both. Drift is now impossible, and a
        // check for it would be theatre.

        // ── every game must have decided capabilities ────────────────────
        check("every Game has a decided answer for demos and identity binding", () =>
        {
            // 001 and 024: CS2 has GOTV, CS 1.6 has no server-side demo pipeline.
            // 010: CS2 SteamIDs are trustworthy, CS 1.6 ones are spoofable.
            // A third game added later must answer both before it can ship.
            var demoSupport = new Dictionary<DbGame, bool> { [DbGame.Cs2] = true, [DbGame.Cs16] = false };
            var needsTwoFactorBinding = new Dictionary<DbGame, bool> { [DbGame.Cs2] = false, [DbGame.Cs16] = true };

            foreach (var game in Enum.GetValues<DbGame>())
            {
                that(demoSupport.ContainsKey(game), $"{game}: nobody decided whether it has a demo pipeline (001, 024)");
                that(needsTwoFactorBinding.ContainsKey(game), $"{game}: nobody decided whether SteamIDs can be trusted (010, 011)");
            }
        });

        // ── every roster slot must have a decided size and a decided answer
        //    to "does this person play?" ─────────────────────────────────
        check("every RosterSlot has a decided capacity and a decided fieldable answer", () =>
        {
            // Adding "Analyst" or "Manager" later must not silently inherit
            // whatever the last switch branch returned. Capacity() falls through
            // to zero and IsFieldable() to false, so a new slot is unusable
            // until somebody decides — and this check says so out loud (045).
            var decided = new Dictionary<RosterSlot, (int Capacity, bool Fieldable)>
            {
                [RosterSlot.Player] = (5, true),
                [RosterSlot.Substitute] = (3, true),
                [RosterSlot.Coach] = (2, false),
            };

            foreach (var slot in Enum.GetValues<RosterSlot>())
            {
                that(decided.ContainsKey(slot),
                    $"{slot}: nobody decided how many places it has or whether they play (045)");

                var (capacity, fieldable) = decided[slot];
                that(RosterRules.Capacity(slot) == capacity,
                    $"{slot}: capacity is {RosterRules.Capacity(slot)}, the decision says {capacity}");
                that(RosterRules.IsFieldable(slot) == fieldable,
                    $"{slot}: fieldable is {RosterRules.IsFieldable(slot)}, the decision says {fieldable}");
            }

            that(decided.Values.Sum(x => x.Capacity) == RosterRules.MaxTotal,
                $"the per-slot caps must sum to MaxTotal ({RosterRules.MaxTotal})");

            // The rule that gives a coach teeth: a lineup that could include the
            // coach would be a 5v5 played 4v5, discovered at map start (011, 035).
            that(!RosterRules.IsFieldable(RosterSlot.Coach), "a coach must never be fieldable");
        });

        // ── every format must be implemented or explicitly deferred ──────
        check("every TournamentFormat is implemented or explicitly deferred", () =>
        {
            // 022 committed to all five. Three are not built yet — which is fine,
            // as long as it is a recorded decision rather than an oversight.
            var implemented = new[] { DbFormat.SingleElimination, DbFormat.DoubleElimination };
            var deferred = new[] { DbFormat.RoundRobin, DbFormat.GroupStage, DbFormat.League };

            foreach (var format in Enum.GetValues<DbFormat>())
                that(implemented.Contains(format) || deferred.Contains(format),
                    $"{format} is neither implemented nor listed as deferred — decide before it can be selected");

            // And the implemented ones must actually generate.
            foreach (var format in implemented)
            {
                var bracket = format == DbFormat.SingleElimination
                    ? BracketGenerator.GenerateSingleElimination(8)
                    : BracketGenerator.GenerateDoubleElimination(8);
                that(bracket.Matches.Count > 0, $"{format} claims to be implemented but generates nothing");
            }
        });

        // ── every exclusion reason must be expressible ───────────────────
        check("every rating exclusion reason produces a human-readable explanation", () =>
        {
            // An organiser asking "why didn't this count?" deserves an answer,
            // not a bare false (025).
            var cases = new (string Label, MatchFacts Facts)[]
            {
                ("bye",       new MatchFacts(true,  ResultIntegrity.Confirmed, true)),
                ("suspect",   new MatchFacts(false, ResultIntegrity.Suspect,   true)),
                ("disputed",  new MatchFacts(false, ResultIntegrity.Disputed,  true)),
                ("window",    new MatchFacts(false, ResultIntegrity.Confirmed, false)),
                ("forfeit",   new MatchFacts(false, ResultIntegrity.Confirmed, true, IsForfeit: true)),
            };

            foreach (var (label, facts) in cases)
            {
                var reason = RatingPolicy.ExcludedBecause(facts);
                that(!string.IsNullOrWhiteSpace(reason), $"{label}: excluded with no explanation");
            }

            foreach (var integrity in Enum.GetValues<ResultIntegrity>())
            {
                var facts = new MatchFacts(false, integrity, true);
                var rated = RatingPolicy.IsRated(facts);
                that(rated == (integrity == ResultIntegrity.Confirmed),
                    $"{integrity}: a new integrity state must not default to being rated");
            }
        });

        // ── every locale must be complete ────────────────────────────────
        check("every supported locale has an Open Graph mapping", () =>
        {
            foreach (var locale in NextSkill.Api.Localisation.Locales.Supported)
            {
                var og = NextSkill.Api.Localisation.Locales.OpenGraph(locale);
                that(og.Contains('_'), $"{locale}: og:locale '{og}' is not in the underscore form crawlers expect");
                that(og.StartsWith(locale, StringComparison.OrdinalIgnoreCase) || locale == "en",
                    $"{locale}: og:locale '{og}' does not correspond to the locale");
            }
        });
    }
}
