using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.News;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;

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

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw("DELETE FROM news WHERE slug IN ({0},{1})", slug, futureSlug);
        }
    }
}
