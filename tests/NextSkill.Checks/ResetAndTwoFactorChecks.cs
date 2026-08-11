using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Email;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Auth;

namespace NextSkill.Checks;

/// <summary>
/// Password reset and two-factor authentication, end to end (decisions 011, 047).
///
/// These read the <b>real email and SMS files</b> rather than pulling tokens out
/// of the database. Reaching into the database would prove the token generator
/// works and nothing about whether a user could ever receive one — and "the
/// message never arrives" is precisely the failure that looks identical to
/// success.
/// </summary>
public static class ResetAndTwoFactorChecks
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => null;
        public Guid RequireOrganiserId() => throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        var stamp = DateTimeOffset.UtcNow.Ticks;
        var mailDir = Path.Combine(Path.GetTempPath(), $"nextskill-mail-{stamp}");
        var smsDir = Path.Combine(Path.GetTempPath(), $"nextskill-sms-{stamp}");

        using var factory = TestHost.Create(b => b
            .UseSetting("Email:Directory", mailDir)
            .UseSetting("Sms:Directory", smsDir));

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new NoTenant());

        var nick = $"Rst{stamp}";
        var address = $"{nick.ToLowerInvariant()}@nextskill.test";
        const string password = "a-long-enough-passphrase";
        const string newPassword = "an-entirely-different-passphrase";

        var clientNumber = 0;

        HttpClient Client() => factory.NewClient();

        Task<HttpResponseMessage> PostAsync(
            HttpClient http, string path, Dictionary<string, string> fields, string? tokenPage = null) =>
            http.PostFormAsync(path, fields, tokenPage);

        /// The newest message in a directory, or null when nothing was sent.
        string? NewestMessage(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            var file = new DirectoryInfo(dir).GetFiles().OrderByDescending(f => f.Name).FirstOrDefault();
            return file is null ? null : File.ReadAllText(file.FullName);
        }

        void ClearOutbox(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        using var http = Client();
        PostAsync(http, "/pl/account/register", new()
        {
            ["Input.Nickname"] = nick,
            ["Input.Email"] = address,
            ["Input.Password"] = password,
        }).GetAwaiter().GetResult();

        Guid accountId;
        using (var db = Db())
            accountId = db.Accounts.AsNoTracking().Single(a => a.Nickname == nick).Id;

        // ── password reset ───────────────────────────────────────────────
        string resetToken = "";

        check("reset: an unknown address gets the SAME answer as a real one, and sends nothing", () =>
        {
            ClearOutbox(mailDir);

            using var anonymous = Client();
            var unknown = PostAsync(anonymous, "/pl/account/forgot", new()
            {
                ["Input.Email"] = $"nobody-{stamp}@nextskill.test",
            }).GetAwaiter().GetResult();

            that(unknown.StatusCode == HttpStatusCode.OK, "the form re-renders with a confirmation");

            var body = unknown.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(body.Contains("Je&#x15B;li takie konto istnieje") || body.Contains("Jeśli takie konto istnieje"),
                "the page must not say whether the address is registered (040)");

            that(NewestMessage(mailDir) is null, "and nothing may be sent to an address we do not have");
        });

        check("reset: a real address receives a link, and the token is NOT stored in plaintext", () =>
        {
            ClearOutbox(mailDir);

            using var anonymous = Client();
            var res = PostAsync(anonymous, "/pl/account/forgot", new()
            {
                ["Input.Email"] = address,
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "same answer as the unknown case");

            var message = NewestMessage(mailDir);
            that(message is not null, "an email must actually be produced — this is the step that silently fails");

            var match = Regex.Match(message!, @"token=([A-Za-z0-9_\-]+)");
            that(match.Success, $"the message must carry a reset link; got:\n{message}");
            resetToken = match.Groups[1].Value;

            using var db = Db();
            var stored = db.PasswordResetTokens.AsNoTracking().Single(t => t.AccountId == accountId);
            that(stored.TokenHash != resetToken,
                "THE TOKEN MUST NOT BE STORED IN PLAINTEXT — a database leak would hand over working reset links");
            that(stored.TokenHash == ResetTokens.HashOf(resetToken), "the stored value must be its hash");

            var lifetime = stored.ExpiresAt - stored.CreatedAt;
            that(Math.Abs((lifetime - ResetTokens.Lifetime).TotalSeconds) < 5,
                $"the link must expire after {ResetTokens.Lifetime.TotalMinutes} minutes, got {lifetime.TotalMinutes}");
        });

        check("reset: a second request inside the throttle sends NOTHING (mailbomb guard)", () =>
        {
            ClearOutbox(mailDir);

            using var anonymous = Client();
            PostAsync(anonymous, "/pl/account/forgot", new() { ["Input.Email"] = address })
                .GetAwaiter().GetResult();

            that(NewestMessage(mailDir) is null,
                "otherwise the form is a free mailbomb aimed at anyone whose address you know (040)");
        });

        check("reset: an EXPIRED link is refused, and says so without being a dead end", () =>
        {
            using (var db = Db())
            {
                var token = db.PasswordResetTokens.Single(t => t.AccountId == accountId);
                token.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                db.SaveChanges();
            }

            using var anonymous = Client();
            var page = anonymous.GetAsync($"/pl/account/reset?token={Uri.EscapeDataString(resetToken)}")
                .GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(page.Contains("nie dzia") || page.Contains("nie działa"), "the page must say the link is dead");
            that(page.Contains("/account/forgot"), "and offer a new one — expiry must never be a dead end (020)");

            using (var db = Db())
            {
                var token = db.PasswordResetTokens.Single(t => t.AccountId == accountId);
                token.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
                db.SaveChanges();
            }
        });

        // ── the one most likely to be missing ────────────────────────────
        check("reset: redeeming the link changes the password, KILLS EVERY SESSION, and burns the token", () =>
        {
            using var victim = Client();
            PostAsync(victim, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            var signedIn = victim.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(signedIn.Contains(nick), "the old session is live before the reset");

            using var anonymous = Client();
            var res = PostAsync(anonymous, "/pl/account/reset", new()
            {
                ["Input.Token"] = resetToken,
                ["Input.Password"] = newPassword,
            }, tokenPage: $"/pl/account/reset?token={Uri.EscapeDataString(resetToken)}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect to login, got {(int)res.StatusCode}");
            that(res.Headers.Location?.OriginalString.Contains("/account/login") == true,
                "a reset must NOT sign anybody in — otherwise taking over an email walks straight past 2FA");

            using var db = Db();
            that(!db.PasswordResetTokens.Any(t => t.AccountId == accountId), "the token must be gone — single use");
            that(!db.Sessions.Any(s => s.AccountId == accountId),
                "EVERY session must die: whoever forced the reset is signed out by the act of recovering the account");

            // And the old session's cookie is now worthless.
            var after = victim.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!after.Contains("Wyloguj"), "the previously signed-in browser must be signed out");
        });

        check("reset: the new password works and the old one does not", () =>
        {
            using var oldPass = Client();
            var refused = PostAsync(oldPass, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            that(refused.StatusCode == HttpStatusCode.OK, "the old password must stop working");

            using var newPass = Client();
            var accepted = PostAsync(newPass, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();
            that(accepted.StatusCode == HttpStatusCode.Redirect, "the new password must work");
        });

        check("reset: a burned token cannot be used twice", () =>
        {
            using var anonymous = Client();
            var res = anonymous.GetAsync($"/pl/account/reset?token={Uri.EscapeDataString(resetToken)}")
                .GetAwaiter().GetResult();

            var body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(body.Contains("nie dzia") || body.Contains("nie działa"),
                "single use is enforced by the row being GONE, not by a flag somebody forgets to check");
        });

        // ── two-factor ───────────────────────────────────────────────────
        string secret = "";

        check("2FA: nothing is enabled until a code proves the app agrees", () =>
        {
            using var signedIn = Client();
            PostAsync(signedIn, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();

            var page = PostAsync(signedIn, "/pl/account/two-factor?handler=StartTotp", new(),
                tokenPage: "/pl/account/two-factor").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var match = Regex.Match(page, @"name=""PendingSecret"" value=""([A-Z2-7]+)""");
            that(match.Success, "the setup page must offer a secret to enter into the app");
            secret = match.Groups[1].Value;

            using (var db = Db())
                that(db.Accounts.AsNoTracking().Single(a => a.Id == accountId).TwoFactorMethod is null,
                    "showing a secret must not enable anything — trusting the app scanned it is how people lock themselves out");

            // A wrong code must not enable it either.
            PostAsync(signedIn, "/pl/account/two-factor?handler=ConfirmTotp", new()
            {
                ["PendingSecret"] = secret,
                ["Code"] = "000000",
            }, tokenPage: "/pl/account/two-factor").GetAwaiter().GetResult();

            using (var db = Db())
                that(db.Accounts.AsNoTracking().Single(a => a.Id == accountId).TwoFactorMethod is null,
                    "a wrong confirmation code must leave it off");
        });

        check("2FA: confirming turns it on AND issues recovery codes in the same breath", () =>
        {
            using var signedIn = Client();
            PostAsync(signedIn, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();

            var code = Totp.Compute(secret, DateTimeOffset.UtcNow);
            var body = PostAsync(signedIn, "/pl/account/two-factor?handler=ConfirmTotp", new()
            {
                ["PendingSecret"] = secret,
                ["Code"] = code,
            }, tokenPage: "/pl/account/two-factor").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();

            using var db = Db();
            var account = db.Accounts.AsNoTracking().Single(a => a.Id == accountId);
            that(account.TwoFactorMethod == TwoFactorMethod.Totp, "it must now be on");
            that(account.TwoFactorSecret == secret, "and the secret stored");

            that(db.RecoveryCodes.Count(c => c.AccountId == accountId) == RecoveryCodes.Count,
                "recovery codes must be issued WITH the factor — enabling 2FA without a way back in is a way to lose an account");
            that(body.Contains("zapasowe"), "and shown to the user, once");
        });

        check("2FA: a password alone NO LONGER SIGNS ANYBODY IN", () =>
        {
            int sessionsBefore;
            using (var before = Db())
                sessionsBefore = before.Sessions.Count(s => s.AccountId == accountId);

            using var login = Client();
            var res = PostAsync(login, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");
            that(res.Headers.Location?.OriginalString.Contains("/account/challenge") == true,
                $"the correct password must lead to the challenge, went to {res.Headers.Location}");

            // The half-finished login must NOT be a login.
            var home = login.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!home.Contains("Wyloguj"),
                "a pending second factor must not grant a session — that would make 2FA decorative");

            // Counted rather than asserted absolute: earlier checks in this file
            // signed in legitimately, and those sessions are supposed to survive.
            using var db = Db();
            that(db.Sessions.Count(s => s.AccountId == accountId) == sessionsBefore,
                "and this login must not have created a session row");
        });

        check("2FA: the right code finishes the login, a wrong one does not", () =>
        {
            using var login = Client();
            PostAsync(login, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();

            PostAsync(login, "/pl/account/challenge", new() { ["Code"] = "000000" },
                tokenPage: "/pl/account/challenge").GetAwaiter().GetResult();

            var stillOut = login.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!stillOut.Contains("Wyloguj"), "a wrong code must not sign anybody in");

            var res = PostAsync(login, "/pl/account/challenge",
                new() { ["Code"] = Totp.Compute(secret, DateTimeOffset.UtcNow) },
                tokenPage: "/pl/account/challenge").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"the right code must finish the login, got {(int)res.StatusCode}");

            var home = login.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(home.Contains(nick), "and only now is there a session");
        });

        check("2FA: the challenge cannot be reached without passing the password first", () =>
        {
            using var stranger = Client();
            var res = stranger.GetAsync("/pl/account/challenge").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a bounce to login, got {(int)res.StatusCode}");
            that(res.Headers.Location?.OriginalString.Contains("/account/login") == true,
                "wandering onto the second step must reveal nothing and grant nothing");
        });

        check("2FA: a RECOVERY CODE works once and then never again", () =>
        {
            // The lost-phone path. Without it, turning on 2FA is a way to lose
            // an account — the same lockout password reset exists to prevent.
            string firstCode;
            using (var db = Db())
            {
                // Codes are stored hashed, so the check re-issues a known set
                // rather than pretending it can read them back.
                var codes = RecoveryCodes.IssueAsync(db, accountId).GetAwaiter().GetResult();
                firstCode = codes[0];
            }

            using var login = Client();
            PostAsync(login, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();

            var res = PostAsync(login, "/pl/account/challenge?recovery=true",
                new() { ["Code"] = firstCode, ["Recovery"] = "true" },
                tokenPage: "/pl/account/challenge?recovery=true").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"a recovery code must sign the user in, got {(int)res.StatusCode}");

            using (var db = Db())
                that(db.RecoveryCodes.Count(c => c.AccountId == accountId) == RecoveryCodes.Count - 1,
                    "the used code must be GONE — single use, enforced by absence rather than a flag");

            // Reusing it must fail.
            using var replay = Client();
            PostAsync(replay, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();

            var reused = PostAsync(replay, "/pl/account/challenge?recovery=true",
                new() { ["Code"] = firstCode, ["Recovery"] = "true" },
                tokenPage: "/pl/account/challenge?recovery=true").GetAwaiter().GetResult();

            // The property that matters is "did it let them in", not which
            // refusal shape came back.
            that(reused.StatusCode != HttpStatusCode.Redirect, "a used recovery code must never work twice");

            var stillOut = replay.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!stillOut.Contains("Wyloguj"), "and the browser must still be signed out");
        });

        check("2FA: disabling it clears the secret AND the recovery codes", () =>
        {
            using var signedIn = Client();
            PostAsync(signedIn, "/pl/account/login", new()
            {
                ["Input.Email"] = address,
                ["Input.Password"] = newPassword,
            }).GetAwaiter().GetResult();
            PostAsync(signedIn, "/pl/account/challenge",
                new() { ["Code"] = Totp.Compute(secret, DateTimeOffset.UtcNow) },
                tokenPage: "/pl/account/challenge").GetAwaiter().GetResult();

            PostAsync(signedIn, "/pl/account/two-factor?handler=Disable", new(),
                tokenPage: "/pl/account/two-factor").GetAwaiter().GetResult();

            using var db = Db();
            var account = db.Accounts.AsNoTracking().Single(a => a.Id == accountId);
            that(account.TwoFactorMethod is null && account.TwoFactorSecret is null, "both must be cleared");
            that(!db.RecoveryCodes.Any(c => c.AccountId == accountId),
                "codes for a factor that no longer exists are credentials lying around");
        });

        // Clean up so repeated runs stay honest.
        ClearOutbox(mailDir);
        ClearOutbox(smsDir);

        using (var db = Db())
            db.Database.ExecuteSqlRaw(
                "DELETE FROM recovery_codes WHERE account_id = {0}; " +
                "DELETE FROM one_time_codes WHERE account_id = {0}; " +
                "DELETE FROM password_reset_tokens WHERE account_id = {0}; " +
                "DELETE FROM sessions WHERE account_id = {0}; " +
                "DELETE FROM accounts WHERE id = {0};",
                accountId);
    }



}
