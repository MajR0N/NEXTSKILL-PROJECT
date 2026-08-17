using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.News;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.News;

/// <summary>One news post (decisions 015, 018, 051 §8).</summary>
public class DetailsModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public string Title { get; private set; } = "";
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>
    /// The rendered body.
    /// </summary>
    /// <remarks>
    /// <see cref="HtmlString"/> means Razor will not escape this, which is only
    /// safe because <see cref="NewsMarkdown"/> produced it: raw HTML is disabled
    /// at the parser and every link target has been through a scheme allow-list.
    /// The single place in this application where markup bypasses escaping, and
    /// it is deliberately one place.
    /// </remarks>
    public HtmlString Body { get; private set; } = HtmlString.Empty;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var post = await db.News
            .Where(n => n.Slug == slug && n.PublishedAt != null && n.PublishedAt <= now)
            // The reader's language when it exists, Polish otherwise — 018's
            // fallback, so a post translated into fewer than four locales is
            // still readable rather than a 404.
            .OrderBy(n => n.Locale == Locales.Current ? 0 : n.Locale == Locales.Default ? 1 : 2)
            .Select(n => new { n.Title, n.Body, n.PublishedAt })
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (post is null) return NotFound();

        Title = post.Title;
        PublishedAt = post.PublishedAt!.Value;
        Body = new HtmlString(NewsMarkdown.ToHtml(post.Body));

        return Page();
    }
}
