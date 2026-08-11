using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Checks;

/// <summary>
/// Verified Steam OpenID and Discord OAuth account links (decision 011).
/// Tests use protocol-shaped provider responses; no external account is trusted
/// merely because a browser typed an id.
/// </summary>
public static class ExternalAccountChecks
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => null;
        public Guid RequireOrganiserId() => throw new InvalidOperationException("No organiser in scope.");
    }

    private sealed class ProviderState
    {
        public int SteamVerificationCalls { get; set; }
        public string? SteamVerificationBody { get; set; }
        public string DiscordUserId { get; set; } = "987654321012345678";
        public string? DiscordTokenBody { get; set; }
        public AuthenticationHeaderValue? DiscordUserAuthorization { get; set; }
        public const string AccessToken = "provider-access-token-must-not-be-stored";
    }

    private sealed class ProviderHandler(ProviderState state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "steamcommunity.com" && uri.AbsolutePath == "/openid/login")
            {
                state.SteamVerificationCalls++;
                state.SteamVerificationBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Text("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n");
            }

            if (uri.Host == "discord.com" && uri.AbsolutePath == "/api/v10/oauth2/token")
            {
                state.DiscordTokenBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json($$"""{"access_token":"{{ProviderState.AccessToken}}","token_type":"Bearer"}""");
            }

            if (uri.Host == "discord.com" && uri.AbsolutePath == "/api/v10/users/@me")
            {
                state.DiscordUserAuthorization = request.Headers.Authorization;
                return Json($$"""{"id":"{{state.DiscordUserId}}","username":"verified"}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8, "text/plain"),
        };

        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(value, Encoding.UTF8, "application/json"),
        };
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        var provider = new ProviderState();
        using var factory = TestHost.Create(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalAccounts:PublicOrigin"] = "https://localhost",
                ["ExternalAccounts:Discord:ClientId"] = "discord-client-id",
                ["ExternalAccounts:Discord:ClientSecret"] = "discord-client-secret",
            }))
            .ConfigureServices(services =>
            {
                services.AddHttpClient(SteamOpenIdProvider.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new ProviderHandler(provider));
                services.AddHttpClient(DiscordOAuthProvider.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new ProviderHandler(provider));
            }));

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new NoTenant());

        HttpClient Client() => factory.NewClient();

        var stamp = DateTimeOffset.UtcNow.Ticks;
        provider.DiscordUserId = $"8{stamp % 100_000_000_000_000_000:D17}";
        const string password = "a-long-enough-passphrase";
        var firstNick = $"LinkA{stamp}";
        var secondNick = $"LinkB{stamp}";

        HttpClient SignedInAs(string nickname)
        {
            var http = Client();
            var response = http.PostFormAsync("/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.Redirect, "link fixture registration must succeed");
            return http;
        }

        using var first = SignedInAs(firstNick);
        using var second = SignedInAs(secondNick);

        Guid firstId, secondId;
        using (var db = Db())
        {
            firstId = db.Accounts.AsNoTracking().Single(a => a.Nickname == firstNick).Id;
            secondId = db.Accounts.AsNoTracking().Single(a => a.Nickname == secondNick).Id;
        }

        static (string State, string ReturnTo) SteamStart(HttpClient http)
        {
            var response = http.PostFormAsync(
                "/pl/me?handler=LinkSteam", new(), tokenPage: "/pl/me").GetAwaiter().GetResult();
            if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is null)
                throw new Exception($"Steam start should redirect, got {(int)response.StatusCode}");

            var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
            var returnTo = query["openid.return_to"].ToString();
            var state = QueryHelpers.ParseQuery(new Uri(returnTo).Query)["state"].ToString();
            if (string.IsNullOrWhiteSpace(state)) throw new Exception("Steam return_to carried no state.");
            return (state, returnTo);
        }

        static string SteamCallback(string state, string returnTo, string steamId)
        {
            var callback = new Uri(returnTo).PathAndQuery;
            var claimed = $"http://steamcommunity.com/openid/id/{steamId}";
            var fields = QueryString.Create(new Dictionary<string, string?>
            {
                ["openid.ns"] = "http://specs.openid.net/auth/2.0",
                ["openid.mode"] = "id_res",
                ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
                ["openid.claimed_id"] = claimed,
                ["openid.identity"] = claimed,
                ["openid.return_to"] = returnTo,
                ["openid.response_nonce"] = "2026-08-11T16:00:00Zfixture",
                ["openid.assoc_handle"] = "fixture",
                ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
                ["openid.sig"] = "fixture",
            }).ToUriComponent().TrimStart('?');
            return callback + "&" + fields;
        }

        static (string State, Uri Redirect) DiscordStart(HttpClient http)
        {
            var response = http.PostFormAsync(
                "/pl/me?handler=LinkDiscord", new(), tokenPage: "/pl/me").GetAwaiter().GetResult();
            if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is null)
                throw new Exception($"Discord start should redirect, got {(int)response.StatusCode}");

            var state = QueryHelpers.ParseQuery(response.Headers.Location.Query)["state"].ToString();
            if (string.IsNullOrWhiteSpace(state)) throw new Exception("Discord authorization carried no state.");
            return (state, response.Headers.Location);
        }

        check("external links: profile offers verified Steam and Discord flows, never typed ids", () =>
        {
            var html = first.GetStringAsync("/pl/me").GetAwaiter().GetResult();
            that(html.Contains("Połącz przez Steam") && html.Contains("Połącz przez Discord"),
                "both configured provider actions must be discoverable");
            that(!html.Contains("name=\"SteamId\"") && !html.Contains("name=\"DiscordId\""),
                "the profile must never offer a typed-id shortcut that proves nothing");
        });

        check("external links: forged state is rejected before any provider call", () =>
        {
            var (state, _) = SteamStart(first);
            var tampered = state[..^1] + (state[^1] == 'A' ? 'B' : 'A');
            var before = provider.SteamVerificationCalls;
            var response = first.GetAsync(
                $"/pl/me?handler=SteamCallback&state={Uri.EscapeDataString(tampered)}").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect, "a rejected callback should leave the provider URL");
            that(response.Headers.Location?.OriginalString.Contains("notice=link-invalid") == true,
                "tampered state must produce the generic invalid-attempt result");
            that(provider.SteamVerificationCalls == before,
                "do not call an external provider until local correlation has passed");
            using var db = Db();
            that(db.Accounts.AsNoTracking().Single(a => a.Id == firstId).SteamId is null,
                "forged state must not create a link");
        });

        var steamId = $"76563{stamp % 1_000_000_000_000:D12}";
        check("external links: Steam OpenID proof links the claimed 64-bit SteamID", () =>
        {
            var (state, returnTo) = SteamStart(first);
            var response = first.GetAsync(SteamCallback(state, returnTo, steamId)).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect, $"Steam callback returned {(int)response.StatusCode}");
            that(response.Headers.Location?.OriginalString.Contains("notice=steam-linked") == true,
                "verified Steam callback should report success");
            that(provider.SteamVerificationCalls > 0, "the assertion must be checked with Valve");
            that(provider.SteamVerificationBody?.Contains("openid.mode=check_authentication") == true,
                "Steam assertion must be replayed as check_authentication, not trusted from the browser");

            using var db = Db();
            that(db.Accounts.AsNoTracking().Single(a => a.Id == firstId).SteamId == steamId,
                "the verified SteamID must be linked to the signed-in account");
        });

        check("external links: one Steam identity cannot be attached to two accounts", () =>
        {
            var (state, returnTo) = SteamStart(second);
            var response = second.GetAsync(SteamCallback(state, returnTo, steamId)).GetAwaiter().GetResult();

            that(response.Headers.Location?.OriginalString.Contains("notice=link-in-use") == true,
                "the second account must get a generic already-linked result");
            using var db = Db();
            that(db.Accounts.AsNoTracking().Single(a => a.Id == secondId).SteamId is null,
                "a verified external identity still belongs to only one local account");
        });

        check("external links: Discord uses code exchange + identify and stores no access token", () =>
        {
            var (state, redirect) = DiscordStart(first);
            var query = QueryHelpers.ParseQuery(redirect.Query);
            that(redirect.Host == "discord.com" && redirect.AbsolutePath == "/oauth2/authorize",
                "authorization must go to Discord's fixed endpoint");
            that(query["scope"] == "identify", "only the minimum identify scope should be requested");
            that(query["client_id"] == "discord-client-id", "the configured application id must be used");

            var response = first.GetAsync(
                $"/pl/me?handler=DiscordCallback&code=provider-code&state={Uri.EscapeDataString(state)}")
                .GetAwaiter().GetResult();

            that(response.Headers.Location?.OriginalString.Contains("notice=discord-linked") == true,
                "verified Discord callback should report success");
            that(provider.DiscordTokenBody?.Contains("grant_type=authorization_code") == true,
                "Discord code must be exchanged server-side");
            that(provider.DiscordTokenBody?.Contains("client_secret=discord-client-secret") == true,
                "the server-side exchange must authenticate the configured application");
            that(provider.DiscordUserAuthorization?.Scheme == "Bearer"
                 && provider.DiscordUserAuthorization.Parameter == ProviderState.AccessToken,
                "the access token must only be used to fetch the authenticated user");

            using var db = Db();
            var account = db.Accounts.AsNoTracking().Single(a => a.Id == firstId);
            that(account.DiscordId == provider.DiscordUserId, "the verified Discord snowflake must be stored");
            var html = first.GetStringAsync("/pl/me").GetAwaiter().GetResult();
            that(!html.Contains(ProviderState.AccessToken), "provider access tokens must never reach the profile");
        });

        check("external links: unlink is a CSRF-protected local removal", () =>
        {
            var unsigned = first.PostAsync(
                "/pl/me?handler=UnlinkSteam", new FormUrlEncodedContent([])).GetAwaiter().GetResult();
            that(unsigned.StatusCode == HttpStatusCode.BadRequest,
                $"unsigned unlink must be rejected, got {(int)unsigned.StatusCode}");

            var steam = first.PostFormAsync(
                "/pl/me?handler=UnlinkSteam", new(), tokenPage: "/pl/me").GetAwaiter().GetResult();
            var discord = first.PostFormAsync(
                "/pl/me?handler=UnlinkDiscord", new(), tokenPage: "/pl/me").GetAwaiter().GetResult();
            that(steam.StatusCode == HttpStatusCode.Redirect && discord.StatusCode == HttpStatusCode.Redirect,
                "signed unlink actions should redirect back to the profile");

            using var db = Db();
            var account = db.Accounts.AsNoTracking().Single(a => a.Id == firstId);
            that(account.SteamId is null && account.DiscordId is null,
                "unlink must remove only the local associations");
        });

        check("external links: database uniqueness closes the simultaneous-callback race", () =>
        {
            var racedId = $"76562{stamp % 1_000_000_000_000:D12}";
            using (var db = Db())
            {
                db.Accounts.Single(a => a.Id == firstId).SteamId = racedId;
                db.SaveChanges();
            }

            var rejected = false;
            using (var db = Db())
            {
                db.Accounts.Single(a => a.Id == secondId).SteamId = racedId;
                try { db.SaveChanges(); }
                catch (DbUpdateException) { rejected = true; }
            }

            that(rejected, "the database must reject one external identity on two accounts");
        });
    }
}
