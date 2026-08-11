using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Checks;

/// <summary>
/// Accounts, passwords and sessions (decisions 011, 040).
///
/// Every check here targets something that <b>fails silently</b>: a password
/// that verifies against the wrong hash, a logout that does not log out, a
/// return URL that walks the user to an attacker's site. None of these produce
/// an error page — they produce a working site with a hole in it.
/// </summary>
public static class AccountChecks
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => null;
        public Guid RequireOrganiserId() => throw new InvalidOperationException("No organiser in scope.");
    }

    // ── pure checks: no database, no HTTP ────────────────────────────────
    public static void RunPure(Action<string, Action> check, Action<bool, string> that)
    {
        check("password: the right password verifies, the wrong one does not", () =>
        {
            var hash = Passwords.Hash("correct horse battery staple");
            that(Passwords.Verify("correct horse battery staple", hash), "the correct password must verify");
            that(!Passwords.Verify("Correct horse battery staple", hash), "one changed character must fail");
            that(!Passwords.Verify("", hash), "an empty password must fail");
        });

        check("password: the same password hashes DIFFERENTLY every time", () =>
        {
            // Equal hashes would mean no salt, which makes the whole table
            // crackable in one pass with a rainbow table (011).
            var a = Passwords.Hash("same-password");
            var b = Passwords.Hash("same-password");
            that(a != b, "two hashes of one password must differ — otherwise the salt is missing");
            that(Passwords.Verify("same-password", a) && Passwords.Verify("same-password", b),
                "both must still verify");
        });

        check("password: the cost parameters are stored WITH the hash", () =>
        {
            // Reading cost from the stored value rather than from constants is
            // what lets us raise the cost later without locking out every
            // existing account.
            var parts = Passwords.Hash("x").Split('$');
            that(parts.Length == 7 && parts[0] == "argon2id", "expected argon2id$v$m$t$p$salt$hash");
            that(int.Parse(parts[2]) >= 19 * 1024, "memory cost must be at least OWASP's 19 MiB");
            that(int.Parse(parts[3]) >= 2, "at least 2 iterations");
        });

        check("password: a CORRUPT stored hash fails the login instead of throwing", () =>
        {
            // A 500 on one specific account tells an attacker that row is
            // interesting. Failing closed and quietly is the correct answer.
            foreach (var junk in new[] { "", "not-a-hash", "argon2id$19$x$y$z$q$r", "argon2id$19$19456$2$1$!!!$!!!" })
                that(!Passwords.Verify("anything", junk), $"malformed hash {junk.Length} chars must return false");
        });

        // ── open redirect: the phishing vector on our own login page ──────
        check("return URL: only same-site relative paths survive", () =>
        {
            const string locale = "pl";

            foreach (var good in new[] { "/pl/ranking", "/pl/t/123?tab=bracket", "/en/lft" })
                that(Sessions.SafeReturnUrl(good, locale) == good, $"{good} should be allowed");

            // An open redirect makes OUR login page a credible phishing launcher:
            // the victim checks the domain, sees nextskill, and lands elsewhere.
            var hostile = new[]
            {
                "https://evil.example",           // absolute
                "//evil.example",                 // protocol-relative — absolute to a browser
                "/\\evil.example",                // backslash variant some parsers normalise
                "javascript:alert(1)",            // scheme injection
                "evil.example",                   // schemeless
                null,
            };

            foreach (var bad in hostile)
                that(Sessions.SafeReturnUrl(bad, locale) == "/pl",
                    $"'{bad}' must fall back to the home page, got '{Sessions.SafeReturnUrl(bad, locale)}'");
        });
    }

    // ── end-to-end: the real pipeline, real cookies, real database ───────
    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = TestHost.Create();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new NoTenant());

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var email = $"check-{stamp}@nextskill.test";
        var nick = $"Check{stamp}";
        const string password = "a-long-enough-passphrase";

        // A client that keeps cookies and does NOT chase redirects, so a 302 to
        // "/pl" is observable as success rather than silently followed.
        //
        // The base address is HTTPS deliberately. The session cookie is marked
        // Secure (011), so an http:// client would accept it and then never send
        // it back — the tests would fail for a reason that does not exist in
        // production. Fixing that by weakening the cookie in Development would
        // mean testing a configuration we never ship.
        HttpClient Client() => factory.NewClient();

        /// Razor Pages rejects a POST without the antiforgery token, so every
        /// form post here must read the real token off a real page first.
        ///
        /// `tokenPage` exists because the logout form lives in the layout rather
        /// than on a page of its own — GETting /account/logout deliberately
        /// redirects instead of rendering anything.
        Task<HttpResponseMessage> PostAsync(
            HttpClient http, string path, Dictionary<string, string> fields, string? tokenPage = null) =>
            http.PostFormAsync(path, fields, tokenPage);

        // Lives here to reuse the running app rather than start a second one.
        // It guards a bug that was invisible from the outside: the site rendered
        // fine while emitting links that silently dropped the reader's language.
        check("E2E: every emitted link keeps the reader's locale and is canonical", () =>
        {
            using var http = Client();

            foreach (var locale in new[] { "pl", "de" })
            {
                var html = http.GetAsync($"/{locale}").GetAwaiter().GetResult()
                    .Content.ReadAsStringAsync().GetAwaiter().GetResult();

                that(html.Contains($"href=\"/{locale}/ranking\""),
                    $"[{locale}] the ranking link must carry the locale — ambient route values are NOT reused across pages, " +
                    "so a missing asp-route-culture drops a German reader back into Polish (018)");

                that(!html.Contains("/ranking/index"),
                    $"[{locale}] '/ranking/index' must never be linked: two URLs for one page means pasted links stop agreeing (018)");

                // The locale switcher is the one place OTHER locales belong.
                that(html.Contains("href=\"/ru\""), $"[{locale}] the locale switcher must stay present");
            }
        });

        check("E2E: registering creates an account with a HASHED password and signs the user in", () =>
        {
            using var http = Client();
            var res = PostAsync(http, "/pl/account/register", new()
            {
                ["Input.Nickname"] = nick,
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect,
                $"a successful registration should redirect, got {(int)res.StatusCode}");

            using var db = Db();
            var account = db.Accounts.AsNoTracking().SingleOrDefault(a => a.Email == email);
            that(account is not null, "the account row must exist");
            that(account!.PasswordHash != password, "THE PASSWORD MUST NOT BE STORED IN PLAIN TEXT");
            that(account.PasswordHash.StartsWith("argon2id$"), "expected an argon2id hash");
            that(db.Sessions.Any(s => s.AccountId == account.Id), "a session row must have been created");
        });

        check("E2E: the email is normalised, so ONE address cannot become two accounts", () =>
        {
            using var http = Client();
            var res = PostAsync(http, "/pl/account/register", new()
            {
                ["Input.Nickname"] = nick + "B",
                ["Input.Email"] = email.ToUpperInvariant(),
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "a duplicate address must re-render the form, not redirect");

            using var db = Db();
            that(db.Accounts.Count(a => a.Email == email) == 1, "still exactly one account for this address");
        });

        check("E2E: a nickname differing only in CASE is refused (impersonation)", () =>
        {
            using var http = Client();
            var res = PostAsync(http, "/pl/account/register", new()
            {
                ["Input.Nickname"] = nick.ToUpperInvariant(),
                ["Input.Email"] = $"other-{stamp}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "must re-render the form rather than create the account");

            using var db = Db();
            that(!db.Accounts.Any(a => a.Email == $"other-{stamp}@nextskill.test"),
                "taking a known player's nickname with a different case must not work");
        });

        check("E2E: a wrong password and an unknown address give the SAME answer", () =>
        {
            using var wrong = Client();
            var wrongPassword = PostAsync(wrong, "/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = "definitely-not-the-password",
            }).GetAwaiter().GetResult();

            using var unknown = Client();
            var noSuchUser = PostAsync(unknown, "/pl/account/login", new()
            {
                ["Input.Email"] = $"nobody-{stamp}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            that(wrongPassword.StatusCode == HttpStatusCode.OK, "a failed login re-renders the page");
            that(noSuchUser.StatusCode == wrongPassword.StatusCode, "both failures must share a status code");

            var a = wrongPassword.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var b = noSuchUser.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            // Anything that distinguishes them turns the login form into an
            // account-enumeration oracle (040).
            that(a.Contains("Nieprawid") && b.Contains("Nieprawid"),
                "both must show the same generic error");
            that(!b.Contains("nie istnieje") && !b.Contains("not found"),
                "the unknown-address case must not say the address is unknown");
        });

        check("E2E: logging in issues a session and the headbar shows the nickname", () =>
        {
            using var http = Client();
            var res = PostAsync(http, "/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            var home = http.GetAsync("/pl").GetAwaiter().GetResult();
            var html = home.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(html.Contains(nick), "the signed-in nickname should appear in the headbar");
            that(html.Contains("Wyloguj"), "a logout control should be present");
        });

        // ── the one most likely to be missing ────────────────────────────
        check("E2E: LOGOUT actually revokes — the cookie stops working, not just disappears", () =>
        {
            using var http = Client();
            PostAsync(http, "/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            Guid accountId;
            using (var db = Db())
                accountId = db.Accounts.AsNoTracking().Single(a => a.Email == email).Id;

            var before = Db().Sessions.Count(s => s.AccountId == accountId);
            that(before > 0, "there should be a live session to revoke");

            // The token comes from the home page, where the logout form actually
            // renders — the logout route itself has no GET view by design.
            var res = PostAsync(http, "/pl/account/logout", new(), tokenPage: "/pl")
                .GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"logout should redirect, got {(int)res.StatusCode}");

            using (var db = Db())
                that(db.Sessions.Count(s => s.AccountId == accountId) < before,
                    "the session ROW must be gone — a cleared cookie alone leaves a valid token in the wild");

            var home = http.GetAsync("/pl").GetAwaiter().GetResult();
            var html = home.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!html.Contains("Wyloguj"), "the site must no longer treat this browser as signed in");
        });

        check("E2E: a session deleted server-side stops working IMMEDIATELY", () =>
        {
            // This is what a signed cookie alone cannot do, and the reason the
            // sessions table exists (011). It is also what makes a ban real.
            using var http = Client();
            PostAsync(http, "/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            var stillIn = http.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(stillIn.Contains(nick), "signed in to begin with");

            // Simulate "log out everywhere" / a ban: delete the rows only.
            using (var db = Db())
            {
                var id = db.Accounts.AsNoTracking().Single(a => a.Email == email).Id;
                db.Sessions.Where(s => s.AccountId == id).ExecuteDelete();
            }

            var after = http.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!after.Contains("Wyloguj"),
                "the same cookie must stop working the moment the session row is deleted");
        });

        check("E2E: logout over GET does NOT log anyone out", () =>
        {
            // A GET logout can be fired by any <img src> on any page, and by
            // link prefetchers by accident.
            using var http = Client();
            PostAsync(http, "/pl/account/login", new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            http.GetAsync("/pl/account/logout").GetAwaiter().GetResult();

            var html = http.GetAsync("/pl").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(html.Contains("Wyloguj"), "a GET must not end the session");
        });

        check("E2E: a form POST WITHOUT the antiforgery token is rejected (CSRF)", () =>
        {
            using var http = Client();
            var res = http.PostAsync("/pl/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password,
            })).GetAwaiter().GetResult();

            that((int)res.StatusCode == 400,
                $"an untokened POST must be refused, got {(int)res.StatusCode}");
        });

        // Runs LAST on purpose: it deliberately exhausts the window, and every
        // check above needs a working login form.
        check("E2E: repeated login ATTEMPTS are rate limited, page VIEWS are not", () =>
        {
            using var http = Client();

            // Reloading the login page must never cost budget — locking someone
            // out for refreshing punishes the wrong behaviour entirely.
            for (var i = 0; i < 30; i++)
                that(http.GetAsync("/pl/account/login").GetAwaiter().GetResult().IsSuccessStatusCode,
                    $"GET #{i + 1} of the login page must still work");

            var limited = false;
            for (var i = 0; i < 40 && !limited; i++)
            {
                var res = PostAsync(http, "/pl/account/login", new()
                {
                    ["Input.Email"] = email,
                    ["Input.Password"] = $"wrong-{i}",
                }).GetAwaiter().GetResult();

                limited = (int)res.StatusCode == 429;
            }

            that(limited, "brute-forcing a password must eventually be refused with 429");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
            db.Database.ExecuteSqlRaw(
                "DELETE FROM sessions WHERE account_id IN (SELECT id FROM accounts WHERE email LIKE {0}); " +
                "DELETE FROM accounts WHERE email LIKE {0};",
                $"%{stamp}@nextskill.test");
    }


}
