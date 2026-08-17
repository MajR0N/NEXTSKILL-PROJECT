using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Simulator;

namespace NextSkill.Checks;

/// <summary>
/// Organiser server registration through the real browser pipeline
/// (decisions 007, 036, 051).
/// </summary>
public static class ServerRegistrationChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = TestHost.Create();

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var organiserId = Guid.NewGuid();
        var rivalOrganiserId = Guid.NewGuid();
        var nickname = $"Srv{stamp}";
        const string password = "a-long-enough-passphrase";
        string? issuedSecret = null;
        Guid? registeredServerId = null;
        Guid accountId;

        using var owner = factory.NewClient();
        owner.PostFormAsync("/pl/account/register", new()
        {
            ["Input.Nickname"] = nickname,
            ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();

        using (var db = Db())
        {
            accountId = db.Accounts.AsNoTracking().Single(a => a.Nickname == nickname).Id;
            db.Organisers.Add(new Organiser
            {
                Id = organiserId,
                Name = $"Server Org {stamp}",
                Slug = $"server-org-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Organisers.Add(new Organiser
            {
                Id = rivalOrganiserId,
                Name = $"Rival Server Org {stamp}",
                Slug = $"rival-server-org-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();

            using var tenantDb = Db(organiserId);
            tenantDb.OrganiserStaff.Add(new OrganiserStaff
            {
                Id = Guid.NewGuid(),
                OrganiserId = organiserId,
                AccountId = accountId,
                Role = OrganiserRole.Owner,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            tenantDb.SaveChanges();
        }

        check("servers: an organiser registers an unverified endpoint and receives its secret once", () =>
        {
            var response = owner.PostFormAsync("/pl/org/servers", new()
            {
                ["Input.OrganiserId"] = organiserId.ToString(),
                ["Input.Host"] = "cs.example.test",
                ["Input.Port"] = "27015",
                ["Input.Game"] = Game.Cs2.ToString(),
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                $"registration should render the one-time secret, got {(int)response.StatusCode}");

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            const string marker = "id=\"server-secret\">";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            that(start >= 0, "the response must expose the newly generated secret in a stable one-time element");
            start += marker.Length;
            var secret = html[start..html.IndexOf('<', start)].Trim();
            issuedSecret = secret;

            using var db = Db(organiserId);
            var server = db.Servers.AsNoTracking().Single(s => s.Host == "cs.example.test" && s.Port == 27015);
            registeredServerId = server.Id;
            that(!server.Verified, "display data alone must never verify a server");
            that(secret.Length >= 32,
                "the displayed credential must be high-entropy");

            // Encrypted at rest (012). The heartbeat check further down proves
            // the stored value is still the working credential — which is the
            // property that actually matters, and the one an equality assertion
            // against the plaintext was only ever standing in for.
            that(server.HmacSecret != secret,
                "the database must NOT hold the plaintext signing credential");
            that(NextSkill.Api.Shared.Auth.SecretProtector.IsProtected(server.HmacSecret),
                "and the stored value must be recognisably encrypted");

            // 036 lists server registration among the actions requiring a
            // recorded human decision, and 012 §7 adds "never the secret
            // involved" — so the row is asserted to exist AND to be clean.
            var audit = db.AuditEntries.AsNoTracking()
                .Single(a => a.ResourceId == server.Id && a.Action == AuditAction.RegisterServer);
            that(audit.ActorAccountId == accountId && audit.Outcome == AuditOutcome.Allowed,
                "the registration must name who decided it");
            that(audit.Reason == "cs.example.test:27015",
                "the audit row identifies the endpoint by its public address");
            that(!audit.Reason!.Contains(secret, StringComparison.Ordinal),
                "NO SECRET APPEARS IN THE AUDIT LOG — the one leak 012's check 5 exists to catch");
        });

        check("servers: later health views show the endpoint but never return its secret", () =>
        {
            var response = owner.GetAsync("/pl/org/servers").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.OK,
                $"the owner should be able to revisit server health, got {(int)response.StatusCode}");

            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(html.Contains("cs.example.test:27015", StringComparison.Ordinal),
                "the registered endpoint must remain visible as operational status");
            that(issuedSecret is { Length: > 0 } && !html.Contains(issuedSecret, StringComparison.Ordinal),
                "the signing secret must not be selected or rendered after its creation response");
            that(response.Headers.CacheControl?.NoStore == true,
                "the credential-bearing page must remain non-cacheable even on later views");
        });

        check("servers: an organiser id in the form is not authority over another tenant", () =>
        {
            var response = owner.PostFormAsync("/pl/org/servers", new()
            {
                ["Input.OrganiserId"] = rivalOrganiserId.ToString(),
                ["Input.Host"] = "rival.example.test",
                ["Input.Port"] = "27016",
                ["Input.Game"] = Game.Cs2.ToString(),
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"tampering the tenant id must be refused, got {(int)response.StatusCode}");

            using var rivalDb = Db(rivalOrganiserId);
            that(!rivalDb.Servers.AsNoTracking().Any(),
                "a denied cross-tenant registration must leave the rival tenant unchanged");

            // THE CASE THAT FORCES AN AUDIT TABLE TO EXIST (051 §7). The attempt
            // changed no row, so per-row attribution has nothing to attach to —
            // without this table a refused privileged request leaves no trace at
            // all, which is precisely the evidence an incident needs.
            var denied = rivalDb.AuditEntries.AsNoTracking()
                .Where(a => a.OrganiserId == rivalOrganiserId &&
                            a.Action == AuditAction.RegisterServer &&
                            a.Outcome == AuditOutcome.Denied)
                .ToList();
            that(denied.Count == 1, $"a refused privileged attempt must be recorded, got {denied.Count}");
            that(denied[0].ActorAccountId == accountId,
                "and it must name who tried");
            that(denied[0].ResourceId is null,
                "there is no resource — nothing was created, which is the whole point");
        });

        check("servers: registration accepts host data only, never a URL or invalid port", () =>
        {
            var response = owner.PostFormAsync("/pl/org/servers", new()
            {
                ["Input.OrganiserId"] = organiserId.ToString(),
                ["Input.Host"] = "https://cs.example.test/status",
                ["Input.Port"] = "0",
                ["Input.Game"] = Game.Cs2.ToString(),
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                $"invalid display data should re-render the form, got {(int)response.StatusCode}");

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(html.Contains("Podaj poprawną nazwę hosta lub adres IP.", StringComparison.Ordinal),
                "the form must explain that schemes and paths are not host data");
            that(html.Contains("Port musi mieścić się w zakresie od 1 do 65535.", StringComparison.Ordinal),
                "the form must reject port zero explicitly");

            using var db = Db(organiserId);
            that(db.Servers.AsNoTracking().Count() == 1,
                "invalid display data must not create another credential");
        });

        check("servers: the same endpoint cannot mint a second credential", () =>
        {
            var response = owner.PostFormAsync("/pl/org/servers", new()
            {
                ["Input.OrganiserId"] = organiserId.ToString(),
                ["Input.Host"] = "CS.EXAMPLE.TEST",
                ["Input.Port"] = "27015",
                ["Input.Game"] = Game.Cs2.ToString(),
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                $"a duplicate should re-render the existing inventory, got {(int)response.StatusCode}");

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(html.Contains("Ten host i port są już zarejestrowane", StringComparison.Ordinal),
                "the organiser needs a safe duplicate explanation rather than a database error");

            using var db = Db(organiserId);
            that(db.Servers.AsNoTracking().Count() == 1,
                "host normalization plus the database constraint must preserve one credential per endpoint");
        });

        check("servers: an invalid heartbeat cannot verify a registered endpoint", () =>
        {
            var simulator = new PluginSimulator(owner, registeredServerId!.Value, issuedSecret!);
            var response = simulator.Heartbeat(bad: Misbehaviour.BadSignature).GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.Unauthorized,
                $"the signing boundary should reject the heartbeat, got {(int)response.StatusCode}");

            using var db = Db(organiserId);
            var server = db.Servers.AsNoTracking().Single(s => s.Id == registeredServerId);
            that(!server.Verified && server.LastHeartbeatAt is null,
                "rejected traffic must not change verification or liveness state");
        });

        check("servers: the first correctly signed heartbeat verifies the endpoint", () =>
        {
            var simulator = new PluginSimulator(owner, registeredServerId!.Value, issuedSecret!);
            var response = simulator.Heartbeat("1.0.0").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.OK,
                $"the registered credential should authenticate, got {(int)response.StatusCode}");

            using var db = Db(organiserId);
            var server = db.Servers.AsNoTracking().Single(s => s.Id == registeredServerId);
            that(server.Verified,
                "a valid signed heartbeat is the proof that turns the display record into a verified server");
            that(server.PluginVersion == "1.0.0" && server.LastHeartbeatAt is not null,
                "verification must retain the plugin version and liveness observed in the same heartbeat");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM servers WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM organiser_staff WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM organisers WHERE id IN ({0}, {1}); " +
                "DELETE FROM sessions WHERE account_id={2}; " +
                "DELETE FROM accounts WHERE id={2};",
                organiserId, rivalOrganiserId, accountId);
        }
    }
}
