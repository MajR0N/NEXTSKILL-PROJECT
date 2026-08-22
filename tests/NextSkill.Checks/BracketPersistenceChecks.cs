using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Tournaments;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Checks;

/// <summary>
/// Persisting a generated bracket (decisions 023, 046).
///
/// The engine has been tested since Phase 2, but it had <b>never run on live
/// data</b>. Everything here is about the seam: do the generator's per-bracket
/// integers survive translation into real rows, do the routing foreign keys
/// resolve, and do byes advance the right team into the right slot.
///
/// A bracket that is wrong here does not look wrong. It renders perfectly and
/// the mistake appears in the final.
/// </summary>
public static class BracketPersistenceChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        var stamp = DateTimeOffset.UtcNow.Ticks;
        var orgId = Guid.NewGuid();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(orgId));

        var teamIds = new List<Guid>();

        using (var db = Db())
        {
            db.Organisers.Add(new Organiser
            {
                Id = orgId, Name = $"BrOrg {stamp}", Slug = $"br-{stamp}", CreatedAt = DateTimeOffset.UtcNow,
            });

            for (var i = 0; i < 16; i++)
            {
                var id = Guid.NewGuid();
                teamIds.Add(id);
                db.Teams.Add(new Team
                {
                    Id = id, Name = $"BT{stamp}n{i}", Tag = $"B{stamp % 10000}{i:D2}",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
            db.SaveChanges();
        }

        /// Creates a tournament with `teamCount` seeded entries and builds its bracket.
        Guid BuildFor(int teamCount, TournamentFormat format)
        {
            var tournamentId = Guid.NewGuid();
            using var db = Db();

            db.Tournaments.Add(new Tournament
            {
                Id = tournamentId, OrganiserId = orgId, Name = $"Cup {stamp} {teamCount} {format}",
                Game = Game.Cs2, Format = format, TeamSize = TeamSize.Squad,
                Status = TournamentStatus.RegistrationClosed, MaxTeams = teamCount,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            for (var i = 0; i < teamCount; i++)
                db.Registrations.Add(new TournamentRegistration
                {
                    Id = Guid.NewGuid(), OrganiserId = orgId, TournamentId = tournamentId,
                    TeamId = teamIds[i], Seed = i + 1, RegisteredAt = DateTimeOffset.UtcNow,
                });

            db.SaveChanges();

            var tournament = db.Tournaments.Single(t => t.Id == tournamentId);
            BracketBuilder.BuildAsync(db, tournament).GetAwaiter().GetResult();
            return tournamentId;
        }

        Guid singleId = default, doubleId = default, byeId = default;

        check("bracket: an 8-team single elimination writes 7 matches with the right shape", () =>
        {
            singleId = BuildFor(8, TournamentFormat.SingleElimination);

            using var db = Db();
            var matches = db.Matches.AsNoTracking().Where(m => m.TournamentId == singleId).ToList();

            that(matches.Count == 7, $"expected 7 matches, got {matches.Count}");
            that(matches.All(m => m.Side == BracketSide.Upper), "single elimination has no lower bracket");
            that(matches.Count(m => m.Round == 1) == 4, "four quarter-finals");
            that(matches.Count(m => m.Round == 3) == 1, "one final");
            that(matches.All(m => m.Status == MatchStatus.Scheduled || m.IsBye), "nothing is played yet");
        });

        // ── the check that matters most ──────────────────────────────────
        check("bracket: EVERY routing link resolves to a real match in the same tournament", () =>
        {
            // In Phase 2 this failed in memory: upper round 2's losers routed
            // nowhere and round 1's routed twice, and the bracket still rendered
            // perfectly. Now the database enforces existence — but not that the
            // link points somewhere SENSIBLE, which is what this asserts.
            doubleId = BuildFor(8, TournamentFormat.DoubleElimination);

            using var db = Db();
            var matches = db.Matches.AsNoTracking().Where(m => m.TournamentId == doubleId).ToList();
            var byId = matches.ToDictionary(m => m.Id);

            that(matches.Count == 15, $"8-team double elim is 7 + 6 + 2 = 15 matches, got {matches.Count}");

            foreach (var m in matches)
            {
                if (m.WinnerToMatchId is { } w)
                {
                    that(byId.ContainsKey(w), $"match {m.Round}/{m.Position} routes its winner outside the tournament");
                    that(m.WinnerToSlot is not null, "a routing link without a slot cannot place anybody");
                    that(byId[w].TournamentId == doubleId, "routing must never cross tournaments");
                }

                if (m.LoserToMatchId is { } l)
                {
                    that(byId.ContainsKey(l), $"match {m.Round}/{m.Position} routes its loser outside the tournament");
                    that(m.LoserToSlot is not null, "a routing link without a slot cannot place anybody");
                }

                that(m.WinnerToMatchId != m.Id && m.LoserToMatchId != m.Id, "a match must not route into itself");
            }

            // The Phase 2 symptom, asserted directly against the database.
            foreach (var m in matches.Where(m => m.Side == BracketSide.Upper && m.Round < 3))
                that(m.LoserToMatchId is not null,
                    $"upper match {m.Round}/{m.Position} drops its loser nowhere — that is single elimination wearing a double-elim bracket");

            // No two matches may claim the same destination slot, or one team
            // silently overwrites another.
            var destinations = matches
                .SelectMany(m => new[]
                {
                    m.WinnerToMatchId is { } w ? (w, m.WinnerToSlot) : default,
                    m.LoserToMatchId is { } l ? (l, m.LoserToSlot) : default,
                })
                .Where(d => d != default)
                .ToList();

            that(destinations.Distinct().Count() == destinations.Count,
                "two matches feeding one slot means one team silently disappears");
        });

        check("bracket: the grand-final RESET exists from generation, marked conditional", () =>
        {
            using var db = Db();
            var finals = db.Matches.AsNoTracking()
                .Where(m => m.TournamentId == doubleId && m.Side == BracketSide.GrandFinal).ToList();

            that(finals.Count == 2, $"grand final + reset, got {finals.Count}");
            that(finals.Count(m => m.IsConditional) == 1,
                "exactly one of them is the reset — it exists up front so the most commonly forgotten rule cannot be forgotten (022)");
        });

        check("bracket: seeded slots resolve to REAL teams, and seed 1 meets the lowest seed", () =>
        {
            using var db = Db();
            var round1 = db.Matches.AsNoTracking()
                .Where(m => m.TournamentId == singleId && m.Round == 1)
                .OrderBy(m => m.Position).ToList();

            that(round1.All(m => m.TeamAId != null && m.TeamBId != null),
                "an 8-team bracket has no byes, so every first-round slot must be filled");

            // Seeding is the point: 1 plays 8, and 1 and 2 cannot meet early.
            var seeds = db.Registrations.AsNoTracking()
                .Where(r => r.TournamentId == singleId)
                .ToDictionary(r => r.TeamId, r => r.Seed!.Value);

            var first = round1[0];
            var pair = new[] { seeds[first.TeamAId!.Value], seeds[first.TeamBId!.Value] }.Order().ToArray();
            that(pair[0] == 1 && pair[1] == 8, $"seed 1 should meet seed 8, got {pair[0]} vs {pair[1]}");
        });

        // ── byes: the free win, resolved at generation ───────────────────
        check("bracket: a 13-team bracket gives BYES to top seeds and advances them immediately", () =>
        {
            byeId = BuildFor(13, TournamentFormat.SingleElimination);

            using var db = Db();
            var matches = db.Matches.AsNoTracking().Where(m => m.TournamentId == byeId).ToList();
            var seeds = db.Registrations.AsNoTracking()
                .Where(r => r.TournamentId == byeId).ToDictionary(r => r.TeamId, r => r.Seed!.Value);

            that(matches.Count == 15, $"a 16-slot bracket is 15 matches, got {matches.Count}");

            var byes = matches.Where(m => m.IsBye).ToList();
            that(byes.Count == 3, $"16 - 13 = 3 byes, got {byes.Count}");

            foreach (var bye in byes)
            {
                that(bye.WinnerTeamId is not null, "a bye must be resolved at generation, not left for someone to click");
                that(bye.Status == MatchStatus.Completed, "a walkover is over before it starts");
                that(bye.ScoreA is null && bye.ScoreB is null,
                    "a bye has no score — and it must never be rated, because a free win cannot move a rating (025)");

                var occupant = seeds[bye.WinnerTeamId!.Value];
                that(occupant is >= 1 and <= 3, $"the bye went to seed {occupant}; the reward for seeding high is the free round");
            }
        });

        check("bracket: a bye ADVANCES ITS TEAM into the correct slot of the next match", () =>
        {
            // The routing seam, exercised for real. If the slot were wrong the
            // team would still appear in round two — just against the wrong
            // opponent, which nobody notices until it is too late.
            using var db = Db();
            var matches = db.Matches.AsNoTracking().Where(m => m.TournamentId == byeId).ToList();
            var byId = matches.ToDictionary(m => m.Id);

            foreach (var bye in matches.Where(m => m.IsBye))
            {
                that(bye.WinnerToMatchId is not null, "a bye still has to send its team onward");

                var next = byId[bye.WinnerToMatchId!.Value];
                var landed = bye.WinnerToSlot == SlotRef.A ? next.TeamAId : next.TeamBId;

                that(landed == bye.WinnerTeamId,
                    $"the bye winner should be in slot {bye.WinnerToSlot} of the next match, found {landed}");
            }
        });

        check("bracket: ADVANCEMENT IS REFUSED for a match that is not confirmed", () =>
        {
            // The integrity gate, at the one place advancement happens (035).
            using var db = Db();
            var match = db.Matches.First(m => m.TournamentId == singleId && !m.IsBye && m.Round == 1);
            match.Integrity = MatchIntegrity.Disputed;
            db.SaveChanges();

            var threw = false;
            try
            {
                BracketBuilder.AdvanceAsync(db, match, match.TeamAId!.Value).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            that(threw, "a disputed result must not move anybody through the bracket");

            using var after = Db();
            var reloaded = after.Matches.AsNoTracking().Single(m => m.Id == match.Id);
            that(reloaded.WinnerTeamId is null, "and no winner may be recorded either");
        });

        check("bracket: a winner that did not play in the match is refused", () =>
        {
            using var db = Db();
            var match = db.Matches.First(m => m.TournamentId == singleId && !m.IsBye && m.Round == 1
                                              && m.Integrity == MatchIntegrity.Confirmed);

            var threw = false;
            try
            {
                BracketBuilder.AdvanceAsync(db, match, Guid.NewGuid()).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            that(threw, "the winner must be one of the two teams in the match");
        });

        check("bracket: an event FORFEIT advances the opponent and propagates a vacancy", () =>
        {
            var forfeitId = BuildFor(4, TournamentFormat.DoubleElimination);
            var actorId = Guid.NewGuid();

            using var db = Db();
            var upper = db.Matches
                .Where(m => m.TournamentId == forfeitId && m.Side == BracketSide.Upper && m.Round == 1)
                .OrderBy(m => m.Position)
                .ToList();
            var forfeitedMatch = upper[0];
            var otherMatch = upper[1];
            var exitingTeam = forfeitedMatch.TeamAId!.Value;
            var opponent = forfeitedMatch.TeamBId!.Value;
            var lowerMatchId = forfeitedMatch.LoserToMatchId!.Value;

            that(otherMatch.LoserToMatchId == lowerMatchId,
                "the two upper semi-final losers should feed the same first lower match");

            BracketBuilder.ForfeitAsync(
                db, forfeitedMatch, exitingTeam, actorId, "Captain confirmed the event exit.")
                .GetAwaiter().GetResult();

            db.ChangeTracker.Clear();
            var decided = db.Matches.AsNoTracking().Single(m => m.Id == forfeitedMatch.Id);
            var registration = db.Registrations.AsNoTracking()
                .Single(r => r.TournamentId == forfeitId && r.TeamId == exitingTeam);
            var lowerWaiting = db.Matches.AsNoTracking().Single(m => m.Id == lowerMatchId);

            that(decided.Status == MatchStatus.Forfeited && decided.WinnerTeamId == opponent,
                "the opponent wins the administrative result");
            that(decided.ScoreA is null && decided.ScoreB is null,
                "a forfeit has no invented score and cannot be rated");
            that(decided.ForfeitedTeamId == exitingTeam && decided.ForfeitedByAccountId == actorId,
                "the match keeps who exited and who recorded it");
            that(registration.ForfeitedAt is not null && registration.ForfeitedByAccountId == actorId,
                "the registration records the event-level exit, not merely one lost match");
            that((forfeitedMatch.LoserToSlot == SlotRef.A && lowerWaiting.SlotAResolved && lowerWaiting.TeamAId is null) ||
                 (forfeitedMatch.LoserToSlot == SlotRef.B && lowerWaiting.SlotBResolved && lowerWaiting.TeamBId is null),
                "the lower path receives a resolved vacancy instead of the exiting team");

            var normalWinner = otherMatch.TeamAId!.Value;
            var survivingLoser = otherMatch.TeamBId!.Value;
            var trackedOther = db.Matches.Single(m => m.Id == otherMatch.Id);
            BracketBuilder.AdvanceAsync(db, trackedOther, normalWinner).GetAwaiter().GetResult();

            db.ChangeTracker.Clear();
            var lowerResolved = db.Matches.AsNoTracking().Single(m => m.Id == lowerMatchId);
            that(lowerResolved.IsBye && lowerResolved.Status == MatchStatus.Completed &&
                 lowerResolved.WinnerTeamId == survivingLoser,
                "when the other loser arrives, the vacancy becomes an immediate unrated bye");
            that(!db.Matches.AsNoTracking().Any(m =>
                    m.TournamentId == forfeitId && m.Id != forfeitedMatch.Id &&
                    (m.TeamAId == exitingTeam || m.TeamBId == exitingTeam)),
                "an event forfeit must never drop the team into the lower bracket");
        });

        // ── the reset, live (023) ────────────────────────────────────────
        //
        // The engine has triggered this since Phase 2 and the row has existed
        // since generation — and nothing ever connected the two.

        /// Advances every ready match except the grand final, slot A always winning.
        void PlayUpTo(Guid tournamentId)
        {
            using var db = Db();
            for (var guard = 0; ; guard++)
            {
                if (guard > 200) throw new Exception("The bracket did not drain — routing is probably cyclic.");

                var next = db.Matches.FirstOrDefault(m =>
                    m.TournamentId == tournamentId && m.Side != BracketSide.GrandFinal &&
                    m.WinnerTeamId == null && !m.IsConditional && m.Status == MatchStatus.Scheduled &&
                    m.SlotAResolved && m.SlotBResolved && m.TeamAId != null && m.TeamBId != null);

                if (next is null) return;

                BracketBuilder.AdvanceAsync(db, next, next.TeamAId!.Value).GetAwaiter().GetResult();
                db.ChangeTracker.Clear();
            }
        }

        (Match GrandFinal, Match Reset) FinalsOf(NextSkillDb db, Guid tournamentId)
        {
            var finals = db.Matches
                .Where(m => m.TournamentId == tournamentId && m.Side == BracketSide.GrandFinal)
                .ToList();
            return (finals.Single(m => !m.IsConditional), finals.Single(m => m.IsConditional));
        }

        check("BRACKET RESET, LIVE: the lower-bracket team wins the grand final → the reset is armed", () =>
        {
            var id = BuildFor(4, TournamentFormat.DoubleElimination);
            PlayUpTo(id);

            using var db = Db();
            var (grandFinal, reset) = FinalsOf(db, id);

            that(grandFinal.TeamAId is not null && grandFinal.TeamBId is not null,
                "the grand final must have both finalists before this proves anything");
            that(reset.TeamAId is null && reset.TeamBId is null && !reset.SlotAResolved,
                "and the reset must still be empty");

            var lowerBracketTeam = grandFinal.TeamBId!.Value;
            var upperBracketTeam = grandFinal.TeamAId!.Value;
            BracketBuilder.AdvanceAsync(db, grandFinal, lowerBracketTeam).GetAwaiter().GetResult();

            db.ChangeTracker.Clear();
            var armed = FinalsOf(db, id).Reset;

            // Without this the undefeated team is eliminated on its first
            // defeat, which is not double elimination at all (022).
            that(armed.TeamAId == upperBracketTeam && armed.TeamBId == lowerBracketTeam,
                "both finalists now have one loss, so the SAME two must play again");
            that(armed.SlotAResolved && armed.SlotBResolved,
                "and the match must be playable, not merely populated");
            that(armed.IsConditional,
                "it stays flagged as the reset — that is what the bracket renders differently");
        });

        check("bracket: the UPPER-bracket team winning ends it — no reset is invented", () =>
        {
            var id = BuildFor(4, TournamentFormat.DoubleElimination);
            PlayUpTo(id);

            using var db = Db();
            var (grandFinal, _) = FinalsOf(db, id);
            BracketBuilder.AdvanceAsync(db, grandFinal, grandFinal.TeamAId!.Value).GetAwaiter().GetResult();

            db.ChangeTracker.Clear();
            var reset = FinalsOf(db, id).Reset;
            that(reset.TeamAId is null && reset.TeamBId is null && !reset.SlotAResolved,
                "an undefeated team that wins has beaten everyone twice — a second final would be inventing one");
        });

        check("bracket: a FORFEITED grand final never summons a team back for a reset", () =>
        {
            var id = BuildFor(4, TournamentFormat.DoubleElimination);
            PlayUpTo(id);

            using var db = Db();
            var (grandFinal, _) = FinalsOf(db, id);

            // The upper-bracket team exits the event, so the lower-bracket team
            // "wins" — the same shape that arms the reset from a played result.
            BracketBuilder.ForfeitAsync(
                db, grandFinal, grandFinal.TeamAId!.Value, Guid.NewGuid(), "Nie stawili się.")
                .GetAwaiter().GetResult();

            db.ChangeTracker.Clear();
            var reset = FinalsOf(db, id).Reset;
            that(reset.TeamAId is null && reset.TeamBId is null,
                "a team that left the event cannot be put back into a final it cannot play");
        });

        check("bracket: mobile shows ONE round, defaults to the one in play, clamps — and follows a team", () =>
        {
            var id = BuildFor(4, TournamentFormat.DoubleElimination);
            using (var db = Db())
            {
                db.Tournaments.Single(t => t.Id == id).Status = TournamentStatus.Running;
                db.SaveChanges();
            }

            using var factory = TestHost.Create();
            using var browser = factory.NewClient();

            int Occurrences(string html, string needle)
            {
                var count = 0;
                for (var at = html.IndexOf(needle, StringComparison.Ordinal);
                     at >= 0;
                     at = html.IndexOf(needle, at + 1, StringComparison.Ordinal))
                    count++;
                return count;
            }

            /// Which column carries the marker, counted in render order.
            int ShownIndex(string html)
            {
                var marker = html.IndexOf("bracket__col--shown", StringComparison.Ordinal);
                if (marker < 0) return -1;
                return Occurrences(html[..marker], "class=\"bracket__col") - 1;
            }

            var first = browser.GetStringAsync($"/pl/t/{id}/bracket").GetAwaiter().GetResult();
            that(Occurrences(first, "bracket__col--shown") == 1,
                "a phone gets exactly one round — the tree is a desktop artifact (023)");

            // Nothing is played yet, so the round in play is the first column.
            that(ShownIndex(first) == 0,
                $"the default is the round being played, because that is the question a phone arrives with; got column {ShownIndex(first)}");

            // The arrow has to be a working link, not an empty href. Everything
            // else here would still pass if the tag helper produced nothing.
            that(first.Contains($"/pl/t/{id}/bracket?round=1", StringComparison.Ordinal),
                "the next-round arrow must keep the tournament in the URL and add the round");

            var second = browser.GetStringAsync($"/pl/t/{id}/bracket?round=2").GetAwaiter().GetResult();
            that(Occurrences(second, "bracket__col--shown") == 1 && ShownIndex(second) == 2,
                $"the arrows are links, so the chosen round survives a reload and a pasted URL; got column {ShownIndex(second)}");

            // ── follow one team (023) ────────────────────────────────
            //
            // The question a shared Discord link actually arrives with: did we
            // advance, and when do we play next. The tree cannot answer it.
            Guid followed;
            string followedName;
            using (var db = Db())
            {
                var opener = db.Matches.AsNoTracking()
                    .First(m => m.TournamentId == id && m.TeamAId != null);
                followed = opener.TeamAId!.Value;
                followedName = db.Teams.AsNoTracking().Single(t => t.Id == followed).Name;
            }

            var route = browser.GetStringAsync($"/pl/t/{id}/bracket?team={followed}").GetAwaiter().GetResult();
            that(route.Contains(followedName, StringComparison.Ordinal),
                "the followed team must be named");
            that(route.Contains("route__step", StringComparison.Ordinal),
                "and its route must render as a sequence, not as a tree");

            // The route is THIS team's matches and nobody else's: a four-team
            // double elimination gives one team at most upper R1, upper final,
            // two lower rounds and two grand finals.
            var steps = Occurrences(route, "route__step");
            that(steps is > 0 and <= 6,
                $"a route is one team's own matches, got {steps} steps");

            var noRoute = browser.GetStringAsync($"/pl/t/{id}/bracket").GetAwaiter().GetResult();
            that(!noRoute.Contains("route__step", StringComparison.Ordinal),
                "and without a team the page stays the ordinary bracket");

            var stranger = browser
                .GetAsync($"/pl/t/{id}/bracket?team={Guid.NewGuid()}").GetAwaiter().GetResult();
            that(stranger.StatusCode == System.Net.HttpStatusCode.OK,
                $"a team id from another tournament must not break the page, got {(int)stranger.StatusCode}");

            var silly = browser.GetAsync($"/pl/t/{id}/bracket?round=999").GetAwaiter().GetResult();
            that(silly.StatusCode == System.Net.HttpStatusCode.OK,
                $"a hand-edited round number must clamp, not throw; got {(int)silly.StatusCode}");

            var clamped = silly.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(Occurrences(clamped, "bracket__col--shown") == 1 &&
                 ShownIndex(clamped) == Occurrences(clamped, "class=\"bracket__col") - 1,
                "and clamp to the LAST round rather than showing none");
        });

        check("bracket: generating twice is refused by the DATABASE, not by luck", () =>
        {
            using var db = Db();
            var tournament = db.Tournaments.Single(t => t.Id == singleId);

            var threw = false;
            try
            {
                BracketBuilder.BuildAsync(db, tournament).GetAwaiter().GetResult();
            }
            catch (DbUpdateException)
            {
                threw = true;
            }

            that(threw, "a second bracket would overlap the first — the unique position index is what stops it");

            using var after = Db();
            that(after.Matches.Count(m => m.TournamentId == singleId) == 7, "still exactly one bracket");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
            db.Database.ExecuteSqlRaw(
                // Routing is a real foreign key now, so the links have to be cut
                // before the rows can go — which is itself proof the constraint exists.
                "UPDATE matches SET winner_to_match_id = NULL, loser_to_match_id = NULL WHERE organiser_id = {0}; " +
                "DELETE FROM matches WHERE organiser_id = {0}; " +
                "DELETE FROM registrations WHERE organiser_id = {0}; " +
                "DELETE FROM tournaments WHERE organiser_id = {0}; " +
                "DELETE FROM teams WHERE name LIKE {1}; " +
                "DELETE FROM organisers WHERE id = {0};",
                orgId, $"BT{stamp}%");
    }
}
