using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Checks;

/// <summary>
/// One person, two accounts (decision 015).
///
/// <b>This is the most dangerous screen in the panel</b>, and the reason it gets
/// its own file: it is the only operation that rewrites who a finished result
/// was about. The checks are aimed at the two ways that goes wrong — history
/// silently failing to move, and the merge quietly arbitrating an overlap it
/// had no business deciding.
/// </summary>
public static class AccountMergeChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = TestHost.Create(builder => builder.ConfigureServices(services =>
            // The sweep is driven by hand rather than raced against a timer,
            // for the same reason MatchResultChecks does it.
            services.RemoveAll<IHostedService>()));

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        void SweepAll()
        {
            using var scope = factory.Services.CreateScope();
            var sweeper = scope.ServiceProvider.GetRequiredService<RatingSweeper>();
            for (var guard = 0; sweeper.SweepNextAsync().GetAwaiter().GetResult(); guard++)
                if (guard > 2000) throw new Exception("The sweeper did not drain.");
        }

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var organiserId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        const string password = "a-long-enough-passphrase";

        var ownerNick = $"MOw{stamp}";
        var staffNick = $"MSt{stamp}";
        var loserNick = $"MLo{stamp}";
        var survivorNick = $"MSu{stamp}";
        var opponentNick = $"MOp{stamp}";
        var playerNick = $"MPl{stamp}";

        HttpClient SignedInAs(string nickname)
        {
            var http = factory.NewClient();
            http.PostFormAsync("/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            return http;
        }

        using var owner = SignedInAs(ownerNick);
        using var staff = SignedInAs(staffNick);
        using var player = SignedInAs(playerNick);
        using var loserBrowser = SignedInAs(loserNick);
        SignedInAs(survivorNick).Dispose();
        SignedInAs(opponentNick).Dispose();

        Guid ownerId, staffId, loserId, survivorId, opponentId, playerId;

        using (var db = Db())
        {
            Guid IdOf(string nickname) => db.Accounts.Single(a => a.Nickname == nickname).Id;
            ownerId = IdOf(ownerNick);
            staffId = IdOf(staffNick);
            loserId = IdOf(loserNick);
            survivorId = IdOf(survivorNick);
            opponentId = IdOf(opponentNick);
            playerId = IdOf(playerNick);

            db.Accounts.Single(a => a.Id == ownerId).PlatformRole = PlatformRole.Owner;
            db.Accounts.Single(a => a.Id == staffId).PlatformRole = PlatformRole.Staff;

            // The duplicate is the one carrying the Steam link — the usual
            // reason a merge is asked for at all.
            db.Accounts.Single(a => a.Id == loserId).SteamId = $"7656{stamp % 10000000000000}";

            db.Organisers.Add(new Organiser
            {
                Id = organiserId, Name = $"Merge Org {stamp}", Slug = $"merge-org-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Teams.AddRange(
                new Team { Id = teamAId, Name = $"Merge Alpha {stamp}", Tag = $"MA{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow },
                new Team { Id = teamBId, Name = $"Merge Bravo {stamp}", Tag = $"MB{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow });

            db.TeamMemberships.AddRange(
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamAId, AccountId = loserId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                },
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamBId, AccountId = opponentId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });
            db.SaveChanges();
        }

        var endedAt = DateTimeOffset.UtcNow.AddHours(-3);
        using (var db = Db(organiserId))
        {
            db.Tournaments.Add(new Tournament
            {
                Id = tournamentId, OrganiserId = organiserId, Name = $"Merge Cup {stamp}",
                Game = Game.Cs2, Format = TournamentFormat.SingleElimination,
                Status = TournamentStatus.Running, TeamSize = TeamSize.Solo, MaxTeams = 4,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Matches.Add(new Match
            {
                Id = matchId, OrganiserId = organiserId, TournamentId = tournamentId, Game = Game.Cs2,
                Status = MatchStatus.Completed, Integrity = MatchIntegrity.Confirmed,
                TeamAId = teamAId, TeamBId = teamBId, SlotAResolved = true, SlotBResolved = true,
                WinnerTeamId = teamAId, ScoreA = 16, ScoreB = 12,
                Side = BracketSide.Upper, Round = 9, Position = 0,
                CreatedAt = endedAt, StartedAt = endedAt, EndedAt = endedAt,
            });
            db.MatchParticipants.AddRange(
                new MatchParticipant
                {
                    Id = Guid.NewGuid(), OrganiserId = organiserId, MatchId = matchId,
                    AccountId = loserId, TeamId = teamAId, Slot = RosterSlot.Player,
                    Provenance = ParticipantProvenance.Bound, CreatedAt = endedAt,
                },
                new MatchParticipant
                {
                    Id = Guid.NewGuid(), OrganiserId = organiserId, MatchId = matchId,
                    AccountId = opponentId, TeamId = teamBId, Slot = RosterSlot.Player,
                    Provenance = ParticipantProvenance.Declared, CreatedAt = endedAt,
                });
            db.SaveChanges();
        }

        SweepAll();

        HttpResponseMessage Merge(HttpClient http, string losing, string surviving, string confirmation, string tokenPage) =>
            http.PostFormAsync("/pl/admin/merge", new()
            {
                ["Input.LosingNickname"] = losing,
                ["Input.SurvivingNickname"] = surviving,
                ["Input.Confirmation"] = confirmation,
                ["Input.Reason"] = "zdublowane konto",
            }, tokenPage).GetAwaiter().GetResult();

        check("merge: setup is real — the duplicate holds a rating from a played match", () =>
        {
            using var db = Db();
            that(db.Ratings.Any(r => r.AccountId == loserId && r.Game == Game.Cs2),
                "the whole point is that the duplicate has history worth keeping");
            that(!db.Ratings.Any(r => r.AccountId == survivorId),
                "and the survivor has none yet, so a moved rating cannot be mistaken for its own");
        });

        check("merge: a player cannot reach it, and a hand-written POST is refused AND recorded", () =>
        {
            var page = player.GetAsync("/pl/admin/merge").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.Forbidden,
                $"rewriting whose result a match was is not a player action; got {(int)page.StatusCode}");

            var response = Merge(player, loserNick, survivorNick, loserNick, "/pl/me");
            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"and skipping the page must not skip the check; got {(int)response.StatusCode}");

            using var db = Db();
            that(db.Accounts.Single(a => a.Id == loserId).DeletedAt is null, "nothing may have happened");
            that(db.AuditEntries.Count(a =>
                    a.ActorAccountId == playerId && a.Action == AuditAction.MergeAccounts &&
                    a.Outcome == AuditOutcome.Denied) == 1,
                "a refused merge changes no row anywhere else — only the log can hold the attempt");
        });

        check("merge: STAFF cannot merge — a ban ends access, a merge rewrites the record", () =>
        {
            var page = staff.GetAsync("/pl/admin/merge").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.Forbidden,
                $"it shipped on ModerateUsers and was moved: setting `trusted` was already " +
                $"owner-only for less (036); got {(int)page.StatusCode}");

            var response = Merge(staff, loserNick, survivorNick, loserNick, "/pl/admin/users");
            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"and the POST must be refused too, got {(int)response.StatusCode}");

            using var db = Db();
            that(db.Accounts.Single(a => a.Id == loserId).DeletedAt is null, "nothing may have happened");
            that(db.AuditEntries.Count(a =>
                    a.ActorAccountId == staffId && a.Action == AuditAction.MergeAccounts &&
                    a.Outcome == AuditOutcome.Denied) == 1,
                "and the attempt is recorded, because it changed nothing else");
        });

        check("merge: a mistyped confirmation refuses, exactly like disbanding a team", () =>
        {
            var response = Merge(owner, loserNick, survivorNick, survivorNick, "/pl/admin/merge");
            that(response.StatusCode == HttpStatusCode.OK,
                $"the refusal is shown on the form; got {(int)response.StatusCode}");

            using var db = Db();
            that(db.Accounts.Single(a => a.Id == loserId).DeletedAt is null,
                "confirming with the WRONG nickname must not merge the right one");
        });

        check("merge: two accounts in the SAME match is refused, never arbitrated", () =>
        {
            using (var db = Db(organiserId))
            {
                db.MatchParticipants.Add(new MatchParticipant
                {
                    Id = Guid.NewGuid(), OrganiserId = organiserId, MatchId = matchId,
                    AccountId = survivorId, TeamId = teamAId, Slot = RosterSlot.Substitute,
                    Provenance = ParticipantProvenance.Declared, CreatedAt = endedAt,
                });
                db.SaveChanges();
            }

            var response = Merge(owner, loserNick, survivorNick, loserNick, "/pl/admin/merge");
            that(response.StatusCode == HttpStatusCode.OK, "refused on the form, not thrown");

            using (var db = Db())
            {
                that(db.Accounts.Single(a => a.Id == loserId).DeletedAt is null,
                    "one human counted twice in one result is a thing for a person to fix, not a merge to guess at");
            }

            // Undo the overlap so the merge below tests the ordinary case.
            using (var db = Db(organiserId))
            {
                db.MatchParticipants
                    .Where(p => p.MatchId == matchId && p.AccountId == survivorId)
                    .ExecuteDelete();
            }
        });

        check("merge: history moves, the duplicate becomes a tombstone, and the rating follows", () =>
        {
            var response = Merge(owner, loserNick, survivorNick, loserNick, "/pl/admin/merge");
            that(response.StatusCode == HttpStatusCode.OK,
                $"a successful merge re-renders with a confirmation; got {(int)response.StatusCode}");

            using (var db = Db(organiserId))
            {
                that(db.MatchParticipants.Any(p => p.MatchId == matchId && p.AccountId == survivorId),
                    "the finished result must now be about the surviving account");
                that(!db.MatchParticipants.Any(p => p.MatchId == matchId && p.AccountId == loserId),
                    "and no longer about the duplicate");
            }

            using (var db = Db())
            {
                var tombstone = db.Accounts.Single(a => a.Id == loserId);
                that(tombstone.DeletedAt is not null, "the duplicate must not survive as a second person");
                that(tombstone.Nickname != loserNick, "and must not keep its identity");
                that(tombstone.SteamId is null, "its Steam link must have been let go");
                that(!db.Sessions.Any(s => s.AccountId == loserId), "every session of the duplicate must be dead");

                that(db.Accounts.Single(a => a.Id == survivorId).SteamId == $"7656{stamp % 10000000000000}",
                    "the survivor had no Steam link, so it inherits the duplicate's");

                that(db.TeamMemberships.Any(m => m.TeamId == teamAId && m.AccountId == survivorId),
                    "the roster place moves with the person");

                that(db.AuditEntries.Any(a =>
                        a.ActorAccountId == ownerId && a.Action == AuditAction.MergeAccounts &&
                        a.Outcome == AuditOutcome.Allowed && a.ResourceId == loserId),
                    "and the merge is answerable later");
            }

            // The replay un-rated the platform on purpose; draining the sweeper
            // is what a running deployment does within seconds.
            SweepAll();

            using (var db = Db())
            {
                that(db.Ratings.Any(r => r.AccountId == survivorId && r.Game == Game.Cs2),
                    "THE POINT: the competitive record ends up on the account that survived");
                that(!db.Ratings.Any(r => r.AccountId == loserId),
                    "and the duplicate no longer appears in any ranking");
            }
        });

        check("merge: the losing account cannot be logged into afterwards", () =>
        {
            var response = loserBrowser.GetAsync("/pl/me").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a live cookie for a merged-away account must stop working, got {(int)response.StatusCode}");
        });
    }
}
