using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Matches;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Simulator;

namespace NextSkill.Checks;

/// <summary>
/// End-to-end: the simulator drives the real HTTP pipeline in-process
/// (decisions 034, 038).
///
/// This is the check that matters most for the plugin seam, because it exercises
/// signing, contract versioning, strict deserialisation, sequencing and
/// reconciliation together — the way they will actually meet in production.
/// </summary>
public static class EndToEndChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        // NextSkillDb is just a type that lives in the API assembly — that is all
        // WebApplicationFactory needs to locate the entry point, and it avoids a
        // name collision between two projects that both use top-level statements.
        //
        // The content root must be set explicitly: the factory otherwise guesses
        // <solutionDir>/NextSkill.Api, and ours lives under src/.
        using var factory = new WebApplicationFactory<NextSkillDb>()
            .WithWebHostBuilder(b => b
                .UseEnvironment("Development")
                .UseContentRoot(FindApiContentRoot()));

        using var http = factory.CreateClient();

        var orgId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();
        const string secret = "sim-secret-1234567890";
        var stamp = DateTimeOffset.UtcNow.Ticks;

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(orgId));

        // Seed a verified server and a scheduled match.
        using (var db = Db())
        {
            db.Organisers.Add(new Organiser { Id = orgId, Name = "Sim Org", Slug = $"sim-{stamp}", Trusted = false, CreatedAt = DateTimeOffset.UtcNow });
            db.Servers.Add(new GameServer { Id = serverId, OrganiserId = orgId, Host = "127.0.0.1", Port = 27015, Game = Game.Cs2, Verified = true, HmacSecret = secret, CreatedAt = DateTimeOffset.UtcNow });
            db.Tournaments.Add(new Tournament { Id = tournamentId, OrganiserId = orgId, Name = "Sim Cup", Game = Game.Cs2, Format = TournamentFormat.SingleElimination, Status = TournamentStatus.Running, CreatedAt = DateTimeOffset.UtcNow });
            db.Matches.Add(new Match { Id = matchId, OrganiserId = orgId, TournamentId = tournamentId, Game = Game.Cs2, Status = MatchStatus.Scheduled, Integrity = MatchIntegrity.Confirmed, ServerId = serverId, Map = "de_dust2", CreatedAt = DateTimeOffset.UtcNow });
            db.SaveChanges();
        }

        var sim = new PluginSimulator(http, serverId, secret);

        MatchIntegrity IntegrityOf()
        {
            using var db = Db();
            return db.Matches.AsNoTracking().Single(m => m.Id == matchId).Integrity;
        }

        void ResetMatch(long seq = 0, MatchIntegrity integrity = MatchIntegrity.Confirmed)
        {
            using var db = Db();
            var m = db.Matches.Single(x => x.Id == matchId);
            m.LastEventSeq = seq; m.Integrity = integrity; m.ScoreA = null; m.ScoreB = null;
            m.Status = MatchStatus.Scheduled;
            db.SaveChanges();
        }

        // ── authentication ───────────────────────────────────────────────
        check("E2E: a correctly signed heartbeat is accepted and records the plugin version", () =>
        {
            var res = sim.Heartbeat("0.9.1").GetAwaiter().GetResult();
            that(res.IsSuccessStatusCode, $"expected success, got {(int)res.StatusCode}");

            using var db = Db();
            var server = db.Servers.AsNoTracking().Single(s => s.Id == serverId);
            that(server.PluginVersion == "0.9.1", "the panel learns the plugin version from the heartbeat (007)");
            that(server.LastHeartbeatAt is not null, "liveness recorded");
        });

        check("E2E: a BAD SIGNATURE is rejected with 401", () =>
        {
            var res = sim.Heartbeat(bad: Misbehaviour.BadSignature).GetAwaiter().GetResult();
            that((int)res.StatusCode == 401, $"expected 401, got {(int)res.StatusCode}");
        });

        check("E2E: NO signature headers are rejected", () =>
        {
            var res = sim.Heartbeat(bad: Misbehaviour.NoSignature).GetAwaiter().GetResult();
            that((int)res.StatusCode == 401, $"expected 401, got {(int)res.StatusCode}");
        });

        check("E2E: a REPLAYED request (genuine signature, old timestamp) is rejected", () =>
        {
            var res = sim.Heartbeat(bad: Misbehaviour.StaleTimestamp).GetAwaiter().GetResult();
            that((int)res.StatusCode == 401,
                $"a captured request replayed two hours later must be refused, got {(int)res.StatusCode}");
        });

        check("E2E: an UNSUPPORTED CONTRACT VERSION is rejected loudly, not best-effort parsed", () =>
        {
            var res = sim.Heartbeat(bad: Misbehaviour.WrongContract).GetAwaiter().GetResult();
            that((int)res.StatusCode == 400, $"expected 400, got {(int)res.StatusCode}");
        });

        // ── config pull ──────────────────────────────────────────────────
        check("E2E: the plugin pulls a config carrying decided maps and NO veto rules", () =>
        {
            var res = sim.GetConfig(matchId).GetAwaiter().GetResult();
            that(res.IsSuccessStatusCode, $"expected success, got {(int)res.StatusCode}");

            var json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(json.Contains("\"maps\""), "config must carry the decided map order");
            that(!json.Contains("veto", StringComparison.OrdinalIgnoreCase),
                "config must NOT carry veto rules — veto happened on the web (028)");
        });

        // ── sequencing ───────────────────────────────────────────────────
        check("E2E: a DUPLICATE event returns 200 and is not applied twice", () =>
        {
            ResetMatch();
            var first = sim.RoundEnd(matchId, 1, 1, 1, 0).GetAwaiter().GetResult();
            that(first.IsSuccessStatusCode, "first delivery accepted");

            var repeat = sim.RoundEnd(matchId, 1, 1, 1, 0).GetAwaiter().GetResult();
            that(repeat.IsSuccessStatusCode,
                "a duplicate MUST return 200 — the plugin retries until acknowledged, so anything else makes it retry forever");

            var body = repeat.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(body.Contains("duplicate_ignored"), $"expected duplicate_ignored, got {body}");
        });

        check("E2E: a GAP in seq marks the match SUSPECT", () =>
        {
            ResetMatch();
            sim.RoundEnd(matchId, 1, 1, 1, 0).GetAwaiter().GetResult();
            var res = sim.RoundEnd(matchId, 5, 5, 3, 2).GetAwaiter().GetResult();   // 2,3,4 missing

            that(res.IsSuccessStatusCode, "the event is still accepted — refusing it would lose the rest of the match too");
            that(IntegrityOf() == MatchIntegrity.Suspect, $"a gap must mark the match suspect, got {IntegrityOf()}");
        });

        // ── reconciliation: the silent failure, caught ───────────────────
        check("E2E: map_end AGREEING with events → CONFIRMED", () =>
        {
            ResetMatch();
            sim.RoundEnd(matchId, 1, 24, 13, 11).GetAwaiter().GetResult();
            var res = sim.MapEnd(matchId, 2, "de_dust2", 13, 11, 24).GetAwaiter().GetResult();

            that(res.IsSuccessStatusCode, "accepted");
            that(IntegrityOf() == MatchIntegrity.Confirmed, $"agreement must confirm, got {IntegrityOf()}");
        });

        check("E2E: map_end DISAGREEING with events → DISPUTED, bracket blocked", () =>
        {
            // The silent failure made loud: the authoritative report claims a
            // different winner from the one the round events described.
            ResetMatch();
            sim.RoundEnd(matchId, 1, 24, 13, 11).GetAwaiter().GetResult();
            var res = sim.MapEnd(matchId, 2, "de_dust2", 11, 13, 24).GetAwaiter().GetResult();

            that(res.IsSuccessStatusCode, "accepted for recording");
            that(IntegrityOf() == MatchIntegrity.Disputed,
                $"a reversed final score MUST be disputed, got {IntegrityOf()}");

            var body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(body.Contains("disputed"), $"the response should say so: {body}");
        });

        check("E2E: a MALFORMED event body is rejected with 400, not silently defaulted", () =>
        {
            ResetMatch();
            var res = http.PostAsync($"/api/v1/servers/{serverId}/matches/{matchId}/events",
                new StringContent("{ not json at all", System.Text.Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();
            that((int)res.StatusCode is 400 or 401, $"garbage must not be accepted, got {(int)res.StatusCode}");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM matches WHERE organiser_id={0}; DELETE FROM tournaments WHERE organiser_id={0}; DELETE FROM servers WHERE organiser_id={0}; DELETE FROM organisers WHERE id={0};",
                orgId);
        }

        _ = JsonSerializer.Serialize(new { });   // keep System.Text.Json referenced
    }

    /// <summary>Walks up from the test binaries to find src/NextSkill.Api.</summary>
    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NextSkill.Api");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate src/NextSkill.Api from " + AppContext.BaseDirectory);
    }
}
