using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Contracts;
using NextSkill.Domain.Access;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;
using NextSkill.Simulator;

namespace NextSkill.Checks;

/// <summary>
/// How a result becomes a rating (decision 050), against real PostgreSQL and
/// the real HTTP pipeline.
///
/// These cover the join that Phases 0–3 left unbuilt: the rating engines, the
/// wire contract and the integrity rules all passed their checks while being
/// connected to nothing. What is proven here is that a reported result moves a
/// bracket, settles into an append-only ledger exactly once, and refuses to
/// move a rating when it should not.
/// </summary>
public static class MatchResultChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        // Starts at the real now, not a convenient far-future date: the plugin's
        // HMAC filter reads this same TimeProvider, so a clock five years ahead
        // makes every genuinely signed request look like a replay and returns
        // 401 — which is the replay defence working correctly on us. Time is
        // advanced later, after the signed traffic is done.
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        using var factory = TestHost.Create(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);

            // The sweeper's own worker is removed, and that is the point of
            // splitting it from RatingSweeper: these checks drive the sweep one
            // step at a time instead of racing a five-second timer and hoping.
            services.RemoveAll<IHostedService>();
        }));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var organiserId = Guid.NewGuid();
        var trustedOrganiserId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();
        const string secret = "rating-sim-secret-1234567890";

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        // ── one sweep, driven by hand ────────────────────────────────────
        bool SweepOnce()
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<RatingSweeper>()
                .SweepNextAsync().GetAwaiter().GetResult();
        }

        void SweepAll()
        {
            // The guard is generous because a referee reversal un-rates the
            // whole platform on purpose; it still catches the failure it exists
            // for, which is the sweeper re-picking a match forever.
            for (var guard = 0; SweepOnce(); guard++)
                if (guard > 2000) throw new Exception("The sweeper did not drain — it is probably re-picking a match.");
        }

        // ── a two-team match, ready to be finished ───────────────────────
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var setinfoToken = "";

        using (var db = Db())
        {
            db.Organisers.AddRange(
                new Organiser { Id = organiserId, Name = $"Rating Org {stamp}", Slug = $"rating-org-{stamp}", CreatedAt = DateTimeOffset.UtcNow },
                new Organiser { Id = trustedOrganiserId, Name = $"Trusted Org {stamp}", Slug = $"trusted-org-{stamp}", Trusted = true, CreatedAt = DateTimeOffset.UtcNow });

            db.Teams.AddRange(
                new Team { Id = teamAId, Name = $"Rating Alpha {stamp}", Tag = $"RA{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow },
                new Team { Id = teamBId, Name = $"Rating Bravo {stamp}", Tag = $"RB{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow });

            var (token, hash) = SetinfoTokens.Create();
            setinfoToken = token;

            db.Accounts.AddRange(
                new Account
                {
                    Id = playerAId, Email = $"rpa{stamp}@nextskill.test", PasswordHash = "x",
                    Nickname = $"Rpa{stamp}", SetinfoTokenHash = hash, CreatedAt = DateTimeOffset.UtcNow,
                },
                new Account
                {
                    Id = playerBId, Email = $"rpb{stamp}@nextskill.test", PasswordHash = "x",
                    Nickname = $"Rpb{stamp}", CreatedAt = DateTimeOffset.UtcNow,
                });

            db.TeamMemberships.AddRange(
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamAId, AccountId = playerAId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                },
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamBId, AccountId = playerBId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });
            db.SaveChanges();
        }

        using (var db = Db(organiserId))
        {
            db.Servers.Add(new GameServer
            {
                Id = serverId, OrganiserId = organiserId, Host = "127.0.0.1", Port = 27016,
                Game = Game.Cs2, Verified = true, HmacSecret = secret, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Tournaments.Add(new Tournament
            {
                Id = tournamentId, OrganiserId = organiserId, Name = $"Rating Cup {stamp}",
                Game = Game.Cs2, Format = TournamentFormat.SingleElimination,
                Status = TournamentStatus.Running, TeamSize = TeamSize.Solo, MaxTeams = 4,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        var createdMatches = new List<Guid>();

        /// <summary>A finished, confirmed match between the two teams.</summary>
        Guid FinishedMatch(
            DateTimeOffset endedAt,
            Guid? winner = null,
            MatchIntegrity integrity = MatchIntegrity.Confirmed,
            Guid? organiser = null,
            MatchStatus status = MatchStatus.Completed,
            bool withParticipants = false,
            int position = 0)
        {
            var id = Guid.NewGuid();
            var owner = organiser ?? organiserId;
            using var db = Db(owner);
            db.Matches.Add(new Match
            {
                Id = id, OrganiserId = owner, TournamentId = tournamentId, Game = Game.Cs2,
                Status = status, Integrity = integrity,
                TeamAId = teamAId, TeamBId = teamBId,
                SlotAResolved = true, SlotBResolved = true,
                WinnerTeamId = winner ?? teamAId,
                ScoreA = 16, ScoreB = 12,
                Side = BracketSide.Upper, Round = 9, Position = position,
                CreatedAt = endedAt, StartedAt = endedAt, EndedAt = endedAt,
            });

            if (withParticipants)
            {
                db.MatchParticipants.AddRange(
                    new MatchParticipant
                    {
                        Id = Guid.NewGuid(), OrganiserId = owner, MatchId = id,
                        AccountId = playerAId, TeamId = teamAId, Slot = RosterSlot.Player,
                        Provenance = ParticipantProvenance.Declared, CreatedAt = endedAt,
                    },
                    new MatchParticipant
                    {
                        Id = Guid.NewGuid(), OrganiserId = owner, MatchId = id,
                        AccountId = playerBId, TeamId = teamBId, Slot = RosterSlot.Player,
                        Provenance = ParticipantProvenance.Declared, CreatedAt = endedAt,
                    });
            }

            db.SaveChanges();
            createdMatches.Add(id);
            return id;
        }

        List<RatingChange> LedgerFor(Guid matchId)
        {
            using var db = Db();
            return db.RatingChanges.AsNoTracking().Where(r => r.MatchId == matchId).ToList();
        }

        (double Value, double Deviation, int Played) TeamRating(Guid teamId)
        {
            using var db = Db();
            var row = db.Ratings.AsNoTracking().SingleOrDefault(r => r.TeamId == teamId && r.Game == Game.Cs2);
            return row is null ? (0, 0, 0) : (row.Value, row.Deviation, row.MatchesPlayed);
        }

        // ═══ 1. idempotency ══════════════════════════════════════════════
        check("rating: sweeping twice gives identical ratings and no duplicate ledger rows", () =>
        {
            var matchId = FinishedMatch(clock.GetUtcNow() - TimeSpan.FromHours(3), withParticipants: true);

            that(SweepOnce(), "a match whose challenge window has expired must be settled");
            var afterFirst = TeamRating(teamAId);
            var rows = LedgerFor(matchId);

            that(rows.Count == 4,
                $"two teams plus two players should each get one ledger row, got {rows.Count}");
            that(rows.All(r => r.ExcludedBecause is null),
                "a clean confirmed result past its window must be RATED, not excluded");
            that(rows.Any(r => r.SubjectId == teamAId && r.SubjectIsTeam),
                "the winning team needs a ledger row");
            that(rows.Any(r => r.SubjectId == playerAId && !r.SubjectIsTeam),
                "a declared participant is rated too — the CS 1.6 moat depends on it (050 §1)");

            // A second pass must not find it again, and must not be able to
            // write it again even if it did.
            SweepAll();
            that(LedgerFor(matchId).Count == 4, "a second sweep must not append a second set of rows");
            that(TeamRating(teamAId).Value == afterFirst.Value, "a second sweep must not move the rating again");

            // The database constraint, proven directly rather than trusted:
            // application logic that merely happens to be right is not the
            // guarantee 050 §3 asked for.
            using var db = Db();
            db.RatingChanges.Add(new RatingChange
            {
                Id = Guid.NewGuid(), MatchId = matchId, SubjectId = teamAId,
                SubjectIsTeam = true, Game = Game.Cs2, AppliedAt = clock.GetUtcNow(),
            });
            var rejected = false;
            try { db.SaveChanges(); }
            catch (DbUpdateException) { rejected = true; }
            that(rejected, "the ledger's unique index MUST refuse a second row for the same (match, subject, game)");
        });

        // ═══ 2. determinism ══════════════════════════════════════════════
        check("rating: results apply in (EndedAt, Id) order regardless of insertion order", () =>
        {
            // Glicko-2 is rating-period based, so out-of-order application gives
            // different numbers. This is what makes recomputation after a
            // referee reversal trustworthy (025, 050 §4).
            var basis = clock.GetUtcNow() - TimeSpan.FromHours(9);

            // Inserted newest-first, deliberately.
            var later = FinishedMatch(basis + TimeSpan.FromHours(2), winner: teamBId, position: 1);
            var earlier = FinishedMatch(basis, winner: teamAId, position: 2);

            var order = new List<Guid>();
            while (true)
            {
                using var db = Db();
                var before = db.Matches.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.RatedAt == null && (m.Id == later || m.Id == earlier))
                    .Select(m => m.Id).ToList();
                if (before.Count == 0) break;
                if (!SweepOnce()) break;

                using var after = Db();
                var settled = after.Matches.IgnoreQueryFilters().AsNoTracking()
                    .Where(m => m.RatedAt != null && (m.Id == later || m.Id == earlier))
                    .Select(m => m.Id).ToList();
                foreach (var id in settled.Where(id => !order.Contains(id))) order.Add(id);
            }

            that(order.Count == 2, $"both matches should settle, got {order.Count}");
            that(order[0] == earlier,
                "the older result must be applied first even though the newer one was inserted first");
        });

        // ═══ 3. integrity gate ═══════════════════════════════════════════
        check("rating: a DISPUTED-integrity result is never rated, and its ledger row says why", () =>
        {
            var matchId = FinishedMatch(
                clock.GetUtcNow() - TimeSpan.FromHours(3),
                integrity: MatchIntegrity.Disputed,
                position: 3);
            var before = TeamRating(teamAId);

            that(SweepOnce(), "the match must still be SETTLED — otherwise the sweeper rescans it forever");

            var rows = LedgerFor(matchId);
            that(rows.Count > 0 && rows.All(r => r.ExcludedBecause is not null),
                "every row for an unrated match must carry a reason");
            that(rows[0].ExcludedBecause!.Contains("Disputed", StringComparison.OrdinalIgnoreCase),
                $"the reason must name the integrity state, got '{rows[0].ExcludedBecause}'");
            that(rows.All(r => r.ValueAfter is null),
                "an unrated row must record no movement at all, not a movement of zero");
            that(TeamRating(teamAId).Value == before.Value, "a disputed result must not move a rating");
        });

        // ═══ 4. a captain's dispute holds the rating, never the bracket ══
        check("rating: a captain dispute holds the rating and leaves the bracket alone", () =>
        {
            var matchId = FinishedMatch(clock.GetUtcNow() - TimeSpan.FromHours(3), position: 4);

            using (var db = Db(organiserId))
            {
                var match = db.Matches.Single(m => m.Id == matchId);
                match.DisputedAt = clock.GetUtcNow();
                match.DisputedByAccountId = playerBId;
                match.DisputeReason = "Wrong side started";
                db.SaveChanges();
            }

            SweepAll();

            using var after = Db(organiserId);
            var settled = after.Matches.AsNoTracking().Single(m => m.Id == matchId);
            that(settled.RatedAt is null, "a disputed match must be PAUSED, not settled as unrated");
            that(LedgerFor(matchId).Count == 0,
                "a pause writes nothing — the claim is unresolved, not resolved-as-no");

            // The half that a losing captain must not be able to touch (040 §9).
            that(settled.WinnerTeamId == teamAId, "a dispute must never un-advance the bracket");
            that(settled.Status == MatchStatus.Completed, "the match stays completed");
        });

        // ═══ 5. confirmation accelerates ═════════════════════════════════
        check("rating: both captains confirming rates the match before the window would have closed", () =>
        {
            // Ended one minute ago: nowhere near the two-hour window.
            var matchId = FinishedMatch(clock.GetUtcNow() - TimeSpan.FromMinutes(1), position: 5);

            SweepAll();
            using (var open = Db(organiserId))
            {
                that(open.Matches.AsNoTracking().Single(m => m.Id == matchId).RatedAt is null,
                    "an open challenge window must hold the rating back");
            }

            using (var db = Db(organiserId))
            {
                var match = db.Matches.Single(m => m.Id == matchId);
                match.TeamAConfirmedAt = clock.GetUtcNow();
                db.SaveChanges();
            }

            SweepAll();
            using (var half = Db(organiserId))
            {
                that(half.Matches.AsNoTracking().Single(m => m.Id == matchId).RatedAt is null,
                    "ONE captain agreeing is not agreement");
            }

            using (var db = Db(organiserId))
            {
                var match = db.Matches.Single(m => m.Id == matchId);
                match.TeamBConfirmedAt = clock.GetUtcNow();
                db.SaveChanges();
            }

            SweepAll();
            using var both = Db(organiserId);
            that(both.Matches.AsNoTracking().Single(m => m.Id == matchId).RatedAt is not null,
                "both captains agreeing must close the window early");
            that(LedgerFor(matchId).All(r => r.ExcludedBecause is null),
                "an accelerated settlement is a normal rated one");
        });

        // ═══ 6. organiser.trusted finally means something ════════════════
        check("rating: a trusted organiser skips the wait — but a SUSPECT result still is not rated", () =>
        {
            // Both ended seconds ago, so only trust can settle them.
            var clean = FinishedMatch(
                clock.GetUtcNow(), organiser: trustedOrganiserId, position: 6);
            var suspect = FinishedMatch(
                clock.GetUtcNow(), integrity: MatchIntegrity.Suspect,
                organiser: trustedOrganiserId, position: 7);

            SweepAll();

            using var db = Db(trustedOrganiserId);
            that(db.Matches.AsNoTracking().Single(m => m.Id == clean).RatedAt is not null,
                "a trusted organiser's result must not wait for confirmation");
            that(LedgerFor(clean).All(r => r.ExcludedBecause is null),
                "and it must actually be rated");

            that(db.Matches.AsNoTracking().Single(m => m.Id == suspect).RatedAt is not null,
                "the suspect result is still settled, so it stops being rescanned");
            that(LedgerFor(suspect).All(r => r.ExcludedBecause is not null),
                "TRUST IS ABOUT INTENT, INTEGRITY IS ABOUT RECEIPT — a trusted organiser " +
                "skips confirmation but never the integrity gate (035, 050 §5)");
        });

        // ═══ 8. a forfeit settles without a confirmation nobody can give ═
        check("rating: a forfeited match settles immediately and is never rated", () =>
        {
            var matchId = FinishedMatch(
                clock.GetUtcNow(), status: MatchStatus.Forfeited, position: 8);

            SweepAll();

            using var db = Db(organiserId);
            that(db.Matches.AsNoTracking().Single(m => m.Id == matchId).RatedAt is not null,
                "a team that did not show up cannot click confirm — waiting on one waits forever");
            var rows = LedgerFor(matchId);
            that(rows.Count > 0 && rows.All(r => r.ExcludedBecause!.Contains("forfeit")),
                "a forfeit measures attendance, not skill, and the reason must say so");
        });

        // ═══ 7 + the keystone: through the real HTTP pipeline ════════════
        var liveMatchId = Guid.NewGuid();
        var nextMatchId = Guid.NewGuid();

        using (var db = Db(organiserId))
        {
            // A real routing link, so advancement has somewhere to advance TO.
            db.Matches.Add(new Match
            {
                Id = nextMatchId, OrganiserId = organiserId, TournamentId = tournamentId, Game = Game.Cs2,
                Status = MatchStatus.Scheduled, Integrity = MatchIntegrity.Confirmed,
                Side = BracketSide.Upper, Round = 11, Position = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Matches.Add(new Match
            {
                Id = liveMatchId, OrganiserId = organiserId, TournamentId = tournamentId, Game = Game.Cs2,
                Status = MatchStatus.Scheduled, Integrity = MatchIntegrity.Confirmed,
                ServerId = serverId, TeamAId = teamAId, TeamBId = teamBId,
                SlotAResolved = true, SlotBResolved = true,
                Side = BracketSide.Upper, Round = 10, Position = 0,
                WinnerToMatchId = nextMatchId, WinnerToSlot = SlotRef.A,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
            createdMatches.Add(liveMatchId);
            createdMatches.Add(nextMatchId);
        }

        using var http = factory.NewClient();
        var sim = new PluginSimulator(http, serverId, secret);

        check("bind: match_loaded declares a lineup automatically, with no captain action", () =>
        {
            var response = sim.MatchLoaded(liveMatchId, 1).GetAwaiter().GetResult();
            that(response.IsSuccessStatusCode, $"match_loaded should be accepted, got {(int)response.StatusCode}");

            using var db = Db(organiserId);
            var participants = db.MatchParticipants.AsNoTracking()
                .Where(p => p.MatchId == liveMatchId).ToList();

            that(participants.Count == 2,
                $"a 1v1 with two rostered captains should declare two players, got {participants.Count}");
            that(participants.All(p => p.Provenance == ParticipantProvenance.Declared),
                "nothing is proven yet, so every row starts Declared");
            that(participants.All(p => p.LinkCode is { Length: 4 }),
                "each declared player gets factor 2 of their binding");

            // The retry the plugin will absolutely make.
            sim.MatchLoaded(liveMatchId, 1).GetAwaiter().GetResult();
            using var again = Db(organiserId);
            that(again.MatchParticipants.AsNoTracking().Count(p => p.MatchId == liveMatchId) == 2,
                "a retried match_loaded must not mint a second lineup");
        });

        check("bind: player_bound upgrades Declared to Bound without creating a second row", () =>
        {
            string codeOf(Guid accountId)
            {
                using var db = Db(organiserId);
                return db.MatchParticipants.AsNoTracking()
                    .Single(p => p.MatchId == liveMatchId && p.AccountId == accountId).LinkCode!;
            }

            var code = codeOf(playerAId);

            // Wrong code, right token.
            var refused = sim.PlayerBound(liveMatchId, 2, playerAId, setinfoToken, "ZZZZ")
                .GetAwaiter().GetResult();
            that(refused.IsSuccessStatusCode,
                "a refused bind must still be 200 — the plugin cannot fix a wrong code by retrying");
            that(refused.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("bind_refused"),
                "the refusal must be reported to the plugin");

            // Right code, wrong token.
            var wrongToken = sim.PlayerBound(liveMatchId, 3, playerAId, "not-the-real-token", code)
                .GetAwaiter().GetResult();
            that(wrongToken.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("bind_refused"),
                "ONE FACTOR IS NOT ENOUGH — a harvested token without the live code must not bind (011)");

            using (var stillDeclared = Db(organiserId))
            {
                that(stillDeclared.MatchParticipants.AsNoTracking()
                        .Single(p => p.MatchId == liveMatchId && p.AccountId == playerAId)
                        .Provenance == ParticipantProvenance.Declared,
                    "two refusals must leave the row exactly as it was");
            }

            // Both factors, the same rostered slot.
            var bound = sim.PlayerBound(liveMatchId, 4, playerAId, setinfoToken, code)
                .GetAwaiter().GetResult();
            that(bound.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("\"bound\""),
                "both factors matching the same rostered slot must bind");

            using var db = Db(organiserId);
            var rows = db.MatchParticipants.AsNoTracking()
                .Where(p => p.MatchId == liveMatchId && p.AccountId == playerAId).ToList();
            that(rows.Count == 1, $"a bind PROMOTES the declared row, it does not add one — got {rows.Count}");
            that(rows[0].Provenance == ParticipantProvenance.Bound && rows[0].BoundAt is not null,
                "the promotion must be recorded");
            that(rows[0].LinkCode is null, "a used code is consumed, so it cannot be replayed");
        });

        check("KEYSTONE: a reported map_end sets the winner and advances the bracket", () =>
        {
            // This is the sentence Phase 4 exists to make true: a bracket that
            // advanced itself from a result the SERVER reported.
            sim.RoundEnd(liveMatchId, 5, 24, 13, 11).GetAwaiter().GetResult();
            var response = sim.MapEnd(liveMatchId, 6, "de_nuke", 13, 11, 24).GetAwaiter().GetResult();
            that(response.IsSuccessStatusCode, $"map_end should be accepted, got {(int)response.StatusCode}");

            using var db = Db(organiserId);
            var played = db.Matches.AsNoTracking().Single(m => m.Id == liveMatchId);
            that(played.Integrity == MatchIntegrity.Confirmed, "agreement confirms");
            that(played.WinnerTeamId == teamAId,
                "the winner must be STORED — until this slice, map_end set a score and left it null");

            var next = db.Matches.AsNoTracking().Single(m => m.Id == nextMatchId);
            that(next.TeamAId == teamAId,
                "the winner must arrive in the next match — AdvanceAsync's only production caller " +
                "used to be bye resolution, so no reported result ever moved a bracket");
            that(next.SlotAResolved, "and the slot must be marked resolved, not merely filled");
        });

        check("rating: the reported result then settles into the ledger for teams AND players", () =>
        {
            clock.Advance(NextSkill.Domain.Matches.MatchSettlement.ChallengeWindow + TimeSpan.FromMinutes(1));
            SweepAll();

            var rows = LedgerFor(liveMatchId);
            that(rows.Count == 2 + 2, $"two teams and two declared players, got {rows.Count}");
            that(rows.All(r => r.ExcludedBecause is null), "a clean confirmed result must rate");

            var winnerRow = rows.Single(r => r.SubjectId == teamAId && r.SubjectIsTeam);
            that(winnerRow.ValueAfter > winnerRow.ValueBefore,
                $"the winning team's rating must rise: {winnerRow.ValueBefore} -> {winnerRow.ValueAfter}");

            var loserRow = rows.Single(r => r.SubjectId == teamBId && r.SubjectIsTeam);
            that(loserRow.ValueAfter < loserRow.ValueBefore, "the losing team's rating must fall");

            var boundPlayer = rows.Single(r => r.SubjectId == playerAId && !r.SubjectIsTeam);
            that(boundPlayer.ValueAfter > boundPlayer.ValueBefore,
                "a player who was PROVEN to have played gains rating");

            using var db = Db();
            that(db.Ratings.AsNoTracking().Any(r => r.AccountId == playerAId && r.Game == Game.Cs2),
                "and a real Rating row exists — nothing in this application had ever written one");
        });

        check("ranking: the settled result becomes a movement arrow and a form strip", () =>
        {
            // These two were ⊘ "blocked on the Matches slice" for the whole
            // phase, because both need match history and the CSS alone is not a
            // feature. The history now exists.
            //
            // The ranked table is what carries them, so the deviation is settled
            // by hand here: whether a team is provisional is 025's rule and is
            // already covered elsewhere — what is under test is the rendering.
            using (var db = Db())
            {
                foreach (var rating in db.Ratings.Where(r => r.TeamId == teamAId || r.TeamId == teamBId))
                    rating.Deviation = 40;
                db.SaveChanges();
            }

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/ranking?q=RA{stamp % 100000}").GetAwaiter().GetResult());

            that(html.Contains($"Rating Alpha {stamp}", StringComparison.Ordinal),
                "the winning team must appear in the ranked table");
            that(html.Contains("▲", StringComparison.Ordinal),
                "a rating that rose must show the movement arrow");
            that(html.Contains("rating wzrósł po ostatnim meczu", StringComparison.Ordinal),
                "and never the glyph alone — the direction must survive without shape or colour (019)");
            that(html.Contains("Ostatnie mecze, od najnowszego: W", StringComparison.Ordinal),
                "the form strip must read from real results, newest first");

            var losing = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/ranking?q=RB{stamp % 100000}").GetAwaiter().GetResult());
            that(losing.Contains("Ostatnie mecze, od najnowszego: L", StringComparison.Ordinal),
                "and the team that lost must read L, not a mirror of the winner");
        });

        check("team page: history is integrity-aware and the trend reads the ledger", () =>
        {
            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/teams/{teamAId}?game=Cs2").GetAwaiter().GetResult());

            that(html.Contains("Historia meczów", StringComparison.Ordinal),
                "the team page must show competitive history (049)");
            that(html.Contains($"Rating Bravo {stamp}", StringComparison.Ordinal),
                "and name the opponent it played");
            that(html.Contains("Bilans:", StringComparison.Ordinal),
                "a win-loss record must be stated, not left to be counted by eye");

            // The fixture deliberately contains a Disputed-integrity result.
            that(html.Contains("niepotwierdzony", StringComparison.Ordinal),
                "a result whose integrity is unresolved must be LABELLED, not quietly counted");
            that(html.Contains("poza bilansem do czasu wyjaśnienia", StringComparison.Ordinal),
                "and the record must say how many results it is holding back, or it cannot be reconciled");

            that(html.Contains("Zmiany ratingu", StringComparison.Ordinal),
                "the rating trend must be present");
            that(html.Contains("<table", StringComparison.Ordinal),
                "and it is a TABLE — 049 asks for an accessible trend, so the numbers " +
                "must be readable without a chart, not only through one");

            var otherGame = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/teams/{teamAId}?game=Cs16").GetAwaiter().GetResult());
            that(!otherGame.Contains($"Rating Bravo {stamp}", StringComparison.Ordinal),
                "CS2 history must not leak into the CS 1.6 view — a mixed record describes nobody (049)");
        });

        check("profile: match history is real and shows HOW it is known", () =>
        {
            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/players/{playerAId}").GetAwaiter().GetResult());

            that(html.Contains("Ostatnie mecze", StringComparison.Ordinal),
                "the profile must show match history (029, unblocked by 050)");
            that(html.Contains($"Rating Alpha {stamp}", StringComparison.Ordinal) &&
                 html.Contains($"Rating Bravo {stamp}", StringComparison.Ordinal),
                "and identify both sides of a match this player is recorded in");

            // playerA bound through the simulator earlier; the fixture's other
            // matches only ever declared them.
            that(html.Contains("potwierdzony w grze", StringComparison.Ordinal),
                "a bind proven by the server must be labelled as such");
            that(html.Contains("zgłoszony przez kapitana", StringComparison.Ordinal),
                "and a captain's declaration must NOT be dressed up as proof — " +
                "keeping the two distinguishable is the whole point of Provenance (050 §1)");
        });

        check("search: one GET form, accent-insensitive, and wildcards are text", () =>
        {
            using var spectator = factory.NewClient();

            var found = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/search?q=Rating+Alpha+{stamp}").GetAwaiter().GetResult());
            that(found.Contains($"Rating Alpha {stamp}", StringComparison.Ordinal),
                "a plain GET search must find a team by name, with no JavaScript involved (018)");

            // The reason 051 §8 had to correct 043's SQL: unaccent() is STABLE,
            // so the query as specified could not even be indexed. With the
            // IMMUTABLE wrapper in place, diacritics stop mattering — which is
            // the difference between working and not working for Polish users.
            using (var db = Db())
            {
                db.Teams.Add(new Team
                {
                    Id = Guid.NewGuid(),
                    Name = $"Łódź Cup {stamp}",
                    Tag = $"LDZ{stamp % 10000}",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.SaveChanges();
            }

            var accentless = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/search?q=lodz+cup+{stamp}").GetAwaiter().GetResult());
            that(accentless.Contains($"Łódź Cup {stamp}", StringComparison.Ordinal),
                "typing 'lodz' must find 'Łódź' — without this the search is unusable in Polish");

            // A bare % must be text, not "return everything".
            var wildcard = WebUtility.HtmlDecode(spectator
                .GetStringAsync("/pl/search?q=%25").GetAwaiter().GetResult());
            that(wildcard.Contains("Nic nie znaleziono", StringComparison.Ordinal),
                "% must be a literal character, never an operator the visitor gets to use (039)");

            using (var cleanup = Db())
            {
                cleanup.Database.ExecuteSqlRaw(
                    "DELETE FROM teams WHERE tag={0}", $"LDZ{stamp % 10000}");
            }
        });

        check("home: the front page leads with real activity, not a scaffold", () =>
        {
            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator.GetStringAsync("/pl").GetAwaiter().GetResult());

            that(!html.Contains("Design system działa", StringComparison.Ordinal),
                "the component scaffold must be gone — it was the home page for the whole phase");
            that(html.Contains("Wyniki z serwera", StringComparison.Ordinal),
                "the page must lead with the differentiator (043, strategy §2)");

            that(html.Contains("Ostatnie wyniki", StringComparison.Ordinal),
                "recent results must be shown once results exist");
            that(html.Contains($"Rating Alpha {stamp}", StringComparison.Ordinal),
                "and they must be REAL rows from the database, not placeholders");
            that(html.Contains("Czołówka CS2", StringComparison.Ordinal),
                "top teams must appear once any team is out of provisional");

            that(!html.Contains("Akcja drugorzędna", StringComparison.Ordinal),
                "and none of the design-system demo buttons may survive into a real page");
        });

        check("seasons: an archive is the ledger read AS AT the closing moment", () =>
        {
            // Two seasons around the same results: one closed before they
            // settled, one after. If the archive were a snapshot, or were
            // recomputed live, these two would not differ.
            var beforeNumber = 9000 + (int)(stamp % 400);
            var afterNumber = beforeNumber + 500;
            var settledAt = clock.GetUtcNow();

            using (var db = Db())
            {
                // Standings exclude provisional teams for the same reason the
                // live ranking does (025), and two matches leave a deviation
                // nowhere near settled. That rule is covered elsewhere; what is
                // under test here is the AS-AT read, so the ledger's deviation
                // is settled by hand exactly as the ranking check settles the
                // aggregate.
                foreach (var row in db.RatingChanges.Where(r =>
                             r.SubjectIsTeam && (r.SubjectId == teamAId || r.SubjectId == teamBId) &&
                             r.SupersededAt == null && r.DeviationAfter != null))
                    row.DeviationAfter = 40;

                db.Seasons.AddRange(
                    new Season
                    {
                        Id = Guid.NewGuid(), Number = beforeNumber, Name = $"Archiwum A {stamp}",
                        StartsAt = settledAt - TimeSpan.FromDays(90),
                        EndsAt = settledAt - TimeSpan.FromDays(30),
                        Closed = true,
                        ClosedAt = settledAt - TimeSpan.FromDays(30),
                    },
                    new Season
                    {
                        Id = Guid.NewGuid(), Number = afterNumber, Name = $"Archiwum B {stamp}",
                        StartsAt = settledAt - TimeSpan.FromDays(29),
                        EndsAt = settledAt + TimeSpan.FromDays(1),
                        Closed = true,
                        ClosedAt = settledAt + TimeSpan.FromDays(1),
                    });
                db.SaveChanges();
            }

            using var spectator = factory.NewClient();

            var later = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/seasons/{afterNumber}?game=Cs2").GetAwaiter().GetResult());
            that(later.Contains($"Rating Alpha {stamp}", StringComparison.Ordinal),
                "a season closing AFTER the results settled must contain them");

            var earlier = WebUtility.HtmlDecode(spectator
                .GetStringAsync($"/pl/seasons/{beforeNumber}?game=Cs2").GetAwaiter().GetResult());
            that(!earlier.Contains($"Rating Alpha {stamp}", StringComparison.Ordinal),
                "a season closed BEFORE they settled must not — which is what proves the table " +
                "is read as at a moment rather than recomputed or snapshotted (051 §4)");
            that(earlier.Contains("zamknięta", StringComparison.Ordinal),
                "and the archive must state that it is frozen, with the instant it froze at");

            // The invariant that makes any of this possible, enforced where it
            // cannot be forgotten. Nothing in the app closes a season yet; the
            // constraint is here so that whatever eventually does cannot skip it.
            using var db2 = Db();
            var rejected = false;
            try
            {
                db2.Database.ExecuteSqlRaw(
                    "INSERT INTO seasons (id, number, name, starts_at, closed) " +
                    "VALUES ({0}, {1}, 'Bez daty', now(), true)",
                    Guid.NewGuid(), beforeNumber + 900);
            }
            catch (Npgsql.PostgresException) { rejected = true; }
            that(rejected,
                "closing a season without recording WHEN must be refused — the archive is " +
                "unreconstructable without it");

            using (var cleanup = Db())
            {
                cleanup.Database.ExecuteSqlRaw(
                    "DELETE FROM seasons WHERE number IN ({0},{1})", beforeNumber, afterNumber);
            }
        });

        // ═══ the referee's desk: what unparks a dispute ══════════════════
        var refereeNick = $"Ref{stamp}";
        var strangerNick = $"Str{stamp}";
        const string password = "a-long-enough-passphrase";
        Guid refereeId;
        Guid strangerId;

        HttpClient SignedInAs(string nickname)
        {
            var client = factory.NewClient();
            client.PostFormAsync("/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            return client;
        }

        using var referee = SignedInAs(refereeNick);
        using var stranger = SignedInAs(strangerNick);

        using (var db = Db())
        {
            refereeId = db.Accounts.AsNoTracking().Single(a => a.Nickname == refereeNick).Id;
            strangerId = db.Accounts.AsNoTracking().Single(a => a.Nickname == strangerNick).Id;
            db.OrganiserStaff.Add(new OrganiserStaff
            {
                Id = Guid.NewGuid(),
                OrganiserId = organiserId,
                AccountId = refereeId,
                Role = OrganiserRole.Referee,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        check("referee: someone with no organiser role cannot resolve a dispute, and the attempt is recorded", () =>
        {
            var held = FinishedMatch(clock.GetUtcNow() - TimeSpan.FromHours(3), position: 20);
            using (var db = Db(organiserId))
            {
                var match = db.Matches.Single(m => m.Id == held);
                match.DisputedAt = clock.GetUtcNow();
                match.DisputeReason = "Opponent used a stand-in";
                db.SaveChanges();
            }

            var response = stranger.PostFormAsync("/pl/org/disputes?handler=Dismiss", new()
            {
                ["Input.MatchId"] = held.ToString(),
                ["Input.Reason"] = "I would like this to go away",
            }, "/pl/m/" + held).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"a stranger must not referee someone else's event, got {(int)response.StatusCode}");

            using var db2 = Db();
            var denied = db2.AuditEntries.AsNoTracking()
                .Where(a => a.ActorAccountId == strangerId && a.Outcome == AuditOutcome.Denied)
                .ToList();
            that(denied.Count == 1 && denied[0].Action == AuditAction.ResolveDispute,
                "the refused referee attempt must leave a row — nothing else changed, so nothing else could");

            using var db3 = Db(organiserId);
            that(db3.Matches.AsNoTracking().Single(m => m.Id == held).DisputedAt is not null,
                "and the dispute must still be held");
        });

        check("referee: dismissing a dispute releases the rating the captain parked", () =>
        {
            var held = FinishedMatch(clock.GetUtcNow() - TimeSpan.FromHours(3), position: 21);
            using (var db = Db(organiserId))
            {
                var match = db.Matches.Single(m => m.Id == held);
                match.DisputedAt = clock.GetUtcNow();
                match.DisputeReason = "Wrong map";
                db.SaveChanges();
            }

            SweepAll();
            using (var parked = Db(organiserId))
            {
                that(parked.Matches.AsNoTracking().Single(m => m.Id == held).RatedAt is null,
                    "the dispute must hold the rating before a referee touches it");
            }

            var response = referee.PostFormAsync("/pl/org/disputes?handler=Dismiss", new()
            {
                ["Input.MatchId"] = held.ToString(),
                ["Input.Reason"] = "Map was correct per the frozen veto",
            }, "/pl/org/disputes").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a referee decision should redirect, got {(int)response.StatusCode}");

            SweepAll();

            using var db2 = Db(organiserId);
            that(db2.Matches.AsNoTracking().Single(m => m.Id == held).RatedAt is not null,
                "dismissing the claim must let the sweeper settle the match");
            that(LedgerFor(held).Any(r => r.ExcludedBecause is null),
                "and it settles as RATED — the result stood");

            var audit = db2.AuditEntries.AsNoTracking()
                .Single(a => a.ResourceId == held && a.Action == AuditAction.ResolveDispute);
            that(audit.ActorAccountId == refereeId && audit.Reason == "Map was correct per the frozen veto",
                "a referee decision must never be silent (036)");
        });

        check("referee: reversing a result supersedes the old ledger rows and replays the ratings", () =>
        {
            var wrong = FinishedMatch(
                clock.GetUtcNow() - TimeSpan.FromHours(4), winner: teamAId, withParticipants: true, position: 22);

            SweepAll();
            var original = LedgerFor(wrong).Single(r => r.SubjectId == teamAId && r.SubjectIsTeam);
            that(original.ValueAfter > original.ValueBefore, "team A was credited with the win first");

            var response = referee.PostFormAsync("/pl/org/disputes?handler=Reverse", new()
            {
                ["Input.MatchId"] = wrong.ToString(),
                ["Input.WinnerTeamId"] = teamBId.ToString(),
                ["Input.Reason"] = "Server reported the sides swapped",
            }, "/pl/org/disputes").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.Redirect,
                $"the reversal should redirect, got {(int)response.StatusCode}");

            using (var db = Db(organiserId))
            {
                that(db.Matches.AsNoTracking().Single(m => m.Id == wrong).WinnerTeamId == teamBId,
                    "the corrected winner must be stored");
            }

            SweepAll();

            var rows = LedgerFor(wrong);
            var superseded = rows.Where(r => r.SupersededAt is not null).ToList();
            var live = rows.Where(r => r.SupersededAt is null).ToList();

            that(superseded.Count > 0,
                "the original rows must be SUPERSEDED, not deleted — the old numbers stay answerable");
            that(live.Count > 0, "and a fresh generation must exist alongside them");

            var replayed = live.Single(r => r.SubjectId == teamBId && r.SubjectIsTeam);
            that(replayed.ValueAfter > replayed.ValueBefore,
                "after the reversal it is team B whose rating rises");

            var audit = Db().AuditEntries.AsNoTracking()
                .Single(a => a.ResourceId == wrong && a.Action == AuditAction.ReverseResult);
            that(audit.ActorAccountId == refereeId, "the reversal names the referee who made it");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM audit_entries WHERE actor_account_id IN ({0},{1});",
                refereeId, strangerId);
            db.Database.ExecuteSqlRaw(
                "DELETE FROM organiser_staff WHERE account_id={0}; " +
                "DELETE FROM sessions WHERE account_id IN ({0},{1}); " +
                "DELETE FROM accounts WHERE id IN ({0},{1});",
                refereeId, strangerId);
        }

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM rating_changes WHERE subject_id IN ({0},{1},{2},{3}); " +
                "DELETE FROM ratings WHERE team_id IN ({0},{1}) OR account_id IN ({2},{3}); " +
                "DELETE FROM match_participants WHERE match_id IN (SELECT id FROM matches WHERE tournament_id={4}); " +
                "DELETE FROM notifications WHERE account_id IN ({2},{3}); " +
                "DELETE FROM matches WHERE tournament_id={4}; " +
                "DELETE FROM tournaments WHERE id={4}; " +
                "DELETE FROM servers WHERE id={5}; " +
                "DELETE FROM team_memberships WHERE team_id IN ({0},{1}); " +
                "DELETE FROM teams WHERE id IN ({0},{1}); " +
                "DELETE FROM accounts WHERE id IN ({2},{3}); " +
                "DELETE FROM organisers WHERE id IN ({6},{7});",
                teamAId, teamBId, playerAId, playerBId, tournamentId, serverId,
                organiserId, trustedOrganiserId);
        }

        _ = createdMatches;
    }
}
