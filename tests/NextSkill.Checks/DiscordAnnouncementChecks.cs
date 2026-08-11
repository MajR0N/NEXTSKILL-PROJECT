using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NextSkill.Api.Features.Discord;
using NextSkill.Api.Features.Tournaments;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Tournaments;
using NextSkill.Simulator;

namespace NextSkill.Checks;

/// <summary>
/// Exercises the credential, SSRF, transactional-outbox and retry boundaries
/// of outgoing tournament announcements (decision 014).
/// </summary>
public static class DiscordAnnouncementChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    private sealed class FakeTransport : IDiscordWebhookTransport
    {
        public DiscordSendResult Result { get; set; } = new(true, "ok");
        public Exception? Throw { get; set; }
        public List<(Uri Webhook, string Payload)> Sent { get; } = [];

        public Task<DiscordSendResult> SendAsync(
            Uri webhook,
            System.Text.Json.JsonDocument payload,
            CancellationToken cancellationToken)
        {
            if (Throw is { } exception) throw exception;
            Sent.Add((webhook, payload.RootElement.GetRawText()));
            return Task.FromResult(Result);
        }
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        const string webhookId = "123456789012345678";
        const string webhookToken = "AbCdEfghIjklMNopQRst_uvWXYZ-123456789";
        const string webhookUrl = $"https://discord.com/api/webhooks/{webhookId}/{webhookToken}";

        check("discord: webhook validation and DNS policy close the SSRF boundary", () =>
        {
            that(DiscordWebhookUrls.TryParse(webhookUrl, out var parsed, out _),
                "the canonical Discord HTTPS webhook form must be accepted");
            that(parsed?.Host == "discord.com", "the accepted host must be exact");

            foreach (var invalid in new[]
            {
                webhookUrl.Replace("https://", "http://"),
                webhookUrl.Replace("discord.com", "discord.com.evil.test"),
                webhookUrl.Replace("discord.com", "discord.com@127.0.0.1"),
                webhookUrl + "?redirect=https://127.0.0.1",
                "https://127.0.0.1/api/webhooks/123456789012345678/AbCdEfghIjklMNopQRst_uvWXYZ",
            })
                that(!DiscordWebhookUrls.TryParse(invalid, out _, out _), $"must reject {invalid}");

            foreach (var blocked in new[]
            {
                IPAddress.Loopback,
                IPAddress.Parse("10.0.0.1"),
                IPAddress.Parse("100.64.0.1"),
                IPAddress.Parse("169.254.169.254"),
                IPAddress.Parse("192.168.1.1"),
                IPAddress.IPv6Loopback,
                IPAddress.Parse("fd00::1"),
            })
                that(!DiscordWebhookTransport.IsPublicAddress(blocked), $"must block {blocked}");

            that(DiscordWebhookTransport.IsPublicAddress(IPAddress.Parse("1.1.1.1")),
                "a public IPv4 address must remain reachable");
            that(DiscordWebhookTransport.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")),
                "a public IPv6 address must remain reachable");
        });

        check("discord: the stored credential is protected and tampering fails closed", () =>
        {
            var protector = new DiscordWebhookProtector(new EphemeralDataProtectionProvider());
            var encrypted = protector.Protect(new Uri(webhookUrl));
            that(!encrypted.Contains(webhookToken, StringComparison.Ordinal),
                "ciphertext must not contain the webhook token");
            that(protector.TryUnprotect(encrypted, out var roundTrip) && roundTrip?.AbsoluteUri == webhookUrl,
                "the application must recover its own protected credential");
            that(!protector.TryUnprotect(encrypted + "tampered", out _),
                "modified ciphertext must be rejected");
        });

        var fake = new FakeTransport();
        using var factory = TestHost.Create(builder =>
        {
            builder.UseSetting("DiscordAnnouncements:PublicOrigin", "https://nextskill.test");
            builder.ConfigureServices(services =>
            {
                var worker = services.FirstOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(DiscordAnnouncementWorker));
                if (worker is not null) services.Remove(worker);
                services.RemoveAll<IDiscordWebhookTransport>();
                services.AddSingleton<IDiscordWebhookTransport>(fake);
            });
        });

        NextSkillDb Db(Guid? organiserId = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(organiserId));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var orgId = Guid.NewGuid();
        var tournamentId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var teamAId = Guid.NewGuid();
        var teamBId = Guid.NewGuid();
        const string pluginSecret = "discord-check-plugin-secret";
        const string password = "a-long-enough-passphrase";
        var ownerNick = $"DOrg{stamp}";
        var outsiderNick = $"DOut{stamp}";

        HttpClient SignedInAs(string nickname)
        {
            var http = factory.NewClient();
            var response = http.PostFormAsync("/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.Redirect, $"registration failed: {(int)response.StatusCode}");
            return http;
        }

        using var asOwner = SignedInAs(ownerNick);
        using var asOutsider = SignedInAs(outsiderNick);

        Guid ownerAccountId;
        Guid outsiderAccountId;
        using (var db = Db())
        {
            ownerAccountId = db.Accounts.Single(a => a.Nickname == ownerNick).Id;
            outsiderAccountId = db.Accounts.Single(a => a.Nickname == outsiderNick).Id;
            db.Organisers.Add(new Organiser
            {
                Id = orgId,
                Name = $"Discord Org {stamp}",
                Slug = $"discord-{stamp}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Teams.AddRange(
                new Team { Id = teamAId, Name = $"Alpha {stamp}", Tag = $"DA{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow },
                new Team { Id = teamBId, Name = $"Bravo {stamp}", Tag = $"DB{stamp % 100000}", CreatedAt = DateTimeOffset.UtcNow });
            db.SaveChanges();
        }

        using (var db = Db(orgId))
        {
            db.OrganiserStaff.Add(new OrganiserStaff
            {
                Id = Guid.NewGuid(), OrganiserId = orgId, AccountId = ownerAccountId,
                Role = OrganiserRole.Owner, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Servers.Add(new GameServer
            {
                Id = serverId, OrganiserId = orgId, Host = "203.0.113.10", Port = 27015,
                Game = Game.Cs2, Verified = true, HmacSecret = pluginSecret,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Tournaments.Add(new Tournament
            {
                Id = tournamentId, OrganiserId = orgId, Name = $"Discord Cup {stamp}",
                Game = Game.Cs2, Format = TournamentFormat.SingleElimination,
                TeamSize = TeamSize.Squad, Status = TournamentStatus.Draft, MaxTeams = 8,
                StartsAt = DateTimeOffset.UtcNow.AddDays(7), CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        var editPath = $"/pl/t/{tournamentId}/edit";

        check("discord: configuration is authorised and rejected credentials are never echoed", () =>
        {
            var forbidden = asOutsider.GetAsync(editPath).GetAwaiter().GetResult();
            that(forbidden.StatusCode == HttpStatusCode.Forbidden,
                $"an outsider must get 403, got {(int)forbidden.StatusCode}");

            var marker = $"super-secret-marker-{stamp}";
            var rejected = asOwner.PostFormAsync(editPath + "?handler=SaveDiscord", new()
            {
                ["DiscordWebhookUrl"] = $"https://discord.com.evil.test/api/webhooks/{webhookId}/{marker}",
            }, editPath).GetAwaiter().GetResult();
            var html = rejected.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(rejected.StatusCode == HttpStatusCode.OK, "invalid input should return the form");
            that(!html.Contains(marker, StringComparison.Ordinal),
                "a rejected credential must not survive in HTML or model state");

            var outsiderPost = asOutsider.PostFormAsync(editPath + "?handler=SaveDiscord", new()
            {
                ["DiscordWebhookUrl"] = webhookUrl,
            }, "/pl").GetAwaiter().GetResult();
            that(outsiderPost.StatusCode == HttpStatusCode.Forbidden,
                $"a hand-written POST must still get 403, got {(int)outsiderPost.StatusCode}");

            using var db = Db(orgId);
            that(!db.TournamentDiscordWebhooks.Any(), "no rejected request may create configuration");
        });

        check("discord: a valid webhook is encrypted at rest and never returned by the edit page", () =>
        {
            var saved = asOwner.PostFormAsync(editPath + "?handler=SaveDiscord", new()
            {
                ["DiscordWebhookUrl"] = webhookUrl,
            }, editPath).GetAwaiter().GetResult();
            that(saved.StatusCode == HttpStatusCode.Redirect, $"save should redirect, got {(int)saved.StatusCode}");

            using (var db = Db(orgId))
            {
                var stored = db.TournamentDiscordWebhooks.AsNoTracking().Single();
                that(stored.ProtectedUrl != webhookUrl &&
                     stored.ProtectedUrl?.Contains(webhookToken, StringComparison.Ordinal) == false,
                    "PostgreSQL must contain ciphertext, not a usable credential");
            }

            var page = asOwner.GetStringAsync(editPath).GetAwaiter().GetResult();
            that(page.Contains("Webhook jest skonfigurowany", StringComparison.Ordinal),
                "the page should show configuration state");
            that(!page.Contains(webhookToken, StringComparison.Ordinal),
                "the configured token must never be projected back to HTML");
        });

        check("discord: opening registration commits one durable, retry-safe outbox fact", () =>
        {
            var opened = asOwner.PostFormAsync($"/pl/t/{tournamentId}?handler=Status", new()
            {
                ["to"] = TournamentStatus.RegistrationOpen.ToString(),
            }, $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(opened.StatusCode == HttpStatusCode.Redirect, $"open should redirect, got {(int)opened.StatusCode}");

            var repeated = asOwner.PostFormAsync($"/pl/t/{tournamentId}?handler=Status", new()
            {
                ["to"] = TournamentStatus.RegistrationOpen.ToString(),
            }, $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(repeated.StatusCode == HttpStatusCode.OK, "the invalid repeated transition should return the page");

            using var db = Db(orgId);
            var tournament = db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId);
            var jobs = db.DiscordAnnouncements.AsNoTracking()
                .Where(a => a.TournamentId == tournamentId && a.Type == DiscordAnnouncementType.RegistrationOpened)
                .ToList();
            that(tournament.RegistrationOpenRevision == 1, "the occurrence counter must advance once");
            that(jobs.Count == 1 && jobs[0].AttemptCount == 0 && jobs[0].SentAt == null,
                "the same transaction must leave exactly one unsent durable job");
            that(!jobs[0].Payload.RootElement.GetRawText().Contains(webhookToken, StringComparison.Ordinal),
                "the outbox contains event facts, never the credential");
        });

        bool DeliverOne()
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<DiscordAnnouncementDispatcher>()
                .DeliverNextAsync().GetAwaiter().GetResult();
        }

        check("discord: delivery renders a safe rich embed and records success", () =>
        {
            fake.Result = new(true, "ok");
            that(DeliverOne(), "one registration announcement should be due");
            that(fake.Sent.Count == 1, "the transport should receive one request");
            var sent = fake.Sent.Single();
            that(sent.Webhook.AbsoluteUri == webhookUrl, "delivery must recover the configured webhook");
            that(sent.Payload.Contains("allowed_mentions", StringComparison.Ordinal) &&
                 sent.Payload.Contains("\"parse\":[]", StringComparison.Ordinal),
                "mentions must be disabled even if future names contain Discord syntax");
            that(sent.Payload.Contains($"https://nextskill.test/pl/t/{tournamentId}", StringComparison.Ordinal),
                "the embed must link to the canonical tournament page");
            that(!sent.Payload.Contains(webhookToken, StringComparison.Ordinal),
                "the credential must not be copied into the payload");

            using var db = Db(orgId);
            that(db.DiscordAnnouncements.Single().SentAt is not null, "the outbox row must be marked sent");
            var configuration = db.TournamentDiscordWebhooks.Single();
            that(configuration.LastSucceededAt is not null && configuration.LastError is null,
                "the organiser-visible delivery state must record success");
        });

        var nextMatchPosition = 0;
        Match NewMatch(Guid id) => new()
        {
            Id = id, OrganiserId = orgId, TournamentId = tournamentId, Game = Game.Cs2,
            Status = MatchStatus.Scheduled, Integrity = MatchIntegrity.Confirmed,
            ServerId = serverId, TeamAId = teamAId, TeamBId = teamBId,
            SlotAResolved = true, SlotBResolved = true, Map = "de_dust2",
            Position = ++nextMatchPosition,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var matchId = Guid.NewGuid();
        var disputedMatchId = Guid.NewGuid();
        using (var db = Db(orgId))
        {
            db.Tournaments.Single(t => t.Id == tournamentId).Status = TournamentStatus.Running;
            db.Matches.AddRange(NewMatch(matchId), NewMatch(disputedMatchId));
            db.SaveChanges();
        }

        using var simulatorHttp = factory.NewClient();
        var simulator = new PluginSimulator(simulatorHttp, serverId, pluginSecret);

        check("discord: match start and confirmed result are idempotent; disputed results stay silent", () =>
        {
            var loaded = simulator.MatchLoaded(matchId, 1).GetAwaiter().GetResult();
            var duplicate = simulator.MatchLoaded(matchId, 1).GetAwaiter().GetResult();
            that(loaded.IsSuccessStatusCode && duplicate.IsSuccessStatusCode,
                "the plugin must be acknowledged on first and duplicate delivery");
            simulator.RoundEnd(matchId, 2, 24, 13, 11).GetAwaiter().GetResult();
            simulator.MapEnd(matchId, 3, "de_dust2", 13, 11, 24).GetAwaiter().GetResult();

            simulator.MatchLoaded(disputedMatchId, 1).GetAwaiter().GetResult();
            simulator.RoundEnd(disputedMatchId, 2, 24, 13, 11).GetAwaiter().GetResult();
            simulator.MapEnd(disputedMatchId, 3, "de_dust2", 11, 13, 24).GetAwaiter().GetResult();

            using var db = Db(orgId);
            var started = db.Matches.AsNoTracking().Single(m => m.Id == matchId);
            that(started.StartedAt is not null && started.Status == MatchStatus.Completed,
                "match_loaded must establish the start before map_end completes it");
            that(db.DiscordAnnouncements.Count(a => a.MatchId == matchId &&
                    a.Type == DiscordAnnouncementType.MatchStarted) == 1,
                "plugin retry must not duplicate the match-start announcement");
            that(db.DiscordAnnouncements.Count(a => a.MatchId == matchId &&
                    a.Type == DiscordAnnouncementType.ResultConfirmed) == 1,
                "a reconciled result should create one result announcement");
            that(db.DiscordAnnouncements.Count(a => a.MatchId == disputedMatchId &&
                    a.Type == DiscordAnnouncementType.ResultConfirmed) == 0,
                "a disputed score must never be announced as confirmed");
        });

        check("discord: failures retry and unexpected exceptions cannot persist the credential", () =>
        {
            fake.Result = new(false, "http_500");
            that(DeliverOne(), "the match-start job should be due");

            using (var db = Db(orgId))
            {
                var failed = db.DiscordAnnouncements.Single(a => a.MatchId == matchId &&
                    a.Type == DiscordAnnouncementType.MatchStarted);
                that(failed.AttemptCount == 1 && failed.SentAt == null && failed.AbandonedAt == null,
                    "a transient failure must remain retryable");
                that(failed.LastError == "http_500" && failed.NextAttemptAt > failed.CreatedAt,
                    "the retry should expose a bounded error code and later due time");
                failed.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                db.SaveChanges();
            }

            fake.Result = new(true, "ok");
            that(DeliverOne(), "the due retry should be claimed");
            using (var db = Db(orgId))
                that(db.DiscordAnnouncements.Single(a => a.MatchId == matchId &&
                    a.Type == DiscordAnnouncementType.MatchStarted).SentAt is not null,
                    "a later successful attempt must close the same row");

            fake.Throw = new InvalidOperationException("transport leaked " + webhookUrl);
            that(DeliverOne(), "the confirmed-result job should be due");
            fake.Throw = null;
            using (var db = Db(orgId))
            {
                var sanitised = db.DiscordAnnouncements.Single(a => a.MatchId == matchId &&
                    a.Type == DiscordAnnouncementType.ResultConfirmed);
                that(sanitised.LastError == "delivery_error" &&
                     !sanitised.LastError.Contains(webhookToken, StringComparison.Ordinal),
                    "an unexpected exception must collapse to a safe durable code");
            }
        });

        var forfeitMatchId = Guid.NewGuid();
        check("discord: an audited forfeit uses the confirmed-result outbox seam", () =>
        {
            using var db = Db(orgId);
            db.Registrations.Add(new TournamentRegistration
            {
                Id = Guid.NewGuid(), OrganiserId = orgId, TournamentId = tournamentId,
                TeamId = teamAId, RegisteredAt = DateTimeOffset.UtcNow,
            });
            var match = NewMatch(forfeitMatchId);
            db.Matches.Add(match);
            db.SaveChanges();

            BracketBuilder.ForfeitAsync(db, match, teamAId, ownerAccountId, "Confirmed event exit.")
                .GetAwaiter().GetResult();
            that(db.DiscordAnnouncements.Count(a => a.MatchId == forfeitMatchId &&
                    a.Type == DiscordAnnouncementType.ResultConfirmed) == 1,
                "a scoreless confirmed walkover should produce one result fact");
        });

        check("discord: removal destroys the credential and abandons pending work", () =>
        {
            var removed = asOwner.PostFormAsync(editPath + "?handler=RemoveDiscord", new(), editPath)
                .GetAwaiter().GetResult();
            that(removed.StatusCode == HttpStatusCode.Redirect,
                $"removal should redirect, got {(int)removed.StatusCode}");

            using var db = Db(orgId);
            var configuration = db.TournamentDiscordWebhooks.Single();
            that(configuration.ProtectedUrl is null && configuration.DisabledAt is not null,
                "removal must destroy the usable credential");
            that(!db.DiscordAnnouncements.Any(a => a.SentAt == null && a.AbandonedAt == null),
                "no queued job may keep retrying after removal");

            var before = db.DiscordAnnouncements.Count();
            var fresh = NewMatch(Guid.NewGuid());
            db.Matches.Add(fresh);
            DiscordAnnouncements.EnqueueMatchStartedAsync(db, fresh).GetAwaiter().GetResult();
            db.SaveChanges();
            that(db.DiscordAnnouncements.Count() == before,
                "future events must stay silent until a webhook is configured again");
        });

        using (var db = Db(orgId))
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM discord_announcements WHERE organiser_id={0}; " +
                "DELETE FROM tournament_discord_webhooks WHERE organiser_id={0}; " +
                "UPDATE matches SET winner_to_match_id=NULL, loser_to_match_id=NULL WHERE organiser_id={0}; " +
                "DELETE FROM matches WHERE organiser_id={0}; " +
                "DELETE FROM registrations WHERE organiser_id={0}; " +
                "DELETE FROM tournaments WHERE organiser_id={0}; " +
                "DELETE FROM servers WHERE organiser_id={0}; " +
                "DELETE FROM organiser_staff WHERE organiser_id={0}; " +
                "DELETE FROM organisers WHERE id={0};", orgId);
        }

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM sessions WHERE account_id IN ({0}, {1}); " +
                "DELETE FROM accounts WHERE id IN ({0}, {1}); " +
                "DELETE FROM teams WHERE id IN ({2}, {3});",
                ownerAccountId, outsiderAccountId, teamAId, teamBId);
        }
    }
}
