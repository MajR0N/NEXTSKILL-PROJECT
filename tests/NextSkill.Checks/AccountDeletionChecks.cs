using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Auth;
using NextSkill.Domain.Teams;

namespace NextSkill.Checks;

/// <summary>
/// Account erasure and IP minimisation against the real web application and
/// PostgreSQL schema (decisions 011, 012, 029).
/// </summary>
public static class AccountDeletionChecks
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => null;
        public Guid RequireOrganiserId() => throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = TestHost.Create();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new NoTenant());

        HttpClient Client() => factory.NewClient();

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var nickname = $"Erase{stamp}";
        var email = $"erase-{stamp}@nextskill.test";
        const string password = "a-long-enough-passphrase";
        const string setinfoMarker = "ERASURE-MUST-DESTROY-SETINFO";
        var steamMarker = $"76564{stamp % 1_000_000_000_000:D12}";
        var discordMarker = $"7{stamp % 100_000_000_000_000_000:D17}";
        var totpSecret = Totp.NewSecret();

        using var signedIn = Client();
        var registration = signedIn.PostFormAsync("/pl/account/register", new()
        {
            ["Input.Nickname"] = nickname,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();
        that(registration.StatusCode == HttpStatusCode.Redirect,
            $"erasure fixture registration should redirect, got {(int)registration.StatusCode}");

        Guid accountId;
        Guid keeperId;
        Guid teamId;
        Guid pendingTeamId;
        using (var db = Db())
        {
            var account = db.Accounts.Single(a => a.Email == email);
            accountId = account.Id;
            account.SetinfoTokenHash = setinfoMarker;
            account.SteamId = steamMarker;
            account.DiscordId = discordMarker;
            account.TwoFactorMethod = TwoFactorMethod.Totp;
            account.TwoFactorSecret = totpSecret;
            account.TwoFactorEnabledAt = DateTimeOffset.UtcNow;
            account.PhoneNumber = "+48123123123";
            account.PhoneVerifiedAt = DateTimeOffset.UtcNow;

            keeperId = Guid.NewGuid();
            db.Accounts.Add(new Account
            {
                Id = keeperId,
                Email = $"keeper-{stamp}@nextskill.test",
                Nickname = $"Keeper{stamp}",
                PasswordHash = Passwords.Hash("keeper-long-enough-password"),
                CreatedAt = DateTimeOffset.UtcNow,
            });

            teamId = Guid.NewGuid();
            pendingTeamId = Guid.NewGuid();
            db.Teams.AddRange(
                new Team { Id = teamId, Name = $"Erasure Team {stamp}", Tag = $"E{stamp % 10_000_000:D7}", CreatedAt = DateTimeOffset.UtcNow },
                new Team { Id = pendingTeamId, Name = $"Pending Team {stamp}", Tag = $"Q{stamp % 10_000_000:D7}", CreatedAt = DateTimeOffset.UtcNow });

            db.TeamMemberships.AddRange(
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = teamId, AccountId = accountId,
                    Role = TeamRole.Captain, Slot = RosterSlot.Player,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                },
                new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = pendingTeamId, AccountId = accountId,
                    Role = TeamRole.Member, Slot = RosterSlot.Substitute,
                    CreatedAt = DateTimeOffset.UtcNow,
                });

            db.Ratings.Add(new Rating
            {
                Id = Guid.NewGuid(), AccountId = accountId, Game = Game.Cs16,
                Value = 99, Deviation = 1, Volatility = 0, MatchesPlayed = 50,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.LftListings.Add(new LftListing
            {
                Id = Guid.NewGuid(), AccountId = accountId, Game = Game.Cs2,
                Role = "ERASURE-LFT", Note = "ERASURE-LFT-NOTE",
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            });
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.NewGuid(), AccountId = accountId,
                TokenHash = ResetTokens.HashOf($"reset-{stamp}"),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            db.RecoveryCodes.Add(new RecoveryCode
            {
                Id = Guid.NewGuid(), AccountId = accountId,
                CodeHash = ResetTokens.HashOf($"recovery-{stamp}"), CreatedAt = DateTimeOffset.UtcNow,
            });
            db.OneTimeCodes.Add(new OneTimeCode
            {
                Id = Guid.NewGuid(), AccountId = accountId, Purpose = OneTimeCodePurpose.SmsSignIn,
                CodeHash = ResetTokens.HashOf($"otp-{stamp}"),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            db.Notifications.AddRange(
                new Notification
                {
                    Id = Guid.NewGuid(), AccountId = accountId, Type = NotificationType.RosterChanged,
                    Data = JsonSerializer.SerializeToDocument(new { marker = "ERASURE-OWN-NOTIFICATION" }),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                new Notification
                {
                    Id = Guid.NewGuid(), AccountId = keeperId, Type = NotificationType.TeamInviteAccepted,
                    Data = JsonSerializer.SerializeToDocument(new { teamId, team = "fixture", player = nickname }),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            db.SaveChanges();
        }

        using var otherSession = Client();
        var secondLogin = otherSession.PostFormAsync("/pl/account/login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();
        that(secondLogin.StatusCode == HttpStatusCode.Redirect &&
             secondLogin.Headers.Location?.OriginalString.Contains("/account/challenge") == true,
            "a two-factor account should stop at its challenge");

        // The fixture was upgraded to 2FA after its first session. Finish the
        // second login so account deletion has two live browsers to revoke.
        var challengePath = secondLogin.Headers.Location!.OriginalString;
        var challengePage = otherSession.GetStringAsync(challengePath).GetAwaiter().GetResult();
        var secondChallenge = otherSession.PostAsync(challengePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Code"] = Totp.Compute(totpSecret, DateTimeOffset.UtcNow),
            ["__RequestVerificationToken"] = TestHost.AntiforgeryToken(challengePage),
        })).GetAwaiter().GetResult();
        that(secondChallenge.StatusCode == HttpStatusCode.Redirect, "the second browser should finish 2FA");

        using var pendingLogin = Client();
        var pendingStart = pendingLogin.PostFormAsync("/pl/account/login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();
        var pendingPath = pendingStart.Headers.Location!.OriginalString;
        var pendingPage = pendingLogin.GetStringAsync(pendingPath).GetAwaiter().GetResult();
        var pendingAntiforgery = TestHost.AntiforgeryToken(pendingPage);

        check("privacy: authentication rate limits use ephemeral HMAC keys and PostgreSQL stores no IP column", () =>
        {
            var firstProcess = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            var secondProcess = Enumerable.Range(2, 32).Select(i => (byte)i).ToArray();
            var address = IPAddress.Parse("203.0.113.77");
            var key = IpAbuseKeys.For(address, firstProcess);

            that(key == IpAbuseKeys.For(address, firstProcess), "one process must rate-limit one IP consistently");
            that(key != IpAbuseKeys.For(address, secondProcess), "a restart key must make old partitions unlinkable");
            that(key != IpAbuseKeys.For(IPAddress.Parse("203.0.113.78"), firstProcess),
                "different addresses must not share a limiter partition");
            that(!key.Contains(address.ToString(), StringComparison.Ordinal),
                "the limiter partition must never retain the raw address");

            using var db = Db();
            db.Database.OpenConnection();
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT count(*) FROM information_schema.columns " +
                                  "WHERE table_schema = 'public' AND table_name = 'accounts' " +
                                  "AND column_name = 'last_seen_ip'";
            that(Convert.ToInt32(command.ExecuteScalar()) == 0,
                "the application database must have zero persistent IP columns");
        });

        check("deletion: /me explains anonymisation and requires password plus exact nickname", () =>
        {
            var html = signedIn.GetStringAsync("/pl/me").GetAwaiter().GetResult();
            that(html.Contains("zachowa historyczne mecze"), "the player must be told what history survives");
            that(html.Contains("nieodwracalnie usunięte"), "the private data consequence must be explicit");
            that(html.Contains("DeleteAccount.Password") && html.Contains("DeleteAccount.ConfirmationNickname"),
                "deletion must require reauthentication and a deliberate typed confirmation");
            that(html.Contains(nickname), "the exact confirmation text must be visible before submission");
        });

        check("deletion: the mutation is CSRF-protected", () =>
        {
            var response = signedIn.PostAsync(
                "/pl/me?handler=DeleteAccount",
                new FormUrlEncodedContent([])).GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.BadRequest,
                $"unsigned deletion should be 400, got {(int)response.StatusCode}");

            using var db = Db();
            that(db.Accounts.Single(a => a.Id == accountId).DeletedAt is null,
                "a CSRF attempt must not alter the account");
            that(db.Sessions.Count(s => s.AccountId == accountId) == 2,
                "a CSRF attempt must revoke no browsers");
        });

        check("deletion: a wrong password changes nothing", () =>
        {
            var response = signedIn.PostFormAsync("/pl/me?handler=DeleteAccount", new()
            {
                ["DeleteAccount.Password"] = "definitely-the-wrong-password",
                ["DeleteAccount.ConfirmationNickname"] = nickname,
            }, tokenPage: "/pl/me").GetAwaiter().GetResult();
            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK, "a refused deletion should re-render the profile");
            that(html.Contains("wpisany nick"),
                "password and confirmation failures should share one answer");

            using var db = Db();
            var account = db.Accounts.AsNoTracking().Single(a => a.Id == accountId);
            that(account.DeletedAt is null && account.Email == email && account.SteamId == steamMarker,
                "failed reauthentication must erase nothing");
        });

        check("deletion: the sole captain is refused before a team is orphaned", () =>
        {
            var response = signedIn.PostFormAsync("/pl/me?handler=DeleteAccount", new()
            {
                ["DeleteAccount.Password"] = password,
                ["DeleteAccount.ConfirmationNickname"] = nickname,
            }, tokenPage: "/pl/me").GetAwaiter().GetResult();
            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK && html.Contains("kapitanat"),
                "the refusal must explain the available way out");
            using var db = Db();
            that(db.Accounts.Single(a => a.Id == accountId).DeletedAt is null,
                "the account must remain intact while it is the only captain");
        });

        using (var db = Db())
        {
            db.TeamMemberships.Add(new TeamMembership
            {
                Id = Guid.NewGuid(), TeamId = teamId, AccountId = keeperId,
                Role = TeamRole.Captain, Slot = RosterSlot.Player,
                JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        check("deletion: identity and credentials are erased while history survives anonymously", () =>
        {
            var response = signedIn.PostFormAsync("/pl/me?handler=DeleteAccount", new()
            {
                ["DeleteAccount.Password"] = password,
                ["DeleteAccount.ConfirmationNickname"] = nickname,
            }, tokenPage: "/pl/me").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"successful deletion should redirect, got {(int)response.StatusCode}");
            that(response.Headers.Location?.OriginalString == "/pl/account/login?accountDeleted=true",
                $"deletion should end at its confirmation, went to {response.Headers.Location}");

            using (var db = Db())
            {
                var account = db.Accounts.AsNoTracking().Single(a => a.Id == accountId);
                that(account.DeletedAt is not null, "the retained row must be marked deleted");
                that(account.Email != email && account.Nickname != nickname,
                    "email and public identity must be replaced, not merely hidden by the UI");
                that(!account.Email.Contains(accountId.ToString("N"), StringComparison.OrdinalIgnoreCase),
                    "the anonymous contact marker must not encode the stable account id");
                that(account.PasswordHash.StartsWith("!deleted-", StringComparison.Ordinal) &&
                     account.SetinfoTokenHash is null && account.SteamId is null && account.DiscordId is null,
                    "every website, game-server and provider credential must be destroyed");
                that(account.TwoFactorMethod is null && account.TwoFactorSecret is null &&
                     account.PhoneNumber is null && account.PhoneVerifiedAt is null,
                    "two-factor secrets and phone data must be destroyed");
                that(!db.Sessions.Any(s => s.AccountId == accountId) &&
                     !db.PasswordResetTokens.Any(t => t.AccountId == accountId) &&
                     !db.RecoveryCodes.Any(c => c.AccountId == accountId) &&
                     !db.OneTimeCodes.Any(c => c.AccountId == accountId),
                    "all outstanding authentication credentials must be gone");
                that(!db.Notifications.Any(n => n.AccountId == accountId) &&
                     !db.LftListings.Any(l => l.AccountId == accountId),
                    "ephemeral community data must be erased");
                that(!db.Notifications.AsNoTracking().ToList().Any(n =>
                        n.Data?.RootElement.GetRawText().Contains(nickname) == true),
                    "copies of the old nickname in another account's notifications must be scrubbed");
                that(db.Ratings.Any(r => r.AccountId == accountId),
                    "the anonymous rating row must survive so competition history is not rewritten");

                var acceptedHistory = db.TeamMemberships.Single(m =>
                    m.TeamId == teamId && m.AccountId == accountId);
                that(acceptedHistory.LeftAt is not null,
                    "accepted roster history must survive but stop occupying a live slot");
                that(!db.TeamMemberships.Any(m => m.TeamId == pendingTeamId && m.AccountId == accountId),
                    "an invitation that was never accepted is not history and must be deleted");
            }

            using var anonymous = Client();
            var publicProfile = anonymous.GetStringAsync($"/pl/players/{accountId}").GetAwaiter().GetResult();
            that(publicProfile.Contains("Usunięty gracz") && publicProfile.Contains("Anonimowy rekord"),
                "historical links should resolve to a plain anonymous tombstone");
            foreach (var privateValue in new[] { nickname, email, steamMarker, discordMarker, setinfoMarker, "ERASURE-LFT" })
                that(!publicProfile.Contains(privateValue, StringComparison.Ordinal),
                    $"the anonymous profile leaked '{privateValue}'");

            var ranking = anonymous.GetStringAsync("/pl/ranking?scope=Players&game=Cs16")
                .GetAwaiter().GetResult();
            that(!ranking.Contains(nickname, StringComparison.Ordinal),
                "a deleted person must not remain in the live ranking");

            foreach (var browser in new[] { signedIn, otherSession })
                that(browser.GetAsync("/pl/me").GetAwaiter().GetResult().StatusCode == HttpStatusCode.Redirect,
                    "every authenticated browser must lose private access immediately");

            var confirmation = signedIn.GetStringAsync(response.Headers.Location!).GetAwaiter().GetResult();
            that(confirmation.Contains("Konto zostało usunięte"),
                "the initiating browser must receive an explicit completion message");
        });

        check("deletion: an in-flight two-factor challenge cannot resurrect a session", () =>
        {
            var response = pendingLogin.PostAsync(pendingPath, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = Totp.Compute(totpSecret, DateTimeOffset.UtcNow),
                ["__RequestVerificationToken"] = pendingAntiforgery,
            })).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect &&
                 response.Headers.Location?.OriginalString.Contains("/account/login") == true,
                "a pending login for a deleted account must return to login");
            using var db = Db();
            that(!db.Sessions.Any(s => s.AccountId == accountId),
                "the deleted account must remain sessionless after the stale challenge");
        });

        Guid replacementId = Guid.Empty;
        check("deletion: the original email and nickname are reusable", () =>
        {
            using var replacement = Client();
            var response = replacement.PostFormAsync("/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = email,
                ["Input.Password"] = "a-new-owner-of-the-released-identity",
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"released identity should register again, got {(int)response.StatusCode}");
            using var db = Db();
            replacementId = db.Accounts.Single(a => a.Email == email && a.DeletedAt == null).Id;
            that(replacementId != accountId, "reuse must create a new identity, never revive the tombstone");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM notifications WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM sessions WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM password_reset_tokens WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM recovery_codes WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM one_time_codes WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM lft_listings WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM ratings WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM team_memberships WHERE account_id IN ({0}, {1}, {2}); " +
                "DELETE FROM accounts WHERE id IN ({0}, {1}, {2}); " +
                "DELETE FROM teams WHERE id IN ({3}, {4});",
                accountId, keeperId, replacementId, teamId, pendingTeamId);
        }
    }
}
