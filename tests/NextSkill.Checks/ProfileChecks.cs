using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Teams;

namespace NextSkill.Checks;

/// <summary>
/// Public/private profile boundary (decisions 012, 025, 029).
///
/// The most important assertion is negative: a public profile must stay public
/// when the account row also contains an email, IP, token hash and provider ids.
/// </summary>
public static class ProfileChecks
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

        static string FreshTokenFrom(string html)
        {
            var decoded = WebUtility.HtmlDecode(html);
            var match = Regex.Match(decoded, @"setinfo _ns ""(?<token>[A-Za-z0-9_-]+)""");
            if (!match.Success) throw new Exception("The one-time setinfo command was not rendered.");
            return match.Groups["token"].Value;
        }

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var nickname = $"Profile{stamp}";
        var email = $"profile-{stamp}@nextskill.test";
        const string password = "a-long-enough-passphrase";
        const string tokenHashMarker = "PUBLIC-PROFILE-MUST-NOT-LEAK-THIS-TOKEN-HASH";
        var steamIdMarker = $"76561{stamp % 1_000_000_000_000:D12}";
        var discordIdMarker = $"9{stamp % 100_000_000_000_000_000:D17}";

        using var signedIn = Client();
        var registration = signedIn.PostFormAsync("/pl/account/register", new()
        {
            ["Input.Nickname"] = nickname,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();

        that(registration.StatusCode == HttpStatusCode.Redirect,
            $"profile fixture registration should redirect, got {(int)registration.StatusCode}");

        Guid accountId;
        string currentTeamName;
        string pastTeamName;
        using (var db = Db())
        {
            var account = db.Accounts.Single(a => a.Email == email);
            accountId = account.Id;
            account.SetinfoTokenHash = tokenHashMarker;
            account.SteamId = steamIdMarker;
            account.DiscordId = discordIdMarker;

            db.Ratings.AddRange(
                new Rating
                {
                    Id = Guid.NewGuid(), AccountId = accountId, Game = Game.Cs2,
                    Value = 30, Deviation = 2, Volatility = 0,
                    MatchesPlayed = 18, UpdatedAt = DateTimeOffset.UtcNow,
                },
                new Rating
                {
                    Id = Guid.NewGuid(), AccountId = accountId, Game = Game.Cs16,
                    Value = 25, Deviation = 8, Volatility = 0,
                    MatchesPlayed = 2, UpdatedAt = DateTimeOffset.UtcNow,
                });

            currentTeamName = $"Current Profile Team {stamp}";
            pastTeamName = $"Past Profile Team {stamp}";
            var currentTeam = new Team
            {
                Id = Guid.NewGuid(), Name = currentTeamName, Tag = $"C{stamp % 1000000}",
                CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2),
            };
            var pastTeam = new Team
            {
                Id = Guid.NewGuid(), Name = pastTeamName, Tag = $"P{stamp % 1000000}",
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
            };
            db.Teams.AddRange(currentTeam, pastTeam);
            db.TeamMemberships.AddRange(
                new TeamMembership
                {
                    Id = Guid.NewGuid(), AccountId = accountId, TeamId = currentTeam.Id,
                    Role = TeamRole.Member, Slot = RosterSlot.Player,
                    CreatedAt = DateTimeOffset.UtcNow.AddMonths(-2),
                    JoinedAt = DateTimeOffset.UtcNow.AddMonths(-2),
                },
                new TeamMembership
                {
                    Id = Guid.NewGuid(), AccountId = accountId, TeamId = pastTeam.Id,
                    Role = TeamRole.Member, Slot = RosterSlot.Substitute,
                    CreatedAt = DateTimeOffset.UtcNow.AddYears(-1),
                    JoinedAt = DateTimeOffset.UtcNow.AddYears(-1),
                    LeftAt = DateTimeOffset.UtcNow.AddMonths(-6),
                });

            db.LftListings.Add(new LftListing
            {
                Id = Guid.NewGuid(), AccountId = accountId, Game = Game.Cs2,
                Role = "IGL", Availability = "Wieczory", Note = "Szukam stałego składu",
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            });

            db.SaveChanges();
        }

        check("profiles: /me requires a live signed-in session", () =>
        {
            using var anonymous = Client();
            var res = anonymous.GetAsync("/pl/me").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect,
                $"anonymous /me should redirect, got {(int)res.StatusCode}");
            that(res.Headers.Location?.OriginalString.Contains("/pl/account/login") == true,
                $"anonymous /me should go to the localised login page, went to {res.Headers.Location}");
            that(res.Headers.Location?.OriginalString.Contains("returnUrl=") == true,
                "login redirect must retain /me as the return destination");
        });

        check("profiles: the public page shows ratings, team history and LFT state", () =>
        {
            using var anonymous = Client();
            var res = anonymous.GetAsync($"/pl/players/{accountId}").GetAwaiter().GetResult();
            var html = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, $"public profile returned {(int)res.StatusCode}");
            that(html.Contains(nickname), "profile must name the player");
            that(html.Contains("CS2") && html.Contains("CS 1.6"), "ratings for both games must stay separate");
            that(html.Contains("rating wstępny, bez pozycji"), "a provisional rating must never imply a rank");
            that(html.Contains(currentTeamName) && html.Contains(pastTeamName),
                "current and past team history must both be visible");
            that(html.Contains("IGL") && html.Contains("Wieczory"), "an active LFT listing must be visible");
            that(html.Contains("Steam połączony") && html.Contains("Discord połączony"),
                "public linked-account badges should report presence without ids");
        });

        check("profiles: public projection leaks no email, IP, token hash or provider id", () =>
        {
            using var anonymous = Client();
            var html = anonymous.GetStringAsync($"/pl/players/{accountId}").GetAwaiter().GetResult();

            foreach (var secret in new[] { email, tokenHashMarker, steamIdMarker, discordIdMarker })
                that(!html.Contains(secret, StringComparison.Ordinal),
                    $"public profile leaked private marker '{secret}'");
        });

        check("profiles: /me shows private state but never retrieves the setinfo token", () =>
        {
            var res = signedIn.GetAsync("/pl/me").GetAwaiter().GetResult();
            var html = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, $"signed-in /me returned {(int)res.StatusCode}");
            that(html.Contains(email), "the owner should see their own email");
            that(html.Contains("Token skonfigurowany"), "the owner should see token status");
            that(html.Contains("Aktywne sesje"), "the owner should see session state");
            that(html.Contains($"/pl/players/{accountId}"), "the owner should be able to reach the public profile");
            that(!html.Contains(tokenHashMarker), "a stored token hash must never be rendered, even to its owner");
            that(!html.Contains(steamIdMarker) && !html.Contains(discordIdMarker),
                "provider ids are not needed to report linked-account state");
        });

        check("profiles: an unknown player id is a plain 404", () =>
        {
            using var anonymous = Client();
            var res = anonymous.GetAsync($"/pl/players/{Guid.NewGuid()}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.NotFound,
                $"unknown public profile should be 404, got {(int)res.StatusCode}");
        });

        check("setinfo: tokens carry 256 bits, are console-safe and stored as hashes", () =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < 10; i++)
            {
                var (token, hash) = SetinfoTokens.Create();
                that(token.Length == 43, $"32-byte base64url token should be 43 characters, got {token.Length}");
                that(token.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'),
                    "token must contain no whitespace, quotes or console separators");
                that(seen.Add(token), "fresh tokens must not repeat");
                that(hash != token, "the stored value must not be the usable token");
                that(SetinfoTokens.Verify(token, hash), "the new token must verify against its hash");
                that(!SetinfoTokens.Verify(token + "x", hash), "a changed token must fail verification");
                that(!SetinfoTokens.Verify(token, "damaged-row"), "a damaged stored hash must fail closed");
            }
        });

        check("setinfo: rotation POST requires an antiforgery token", () =>
        {
            var res = signedIn.PostAsync(
                "/pl/me?handler=RotateSetinfo",
                new FormUrlEncodedContent([])).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.BadRequest,
                $"unsigned rotation should be rejected with 400, got {(int)res.StatusCode}");

            using var db = Db();
            that(db.Accounts.AsNoTracking().Single(a => a.Id == accountId).SetinfoTokenHash == tokenHashMarker,
                "a CSRF attempt must not change the stored token hash");
        });

        string firstToken = "";
        check("setinfo: rotation reveals one command and stores only its hash", () =>
        {
            var res = signedIn.PostFormAsync(
                "/pl/me?handler=RotateSetinfo",
                new(),
                tokenPage: "/pl/me").GetAwaiter().GetResult();
            var html = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, $"rotation returned {(int)res.StatusCode}");
            that(res.Headers.CacheControl?.NoStore == true, "the one-time command response must be no-store");
            that(html.Contains("widoczny tylko raz"), "the page must tell the player this is their only view");
            that(html.Contains("To nie jest hasło") && html.Contains("jednorazowego kodu"),
                "the page must explain that the token cannot log in and never works alone");

            firstToken = FreshTokenFrom(html);
            using (var db = Db())
            {
                var storedHash = db.Accounts.AsNoTracking().Single(a => a.Id == accountId).SetinfoTokenHash;
                that(storedHash is not null && storedHash != firstToken,
                    "the usable token must never be stored in plaintext");
                that(storedHash != tokenHashMarker, "rotation must replace the old credential immediately");
                that(SetinfoTokens.Verify(firstToken, storedHash!), "the rendered token must match the stored hash");
            }

            var later = signedIn.GetAsync("/pl/me").GetAwaiter().GetResult();
            var laterHtml = later.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(later.Headers.CacheControl?.NoStore == true, "the private profile itself must be no-store");
            that(!laterHtml.Contains(firstToken), "a later GET must not retrieve the token");
            that(!laterHtml.Contains("setinfo _ns \""), "a later GET must not reconstruct a command");
        });

        check("setinfo: rotating again invalidates the previous token", () =>
        {
            var res = signedIn.PostFormAsync(
                "/pl/me?handler=RotateSetinfo",
                new(),
                tokenPage: "/pl/me").GetAwaiter().GetResult();
            var secondToken = FreshTokenFrom(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            that(secondToken != firstToken, "a rotation must mint a genuinely new credential");

            using var db = Db();
            var storedHash = db.Accounts.AsNoTracking().Single(a => a.Id == accountId).SetinfoTokenHash!;
            that(SetinfoTokens.Verify(secondToken, storedHash), "the latest token must verify");
            that(!SetinfoTokens.Verify(firstToken, storedHash), "the previous token must stop working immediately");
        });

        using var otherSession = Client();
        check("sessions: profile offers account-wide logout and counts both browsers", () =>
        {
            var login = otherSession.PostFormAsync("/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            that(login.StatusCode == HttpStatusCode.Redirect,
                $"the second browser should sign in, got {(int)login.StatusCode}");

            using (var db = Db())
                that(db.Sessions.Count(s => s.AccountId == accountId) == 2,
                    "the fixture must have two independent live session rows");

            var html = signedIn.GetStringAsync("/pl/me").GetAwaiter().GetResult();
            that(html.Contains("Wyloguj wszędzie"), "the profile must expose the account-wide control");
            that(html.Contains("Natychmiast unieważnia wszystkie sesje"),
                "the consequence must be stated before the player acts");
        });

        check("sessions: log out everywhere requires an antiforgery token", () =>
        {
            var res = signedIn.PostAsync(
                "/pl/me?handler=LogoutEverywhere",
                new FormUrlEncodedContent([])).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.BadRequest,
                $"unsigned account-wide logout should be rejected with 400, got {(int)res.StatusCode}");

            using (var db = Db())
                that(db.Sessions.Count(s => s.AccountId == accountId) == 2,
                    "a CSRF attempt must revoke no sessions");

            that(otherSession.GetAsync("/pl/me").GetAwaiter().GetResult().StatusCode == HttpStatusCode.OK,
                "the other browser must remain signed in after the rejected request");
        });

        check("sessions: log out everywhere immediately revokes every browser", () =>
        {
            var res = signedIn.PostFormAsync(
                "/pl/me?handler=LogoutEverywhere",
                new(),
                tokenPage: "/pl/me").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect,
                $"account-wide logout should redirect, got {(int)res.StatusCode}");
            that(res.Headers.Location?.OriginalString == "/pl/account/login?allSessionsRevoked=true",
                $"account-wide logout should end at a confirmation, went to {res.Headers.Location}");

            using (var db = Db())
                that(!db.Sessions.Any(s => s.AccountId == accountId),
                    "EVERY session row for the account must be gone");

            foreach (var browser in new[] { signedIn, otherSession })
            {
                var profile = browser.GetAsync("/pl/me").GetAwaiter().GetResult();
                that(profile.StatusCode == HttpStatusCode.Redirect,
                    "a revoked browser must lose private-page access immediately");
                that(profile.Headers.Location?.OriginalString.Contains("/pl/account/login") == true,
                    $"a revoked browser should be challenged locally, went to {profile.Headers.Location}");
            }

            var confirmation = signedIn.GetStringAsync(res.Headers.Location!).GetAwaiter().GetResult();
            that(confirmation.Contains("Wylogowano ze wszystkich urządzeń"),
                "the initiating browser should receive an explicit completion message");
        });
    }
}
