using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.News;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;

namespace NextSkill.Checks;

/// <summary>
/// News markdown, and the stored-XSS hole it would have shipped with
/// (decisions 040 §5, 051 §8).
///
/// <b>These exist because <c>DisableHtml()</c> was checked and found
/// insufficient.</b> It blocks the attack everyone thinks of — an author
/// embedding a script tag — and leaves untouched the one an author actually
/// reaches for, which is an ordinary markdown link with a hostile scheme in it.
/// </summary>
public static class NewsChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        // ── the renderer, in isolation ───────────────────────────────────
        check("news: javascript:, vbscript: and data: links do NOT survive rendering", () =>
        {
            var html = NewsMarkdown.ToHtml(
                "[kliknij](javascript:alert(1)) " +
                "[albo tu](vbscript:msgbox(1)) " +
                "[albo tu](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==) " +
                "[i tu](JavaScript:alert(2))");

            foreach (var scheme in new[] { "javascript:", "vbscript:", "data:", "JavaScript:" })
                that(!html.Contains(scheme, StringComparison.OrdinalIgnoreCase),
                    $"'{scheme}' must not reach the rendered page — DisableHtml() alone lets it through");

            // The words stay. Silently deleting an author's text would hide the
            // sanitising from the person who caused it.
            that(html.Contains("kliknij", StringComparison.Ordinal),
                "the link TEXT must survive; only the target is removed");
        });

        check("news: ordinary links still work — the allow-list is not a wall", () =>
        {
            var html = NewsMarkdown.ToHtml(
                "[https](https://example.test/a) [relative](/pl/ranking) " +
                "[anchor](#sekcja) [mail](mailto:kontakt@example.test)");

            that(html.Contains("https://example.test/a", StringComparison.Ordinal), "https must survive");
            that(html.Contains("/pl/ranking", StringComparison.Ordinal), "a relative link must survive");
            that(html.Contains("#sekcja", StringComparison.Ordinal), "an in-page anchor must survive");
            that(html.Contains("mailto:kontakt@example.test", StringComparison.Ordinal), "mailto must survive");
        });

        check("news: raw HTML and scheme-relative URLs are both refused", () =>
        {
            var html = NewsMarkdown.ToHtml(
                "<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>\n\n[protokół](//evil.example)");

            that(!html.Contains("<script", StringComparison.OrdinalIgnoreCase),
                "raw HTML must be disabled at the parser");

            // DisableHtml() ESCAPES rather than strips, so the characters of an
            // event handler do appear — as inert text inside an escaped tag that
            // no browser will ever parse as markup. Asserting the substring is
            // absent would be asserting the wrong thing; what matters is that no
            // TAG is emitted.
            that(!html.Contains("<img", StringComparison.OrdinalIgnoreCase),
                "an image tag must never be emitted, however it was written");
            that(html.Contains("&lt;img", StringComparison.OrdinalIgnoreCase),
                "it must be escaped into visible text, which is what makes the handler inert");
            that(!html.Contains("//evil.example", StringComparison.Ordinal),
                "a scheme-relative URL is absolute in disguise — the same trick the " +
                "open-redirect guard already refuses (040)");
        });

        // ── and the same thing, through the real page ────────────────────
        using var factory = TestHost.Create();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(null));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var slug = $"test-post-{stamp}";
        var futureSlug = $"future-post-{stamp}";

        using (var db = Db())
        {
            db.News.AddRange(
                new NewsPost
                {
                    Id = Guid.NewGuid(), Slug = slug, Locale = "pl",
                    Title = $"Ogłoszenie {stamp}",
                    Body = "Zwykły akapit z [linkiem](javascript:alert(1)) i [dobrym](https://example.test/ok).",
                    PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                new NewsPost
                {
                    Id = Guid.NewGuid(), Slug = futureSlug, Locale = "pl",
                    Title = $"Zaplanowane {stamp}",
                    Body = "Jeszcze nie teraz.",
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(30),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            db.SaveChanges();
        }

        check("news: a published post renders, sanitised, end to end", () =>
        {
            using var reader = factory.NewClient();
            var html = WebUtility.HtmlDecode(
                reader.GetStringAsync($"/pl/news/{slug}").GetAwaiter().GetResult());

            that(html.Contains($"Ogłoszenie {stamp}", StringComparison.Ordinal),
                "the post must render");
            that(html.Contains("https://example.test/ok", StringComparison.Ordinal),
                "and keep its legitimate link");
            that(!html.Contains("javascript:", StringComparison.OrdinalIgnoreCase),
                "THE STORED XSS, closed end to end — not just in a unit of the renderer");
        });

        check("news: a scheduled post is invisible until its time", () =>
        {
            using var reader = factory.NewClient();

            var index = WebUtility.HtmlDecode(
                reader.GetStringAsync("/pl/news").GetAwaiter().GetResult());
            that(index.Contains($"Ogłoszenie {stamp}", StringComparison.Ordinal),
                "the published post must be listed");
            that(!index.Contains($"Zaplanowane {stamp}", StringComparison.Ordinal),
                "a future-dated post must not be listed");

            var direct = reader.GetAsync($"/pl/news/{futureSlug}").GetAwaiter().GetResult();
            that(direct.StatusCode == HttpStatusCode.NotFound,
                $"nor reachable by guessing its slug, got {(int)direct.StatusCode}");
        });

        // ── the editor: until it existed, every post above was hand-seeded ──

        var cmsSlug = $"cms-post-{stamp}";
        var draftSlug = $"cms-draft-{stamp}";
        const string password = "a-long-enough-passphrase";

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

        var editorNick = $"NEd{stamp}";
        var playerNick = $"NPl{stamp}";
        using var editor = SignedInAs(editorNick);
        using var player = SignedInAs(playerNick);

        Guid editorId, playerId;
        using (var db = Db())
        {
            editorId = db.Accounts.Single(a => a.Nickname == editorNick).Id;
            playerId = db.Accounts.Single(a => a.Nickname == playerNick).Id;
            db.Accounts.Single(a => a.Id == editorId).PlatformRole = PlatformRole.Staff;
            db.SaveChanges();
        }

        HttpResponseMessage Save(HttpClient http, Dictionary<string, string> fields, string tokenPage) =>
            http.PostFormAsync("/pl/admin/news", fields, tokenPage).GetAwaiter().GetResult();

        check("news CMS: a player cannot reach the editor, and a hand-written POST is refused AND recorded", () =>
        {
            var page = player.GetAsync("/pl/admin/news").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.Forbidden,
                $"the editor writes the page every visitor sees; got {(int)page.StatusCode}");

            var response = Save(player, new()
            {
                ["Input.Slug"] = $"hostile-{stamp}",
                ["Input.Locale"] = "pl",
                ["Input.Title"] = "Nie",
                ["Input.Body"] = "Nie.",
            }, "/pl/me");

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"and skipping the page must not skip the check; got {(int)response.StatusCode}");

            using var db = Db();
            that(!db.News.Any(n => n.Slug == $"hostile-{stamp}"), "nothing may have been written");
            that(db.AuditEntries.Count(a =>
                    a.ActorAccountId == playerId &&
                    a.Action == AuditAction.PublishNews &&
                    a.Outcome == AuditOutcome.Denied) == 1,
                "a refused publish writes no post — only the log can hold the attempt");
        });

        check("news CMS: staff write a post through the FORM and readers get it, sanitised", () =>
        {
            var response = Save(editor, new()
            {
                ["Input.Slug"] = cmsSlug,
                ["Input.Locale"] = "pl",
                ["Input.Title"] = $"Z edytora {stamp}",
                ["Input.Body"] = "Tekst z [linkiem](javascript:alert(1)) i [dobrym](https://example.test/cms).",
                ["Input.PublishedAt"] = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm"),
            }, "/pl/admin/news");

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a successful save redirects; got {(int)response.StatusCode}");

            using var reader = factory.NewClient();
            var html = WebUtility.HtmlDecode(
                reader.GetStringAsync($"/pl/news/{cmsSlug}").GetAwaiter().GetResult());

            that(html.Contains($"Z edytora {stamp}", StringComparison.Ordinal),
                "the post an author actually wrote must reach the reader");
            that(html.Contains("https://example.test/cms", StringComparison.Ordinal),
                "with its legitimate link intact");
            that(!html.Contains("javascript:", StringComparison.OrdinalIgnoreCase),
                "THE AUTHOR PATH is the one the sanitiser exists for — the seeded rows never proved it");

            using var db = Db();
            that(db.AuditEntries.Any(a =>
                    a.ActorAccountId == editorId && a.Action == AuditAction.PublishNews &&
                    a.Outcome == AuditOutcome.Allowed),
                "and who published it is answerable");
        });

        check("news CMS: no date means draft, and editing publishes the SAME row", () =>
        {
            Save(editor, new()
            {
                ["Input.Slug"] = draftSlug,
                ["Input.Locale"] = "pl",
                ["Input.Title"] = $"Szkic {stamp}",
                ["Input.Body"] = "Jeszcze nieskończone.",
            }, "/pl/admin/news");

            using (var db = Db())
            {
                var draft = db.News.AsNoTracking().Single(n => n.Slug == draftSlug);
                that(draft.PublishedAt is null, "an empty date must mean draft, not 'now'");
            }

            var invisible = factory.NewClient();
            that(invisible.GetAsync($"/pl/news/{draftSlug}").GetAwaiter().GetResult().StatusCode
                    == HttpStatusCode.NotFound,
                "and a draft must not be reachable by guessing its slug");
            invisible.Dispose();

            Guid id;
            using (var db = Db()) id = db.News.AsNoTracking().Single(n => n.Slug == draftSlug).Id;

            Save(editor, new()
            {
                ["Input.Id"] = id.ToString(),
                ["Input.Slug"] = draftSlug,
                ["Input.Locale"] = "pl",
                ["Input.Title"] = $"Szkic {stamp}",
                ["Input.Body"] = "Już skończone.",
                ["Input.PublishedAt"] = DateTime.UtcNow.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm"),
            }, "/pl/admin/news");

            using var after = Db();
            var rows = after.News.AsNoTracking().Where(n => n.Slug == draftSlug).ToList();
            that(rows.Count == 1,
                $"editing must update the post, not fork it into two under one slug; got {rows.Count}");
            that(rows[0].PublishedAt is not null && rows[0].Id == id, "and it is the same row, now published");
        });

        check("news CMS: a duplicate slug+locale is refused with a message, not a 500", () =>
        {
            var response = Save(editor, new()
            {
                ["Input.Slug"] = cmsSlug,
                ["Input.Locale"] = "pl",
                ["Input.Title"] = "Drugi raz",
                ["Input.Body"] = "To samo miejsce.",
            }, "/pl/admin/news");

            that(response.StatusCode == HttpStatusCode.OK,
                $"the database rule must surface as a form error; got {(int)response.StatusCode}");

            using var db = Db();
            that(db.News.Count(n => n.Slug == cmsSlug && n.Locale == "pl") == 1,
                "and exactly one post keeps that address");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM news WHERE slug IN ({0},{1},{2},{3})", slug, futureSlug, cmsSlug, draftSlug);
        }
    }
}
