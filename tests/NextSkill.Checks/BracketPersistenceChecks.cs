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
