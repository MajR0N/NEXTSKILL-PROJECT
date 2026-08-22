using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;

namespace NextSkill.Checks;

/// <summary>
/// The staff panel (decisions 012 §7, 015, 036).
///
/// Two gaps closed here had teeth. <b>The <c>trusted</c> flag could only be set
/// by editing the database by hand</b>, while it now decides whose results skip
/// the challenge window (050 §5) — and partner #1 is meant to be trusted.
/// <b>The audit log had no reader</b>, which made it storage rather than
/// security: an append-only record nobody can look at protects nothing.
/// </summary>
public static class StaffChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = TestHost.Create();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(null));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var organiserId = Guid.NewGuid();
        const string password = "a-long-enough-passphrase";

        var ownerNick = $"Own{stamp}";
        var staffNick = $"Stf{stamp}";
        var playerNick = $"Ply{stamp}";

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

        Guid ownerId, staffId, playerId;

        using (var db = Db())
        {
            ownerId = db.Accounts.Single(a => a.Nickname == ownerNick).Id;
            staffId = db.Accounts.Single(a => a.Nickname == staffNick).Id;
            playerId = db.Accounts.Single(a => a.Nickname == playerNick).Id;

            db.Accounts.Single(a => a.Id == ownerId).PlatformRole = PlatformRole.Owner;
            db.Accounts.Single(a => a.Id == staffId).PlatformRole = PlatformRole.Staff;

            db.Organisers.Add(new Organiser
            {
                Id = organiserId,
                Name = $"Staff Org {stamp}",
                Slug = $"staff-org-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        check("panel: every section is REACHABLE — nine pages had no link anywhere", () =>
        {
            // Each slice skipped navigation because the pages beside it had
            // none, which is how a finished panel stays invisible.
            var ownerHtml = owner.GetStringAsync("/pl/admin/users").GetAwaiter().GetResult();
            foreach (var path in new[]
                     {
                         "/pl/admin/users", "/pl/admin/organisers", "/pl/admin/audit",
                         "/pl/admin/news", "/pl/admin/merge", "/pl/admin/reports",
                         "/pl/org/servers", "/pl/org/staff", "/pl/org/disputes",
                     })
            {
                that(ownerHtml.Contains($"href=\"{path}\"", StringComparison.Ordinal),
                    $"the platform owner must be able to REACH {path} without typing it");
            }

            // The referee's desk included. It used to read only the actor's OWN
            // organiser roles, so a platform account was refused the page while
            // 036 grants it ResolveDispute precisely for disputes BETWEEN
            // organisers — which have no referee inside either of them.
            foreach (var (who, browser) in new[] { ("owner", owner), ("staff", staff) })
            {
                var desk = browser.GetAsync("/pl/org/disputes").GetAwaiter().GetResult();
                that(desk.StatusCode == System.Net.HttpStatusCode.OK,
                    $"platform {who} must reach the referee's desk, got {(int)desk.StatusCode}");
            }

            // And no wider: a roleless player still gets nothing.
            var refused = player.GetAsync("/pl/org/disputes").GetAwaiter().GetResult();
            that(refused.StatusCode == System.Net.HttpStatusCode.Forbidden,
                $"while somebody with no role anywhere is still refused, got {(int)refused.StatusCode}");

            // And the nav is authority, not decoration: staff may not merge, so
            // staff are not offered it (036).
            var staffHtml = staff.GetStringAsync("/pl/admin/users").GetAwaiter().GetResult();
            that(!staffHtml.Contains("href=\"/pl/admin/merge\"", StringComparison.Ordinal),
                "a link that appears and then 403s is worse than no link");
            that(staffHtml.Contains("href=\"/pl/admin/users\"", StringComparison.Ordinal),
                "while what they CAN do is still offered");

            // An ordinary player has no panel, so no entry point either.
            var playerHtml = player.GetStringAsync("/pl/ranking").GetAwaiter().GetResult();
            that(!playerHtml.Contains("/pl/admin/", StringComparison.Ordinal),
                "and somebody with no role is shown no way in at all");
        });

        check("staff panel: an ordinary player cannot reach any of it", () =>
        {
            foreach (var path in new[] { "/pl/admin/users", "/pl/admin/organisers", "/pl/admin/audit" })
            {
                var response = player.GetAsync(path).GetAwaiter().GetResult();
                that(response.StatusCode == HttpStatusCode.Forbidden,
                    $"{path} must be refused to a player, got {(int)response.StatusCode}");
            }

            using var anonymous = factory.NewClient();
            var login = anonymous.GetAsync("/pl/admin/audit").GetAwaiter().GetResult();
            that(login.StatusCode == HttpStatusCode.Redirect,
                $"an anonymous visitor must be sent to log in, got {(int)login.StatusCode}");
        });

        check("staff panel: trusted is OWNER-only, and a staff attempt is refused and recorded", () =>
        {
            var response = staff.PostFormAsync("/pl/admin/organisers", new()
            {
                ["Input.OrganiserId"] = organiserId.ToString(),
                ["Input.Trusted"] = "true",
                ["Input.Reason"] = "I think they are fine",
            }, "/pl/admin/users").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"staff moderate people; deciding whose results skip corroboration is not theirs (036), got {(int)response.StatusCode}");

            using var db = Db();
            that(!db.Organisers.AsNoTracking().Single(o => o.Id == organiserId).Trusted,
                "and nothing may have changed");

            var denied = db.AuditEntries.AsNoTracking().Where(a =>
                a.ActorAccountId == staffId &&
                a.Action == AuditAction.SetOrganiserTrusted &&
                a.Outcome == AuditOutcome.Denied).ToList();
            that(denied.Count == 1,
                $"the refused attempt must leave a row — nothing else changed, so nothing else could; got {denied.Count}");
        });

        check("staff panel: the owner can finally mark a partner trusted, with a reason", () =>
        {
            var response = owner.PostFormAsync("/pl/admin/organisers", new()
            {
                ["Input.OrganiserId"] = organiserId.ToString(),
                ["Input.Trusted"] = "true",
                ["Input.Reason"] = "Partner umowny",
            }, "/pl/admin/organisers").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"the owner's decision should redirect, got {(int)response.StatusCode}");

            using var db = Db();
            that(db.Organisers.AsNoTracking().Single(o => o.Id == organiserId).Trusted,
                "the flag that had no user interface at all must now be settable without SQL");

            var entry = db.AuditEntries.AsNoTracking().Single(a =>
                a.ActorAccountId == ownerId && a.Action == AuditAction.SetOrganiserTrusted);
            that(entry.Reason!.Contains("Partner umowny", StringComparison.Ordinal),
                "and the decision must carry why");
        });

        check("staff panel: a ban ends live sessions immediately and explains itself at login", () =>
        {
            // The player is signed in right now, and must stop being signed in
            // on their very next request — not whenever the cookie expires.
            var before = player.GetAsync("/pl/me").GetAwaiter().GetResult();
            that(before.StatusCode == HttpStatusCode.OK,
                $"the player should have access before the ban, got {(int)before.StatusCode}");

            var banned = staff.PostFormAsync("/pl/admin/users?handler=Ban", new()
            {
                ["Input.AccountId"] = playerId.ToString(),
                ["Input.Reason"] = "Podszywanie się pod innego gracza",
            }, $"/pl/admin/users?q={playerNick}").GetAwaiter().GetResult();
            that(banned.StatusCode == HttpStatusCode.Redirect,
                $"staff must be able to moderate a player, got {(int)banned.StatusCode}");

            var after = player.GetAsync("/pl/me").GetAwaiter().GetResult();
            that(after.StatusCode == HttpStatusCode.Redirect,
                $"the live session must die on the next request, got {(int)after.StatusCode}");

            using (var db = Db())
            {
                that(!db.Sessions.AsNoTracking().Any(s => s.AccountId == playerId),
                    "and every server-side session must be gone, not merely rejected");
            }

            // The message appears only AFTER the password verified, so it never
            // becomes a way to discover which addresses exist (040).
            using var fresh = factory.NewClient();
            var login = fresh.PostFormAsync("/pl/account/login", new()
            {
                ["Input.Email"] = $"{playerNick.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            var html = WebUtility.HtmlDecode(
                login.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(html.Contains("Podszywanie się pod innego gracza", StringComparison.Ordinal),
                "a sanction nobody can understand cannot be appealed — the reason must be shown (015)");

            var wrongPassword = fresh.PostFormAsync("/pl/account/login", new()
            {
                ["Input.Email"] = $"{playerNick.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = "definitely-not-the-password",
            }).GetAwaiter().GetResult();
            var generic = WebUtility.HtmlDecode(
                wrongPassword.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(!generic.Contains("zablokowane", StringComparison.Ordinal),
                "BUT a wrong password must still give the generic answer — otherwise the ban " +
                "message becomes an account-enumeration oracle (040)");
        });

        check("staff panel: staff cannot ban each other, or the owner", () =>
        {
            var response = staff.PostFormAsync("/pl/admin/users?handler=Ban", new()
            {
                ["Input.AccountId"] = ownerId.ToString(),
                ["Input.Reason"] = "Bo tak",
            }, $"/pl/admin/users?q={ownerNick}").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"one compromised staff account must not be able to lock out the people who would notice, got {(int)response.StatusCode}");

            using var db = Db();
            that(db.Accounts.AsNoTracking().Single(a => a.Id == ownerId).BannedAt is null,
                "the owner must remain able to log in");
        });

        check("staff panel: the audit log is READABLE — which is what makes it security", () =>
        {
            var html = WebUtility.HtmlDecode(
                owner.GetStringAsync("/pl/admin/audit").GetAwaiter().GetResult());

            that(html.Contains("SetOrganiserTrusted", StringComparison.Ordinal),
                "the trust decision must appear in the log");
            that(html.Contains("BanUser", StringComparison.Ordinal),
                "and so must the ban");
            that(html.Contains(ownerNick, StringComparison.Ordinal),
                "with the name of whoever decided");

            var denied = WebUtility.HtmlDecode(
                owner.GetStringAsync("/pl/admin/audit?deniedonly=true").GetAwaiter().GetResult());
            that(denied.Contains("odmowa", StringComparison.Ordinal),
                "and refusals must be filterable — that is where an incident review starts");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM audit_entries WHERE actor_account_id IN ({0},{1},{2}); " +
                "DELETE FROM sessions WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM accounts WHERE id IN ({0},{1},{2}); " +
                "DELETE FROM organisers WHERE id={3};",
                ownerId, staffId, playerId, organiserId);
        }
    }
}
