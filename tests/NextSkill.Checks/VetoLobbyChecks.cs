using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Access;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;
using NextSkill.Domain.Veto;

namespace NextSkill.Checks;

/// <summary>
/// The public match lobby and web veto through the real browser pipeline
/// (decisions 028, 036, 051).
/// </summary>
public static class VetoLobbyChecks
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
        var clock = new MutableTimeProvider(new DateTimeOffset(2030, 9, 1, 12, 0, 0, TimeSpan.Zero));
        using var factory = TestHost.Create(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var organiserId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        var teamAName = $"Veto Alpha {stamp}";
        var teamBName = $"Veto Bravo {stamp}";
        var captainANick = $"Vca{stamp}";
        var captainBNick = $"Vcb{stamp}";
        const string password = "a-long-enough-passphrase";

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

        using var captainA = SignedInAs(captainANick);
        using var captainB = SignedInAs(captainBNick);
        Guid captainAId;
        Guid captainBId;

        using (var db = Db())
        {
            captainAId = db.Accounts.AsNoTracking().Single(a => a.Nickname == captainANick).Id;
            captainBId = db.Accounts.AsNoTracking().Single(a => a.Nickname == captainBNick).Id;
            db.Organisers.Add(new Organiser
            {
                Id = organiserId,
                Name = $"Veto Org {stamp}",
                Slug = $"veto-org-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Teams.AddRange(
                new Team { Id = teamAId, Name = teamAName, Tag = $"VA{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow },
                new Team { Id = teamBId, Name = teamBName, Tag = $"VB{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow });
            db.TeamMemberships.AddRange(
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamAId, AccountId = captainAId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                },
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamBId, AccountId = captainBId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });
            db.SaveChanges();
        }

        using (var db = Db(organiserId))
        {
            db.Tournaments.Add(new Tournament
            {
                Id = tournamentId,
                OrganiserId = organiserId,
                Name = $"Veto Cup {stamp}",
                Game = Game.Cs2,
                Format = TournamentFormat.SingleElimination,
                Status = TournamentStatus.Running,
                TeamSize = TeamSize.Squad,
                MaxTeams = 8,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Matches.Add(new Match
            {
                Id = matchId,
                OrganiserId = organiserId,
                TournamentId = tournamentId,
                Game = Game.Cs2,
                Status = MatchStatus.Scheduled,
                Integrity = MatchIntegrity.Confirmed,
                TeamAId = teamAId,
                TeamBId = teamBId,
                SlotAResolved = true,
                SlotBResolved = true,
                Side = BracketSide.Upper,
                Round = 1,
                Position = 0,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        check("veto: a plain public fetch renders both teams and the pre-veto lobby state", () =>
        {
            using var spectator = factory.NewClient();
            var response = spectator.GetAsync($"/pl/m/{matchId}").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.OK,
                $"the public lobby should be fetchable without SSE or login, got {(int)response.StatusCode}");

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(html.Contains(teamAName, StringComparison.Ordinal) && html.Contains(teamBName, StringComparison.Ordinal),
                "the durable lobby projection must identify both scheduled teams");
            that(html.Contains("Veto rozpocznie się, gdy obaj kapitanowie potwierdzą obecność.", StringComparison.Ordinal),
                "the initial state must explain the captain-presence gate rather than showing an inert veto");
        });

        check("veto: the second participant captain to become ready starts the durable veto", () =>
        {
            var path = $"/pl/m/{matchId}";
            var first = captainA.PostFormAsync($"{path}?handler=Ready", new()
            {
                ["Input.TeamId"] = teamAId.ToString(),
            }, path).GetAwaiter().GetResult();
            that(first.StatusCode == HttpStatusCode.Redirect,
                $"the first captain readiness should use post/redirect/get, got {(int)first.StatusCode}");

            using var spectator = factory.NewClient();
            var waiting = WebUtility.HtmlDecode(
                spectator.GetStringAsync(path).GetAwaiter().GetResult());
            that(waiting.Contains($"{teamAName} · Gotowy", StringComparison.Ordinal),
                "the first side's readiness must survive a plain spectator reload");
            that(waiting.Contains($"{teamBName} · Czeka", StringComparison.Ordinal),
                "one captain cannot start the timer alone");

            var second = captainB.PostFormAsync($"{path}?handler=Ready", new()
            {
                ["Input.TeamId"] = teamBId.ToString(),
            }, path).GetAwaiter().GetResult();
            that(second.StatusCode == HttpStatusCode.Redirect,
                $"the second captain readiness should start then redirect, got {(int)second.StatusCode}");

            var started = WebUtility.HtmlDecode(
                spectator.GetStringAsync(path).GetAwaiter().GetResult());
            that(started.Contains("Veto trwa", StringComparison.Ordinal),
                "both durable readiness facts should move the lobby into the veto state");
            that(started.Contains("http-equiv=\"refresh\" content=\"5\"", StringComparison.OrdinalIgnoreCase),
                "the no-JavaScript waiting view must refresh while the veto is active");
        });

        check("veto: a participant captain acting out of turn is rejected without changing state", () =>
        {
            var path = $"/pl/m/{matchId}";
            var response = captainB.PostFormAsync($"{path}?handler=Veto", new()
            {
                ["Input.TeamId"] = teamBId.ToString(),
                ["Input.Map"] = "de_dust2",
            }, path).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Conflict,
                $"a participant acting on the other side's turn should get 409, got {(int)response.StatusCode}");

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(
                spectator.GetStringAsync(path).GetAwaiter().GetResult());
            that(html.Contains($"Ruch: {teamAName}", StringComparison.Ordinal),
                "the rejected post must leave the original captain's turn visible on reload");
            that(!html.Contains("Zbanowano: de_dust2", StringComparison.Ordinal),
                "a rejected map choice must leave no action in the durable veto strip");
        });

        check("veto: a legal ban is durable and advances the preset turn", () =>
        {
            var path = $"/pl/m/{matchId}";
            var response = captainA.PostFormAsync($"{path}?handler=Veto", new()
            {
                ["Input.TeamId"] = teamAId.ToString(),
                ["Input.Map"] = "de_dust2",
            }, path).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a legal first ban should use post/redirect/get, got {(int)response.StatusCode}");

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(
                spectator.GetStringAsync(path).GetAwaiter().GetResult());
            that(html.Contains("Zbanowano: de_dust2", StringComparison.Ordinal),
                "the veto strip must rebuild the accepted action from durable state");
            that(html.Contains($"Ruch: {teamBName}", StringComparison.Ordinal),
                "the preset must advance to the other captain after the accepted ban");
        });

        check("veto: an already used map is rejected and the same turn remains", () =>
        {
            var path = $"/pl/m/{matchId}";
            var response = captainB.PostFormAsync($"{path}?handler=Veto", new()
            {
                ["Input.TeamId"] = teamBId.ToString(),
                ["Input.Map"] = "de_dust2",
            }, path).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Conflict,
                $"reusing a banned map should be a state conflict, got {(int)response.StatusCode}");

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(
                spectator.GetStringAsync(path).GetAwaiter().GetResult());
            that(html.Split("Zbanowano: de_dust2", StringSplitOptions.None).Length - 1 == 1,
                "the rejected duplicate must not append a second veto action");
            that(html.Contains($"Ruch: {teamBName}", StringComparison.Ordinal),
                "an illegal choice must not consume the captain's turn");
        });

        check("veto: a plain read after the deadline makes one legal automatic choice", () =>
        {
            clock.Advance(TimeSpan.FromSeconds(31));

            var path = $"/pl/m/{matchId}";
            using var spectator = factory.NewClient();
            var response = spectator.GetAsync(path).GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.OK,
                $"lazy expiry should resolve inside the ordinary lobby read, got {(int)response.StatusCode}");

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(html.Contains("Automatyczny wybór", StringComparison.Ordinal),
                "the veto strip must disclose that expiry, not a captain, made the choice");
            that(html.Contains($"Ruch: {teamAName}", StringComparison.Ordinal),
                "one expired Team B turn should advance to Team A with a fresh deadline");
            that(html.Split("Automatyczny wybór", StringSplitOptions.None).Length - 1 == 1,
                "one read resolves one expired turn rather than fast-forwarding the whole veto");
        });

        check("veto: the final BO1 ban freezes the decided map and starting side", () =>
        {
            var path = $"/pl/m/{matchId}";
            while (true)
            {
                VetoTurn? turn;
                using (var db = Db(organiserId))
                {
                    var veto = db.MatchVetos.AsNoTracking().Single(v => v.MatchId == matchId);
                    var actions = db.MatchVetoActions.AsNoTracking()
                        .Where(a => a.MatchVetoId == veto.Id)
                        .OrderBy(a => a.Sequence)
                        .Select(a => new VetoAction(a.Sequence, a.TeamId, a.Kind, a.Map, a.Side, a.Automatic))
                        .ToArray();
                    turn = VetoRules.Next(veto.BestOf, teamAId, teamBId, veto.MapPool, actions);
                }

                if (turn is null) break;
                that(turn.Kind != VetoActionKind.ChooseSide,
                    "BO1 has no picked map, so it must not invent a captain side-choice turn");
                var captain = turn.TeamId == teamAId ? captainA : captainB;
                var response = captain.PostFormAsync($"{path}?handler=Veto", new()
                {
                    ["Input.TeamId"] = turn.TeamId.ToString(),
                    ["Input.Map"] = turn.LegalMaps[0],
                }, path).GetAwaiter().GetResult();
                that(response.StatusCode == HttpStatusCode.Redirect,
                    $"each remaining legal ban should redirect, got {(int)response.StatusCode}");
            }

            using var completedDb = Db(organiserId);
            var completedVeto = completedDb.MatchVetos.AsNoTracking().Single(v => v.MatchId == matchId);
            var match = completedDb.Matches.AsNoTracking().Single(m => m.Id == matchId);
            var maps = completedDb.MatchMaps.AsNoTracking()
                .Where(m => m.MatchId == matchId)
                .OrderBy(m => m.Order)
                .ToArray();
            that(completedVeto.CompletedAt is not null, "the terminal veto must be frozen as complete");
            that(maps.Length == 1 && maps[0].Map == match.Map,
                "the BO1 decider must be stored both as ordered config and the match's primary map");
            that(maps[0].TeamAStartsAs is VetoStartingSide.Ct or VetoStartingSide.T,
                "the server-facing map must have a decided side, never an implicit default");
            that(match.Status == MatchStatus.AwaitingPlayers,
                "a completed veto must make the match config ready for player arrival");

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator.GetStringAsync(path).GetAwaiter().GetResult());
            that(html.Contains("Veto zakończone", StringComparison.Ordinal),
                "the durable lobby must distinguish completion from waiting for captains");
            that(!html.Contains("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase),
                "a completed veto must stop the polling fallback");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM matches WHERE id={0}; " +
                "DELETE FROM tournaments WHERE id={1}; " +
                "DELETE FROM team_memberships WHERE team_id IN ({2}, {3}); " +
                "DELETE FROM teams WHERE id IN ({2}, {3}); " +
                "DELETE FROM sessions WHERE account_id IN ({5}, {6}); " +
                "DELETE FROM accounts WHERE id IN ({5}, {6}); " +
                "DELETE FROM organisers WHERE id={4};",
                matchId, tournamentId, teamAId, teamBId, organiserId, captainAId, captainBId);
        }
    }
}
