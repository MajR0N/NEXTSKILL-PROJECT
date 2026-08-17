using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Auth;

namespace NextSkill.Checks;

/// <summary>
/// The three production debts (decisions 011, 012, 042).
///
/// Each was recorded as a <c>ponytail:</c> ceiling with a named upgrade path.
/// These are the checks that make the upgrades real rather than asserted.
/// </summary>
public static class SecretHandlingChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    /// <summary>Captures everything the application logs, so it can be searched.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public readonly ConcurrentBag<string> Lines = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Lines);
        public void Dispose() { }

        private sealed class Capturing(ConcurrentBag<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                sink.Add(formatter(state, exception));
                // The exception chain too: a secret passed as a parameter to a
                // failing call ends up in a message nobody meant to write.
                if (exception is not null) sink.Add(exception.ToString());
            }
        }
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        // ── debt 2: the limiter behind a proxy ────────────────────────────
        check("proxies: an untrusted X-Forwarded-For cannot move the rate-limit bucket", () =>
        {
            var options = new ForwardedHeadersOptions();
            TrustedProxies.Configure(options, []);

            that(options.ForwardedHeaders == ForwardedHeaders.None,
                "with nothing trusted the middleware must be inert — otherwise anyone mints a " +
                "fresh bucket per request by changing one header, which removes the limiter entirely");
            that(options.KnownNetworks.Count == 0 && options.KnownProxies.Count == 0,
                "and the framework's default loopback trust must be cleared: this application's " +
                "own health checks arrive on loopback and speak for nobody");
        });

        check("proxies: Cloudflare's ranges are trusted, and only for one hop", () =>
        {
            var options = new ForwardedHeadersOptions();
            TrustedProxies.Configure(options, TrustedProxies.CloudflareRanges);

            that(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor),
                "the visitor's address must be recovered, or the whole internet shares one bucket (042)");
            that(options.KnownNetworks.Count == TrustedProxies.CloudflareRanges.Length,
                $"every published range must parse, got {options.KnownNetworks.Count}");
            that(options.ForwardLimit == 1,
                "ONE hop. Walking further back starts reading values the CLIENT supplied, " +
                "which is the spoofing this exists to prevent");

            that(options.KnownNetworks.Any(n => n.Contains(IPAddress.Parse("104.16.0.1"))),
                "a real Cloudflare edge address must be recognised");
            that(!options.KnownNetworks.Any(n => n.Contains(IPAddress.Parse("203.0.113.7"))),
                "and an ordinary internet address must not be");
        });

        check("proxies: a malformed range is dropped, never trusted by accident", () =>
        {
            var options = new ForwardedHeadersOptions();
            TrustedProxies.Configure(options, ["not-a-cidr", "10.0.0.0/99", "10.0.0.0/8"]);

            that(options.KnownNetworks.Count == 1,
                $"only the valid range may be trusted, got {options.KnownNetworks.Count}");
        });

        // ── debt 1 + 3, against the real application ──────────────────────
        var logs = new CapturingLoggerProvider();

        using var factory = TestHost.Create(builder => builder.ConfigureServices(services =>
        {
            services.AddLogging(logging =>
            {
                logging.AddProvider(logs);
                logging.SetMinimumLevel(LogLevel.Trace);
            });
        }));

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var organiserId = Guid.NewGuid();
        var nickname = $"Sec{stamp}";
        const string password = "a-long-enough-passphrase";
        var email = $"{nickname.ToLowerInvariant()}@nextskill.test";

        using var owner = factory.NewClient();
        owner.PostFormAsync("/pl/account/register", new()
        {
            ["Input.Nickname"] = nickname,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();

        Guid accountId;
        using (var db = Db())
        {
            accountId = db.Accounts.Single(a => a.Nickname == nickname).Id;
            db.Organisers.Add(new Organiser
            {
                Id = organiserId, Name = $"Secret Org {stamp}", Slug = $"secret-org-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.OrganiserStaff.Add(new OrganiserStaff
            {
                Id = Guid.NewGuid(), OrganiserId = organiserId, AccountId = accountId,
                Role = NextSkill.Domain.Access.OrganiserRole.Owner, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        string issuedSecret = "";
        Guid serverId = Guid.Empty;

        check("secrets: an HMAC secret is ENCRYPTED at rest, not stored in the clear", () =>
        {
            var response = owner.PostFormAsync("/pl/org/servers", new()
            {
                ["Input.OrganiserId"] = organiserId.ToString(),
                ["Input.Host"] = "secret.example.test",
                ["Input.Port"] = "27015",
                ["Input.Game"] = Game.Cs2.ToString(),
            }).GetAwaiter().GetResult();

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            const string marker = "id=\"server-secret\">";
            var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            issuedSecret = html[start..html.IndexOf('<', start)].Trim();

            that(issuedSecret.Length >= 32, "the one-time secret must still be shown to the organiser");

            using var db = Db(organiserId);
            var server = db.Servers.AsNoTracking()
                .Single(s => s.Host == "secret.example.test" && s.Port == 27015);
            serverId = server.Id;

            that(server.HmacSecret != issuedSecret,
                "THE DATABASE MUST NOT CONTAIN THE PLAINTEXT — a stolen dump would otherwise " +
                "hand over a working signing credential (012)");
            that(SecretProtector.IsProtected(server.HmacSecret),
                "and the stored value must be recognisably encrypted, not merely different");
        });

        check("secrets: the encrypted credential still verifies a real signed request", () =>
        {
            // Encryption that broke signing would be a worse outcome than the
            // debt it repaid, so this drives the actual plugin pipeline.
            using var http = factory.NewClient();
            var sim = new NextSkill.Simulator.PluginSimulator(http, serverId, issuedSecret);

            var accepted = sim.Heartbeat("1.0.0").GetAwaiter().GetResult();
            that(accepted.IsSuccessStatusCode,
                $"a correctly signed heartbeat must still be accepted, got {(int)accepted.StatusCode}");

            using var wrong = factory.NewClient();
            var impostor = new NextSkill.Simulator.PluginSimulator(wrong, serverId, "not-the-secret");
            var refused = impostor.Heartbeat().GetAwaiter().GetResult();
            that((int)refused.StatusCode == 401,
                $"and a wrong secret must still be refused, got {(int)refused.StatusCode}");
        });

        var totpSecret = "";

        check("secrets: a TOTP seed is ENCRYPTED at rest and still verifies", () =>
        {
            // Enable TOTP through the real pages, then prove the seed the code
            // was derived from is not what the database holds.
            var page = owner.PostFormAsync("/pl/account/two-factor?handler=StartTotp", new(),
                    "/pl/account/two-factor").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var match = Regex.Match(page, @"name=""PendingSecret"" value=""([A-Z2-7]+)""");
            that(match.Success, "the setup page must offer a secret to enrol");
            totpSecret = match.Groups[1].Value;

            var enabled = owner.PostFormAsync("/pl/account/two-factor?handler=ConfirmTotp", new()
            {
                ["PendingSecret"] = totpSecret,
                ["Code"] = Totp.Compute(totpSecret, DateTimeOffset.UtcNow),
            }, "/pl/account/two-factor").GetAwaiter().GetResult();

            that(enabled.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
                $"enrolment should succeed, got {(int)enabled.StatusCode}");

            using var db = Db();
            var account = db.Accounts.AsNoTracking().Single(a => a.Id == accountId);

            that(account.TwoFactorSecret is not null, "arrange: the factor is enabled");
            that(account.TwoFactorSecret != totpSecret,
                "A DUMP MUST NOT YIELD THE SEED — otherwise an attacker generates valid codes " +
                "for every account and the second factor is defeated entirely (012, 047)");
            that(SecretProtector.IsProtected(account.TwoFactorSecret),
                "and it must be recognisably encrypted");
        });

        check("secrets: a legacy plaintext value is encrypted on the next startup", () =>
        {
            // Simulates a row written before encryption existed. The migration
            // could not do this — it has no access to the key ring — so startup
            // is the only place the window can be closed.
            using (var db = Db(organiserId))
            {
                db.Servers.Single(s => s.Id == serverId).HmacSecret = "legacy-plaintext-value";
                db.SaveChanges();
            }

            using (var scope = factory.Services.CreateScope())
            {
                var protector = scope.ServiceProvider.GetRequiredService<SecretProtector>();
                var scoped = scope.ServiceProvider.GetRequiredService<NextSkillDb>();
                var changed = protector.BackfillAsync(scoped).GetAwaiter().GetResult();
                that(changed >= 1, $"the backfill must find the legacy row, changed {changed}");

                // Idempotent: a second boot must find nothing left to do.
                that(protector.BackfillAsync(scoped).GetAwaiter().GetResult() == 0,
                    "and running twice must be a no-op, or every restart rewrites every secret");
            }

            using var db2 = Db(organiserId);
            var stored = db2.Servers.AsNoTracking().Single(s => s.Id == serverId).HmacSecret;
            that(SecretProtector.IsProtected(stored), "the legacy value must now be encrypted");
            that(stored != "legacy-plaintext-value", "and must not still be readable");
        });

        // ── debt 3: the check 012 asked for and nobody had written ────────
        check("secrets: NO SECRET APPEARS IN ANY LOG", () =>
        {
            // Exercise the paths that handle secrets, including ones that fail —
            // a failing call is where a value most often lands in a message
            // nobody intended to write.
            owner.PostFormAsync("/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            owner.PostFormAsync("/pl/account/forgot", new()
            {
                ["Input.Email"] = email,
            }).GetAwaiter().GetResult();

            using (var badly = factory.NewClient())
            {
                var sim = new NextSkill.Simulator.PluginSimulator(badly, serverId, issuedSecret);
                sim.Heartbeat().GetAwaiter().GetResult();
                sim.MapEnd(Guid.NewGuid(), 1, "de_dust2", 16, 4, 20).GetAwaiter().GetResult();
            }

            string? resetToken;
            using (var db = Db())
            {
                resetToken = db.PasswordResetTokens.AsNoTracking()
                    .Where(t => t.AccountId == accountId)
                    .Select(t => t.TokenHash)
                    .FirstOrDefault();
            }

            var captured = string.Join("\n", logs.Lines);

            that(captured.Length > 0,
                "arrange: the capturing provider must actually be receiving log output, " +
                "or this check passes by seeing nothing");

            // Every recoverable secret this session produced.
            var forbidden = new List<(string What, string Value)>
            {
                ("the server HMAC secret", issuedSecret),
                ("the TOTP seed", totpSecret),
                ("the account password", password),
            };

            if (resetToken is not null) forbidden.Add(("the password-reset token hash", resetToken));

            foreach (var (what, value) in forbidden)
            {
                that(!string.IsNullOrEmpty(value), $"arrange: {what} must be non-empty to be searched for");
                that(!captured.Contains(value, StringComparison.Ordinal),
                    $"{what} was written to a log. This is the cheapest, most common real-world " +
                    $"leak there is, and 012's check 5 exists for exactly it");
            }
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM audit_entries WHERE actor_account_id={0}; " +
                "DELETE FROM password_reset_tokens WHERE account_id={0}; " +
                "DELETE FROM recovery_codes WHERE account_id={0}; " +
                "DELETE FROM servers WHERE organiser_id={1}; " +
                "DELETE FROM organiser_staff WHERE account_id={0}; " +
                "DELETE FROM sessions WHERE account_id={0}; " +
                "DELETE FROM accounts WHERE id={0}; " +
                "DELETE FROM organisers WHERE id={1};",
                accountId, organiserId);
        }
    }
}
