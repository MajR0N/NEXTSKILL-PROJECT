using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.News;

/// <summary>News index (decisions 015, 021).</summary>
public class IndexModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed record PostRow(string Slug, string Title, bool Pinned, DateTimeOffset PublishedAt);

    public IReadOnlyList<PostRow> Posts { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // Unpublished and future-dated posts are invisible. A scheduled
        // announcement that leaks early is the CMS equivalent of a draft
        // tournament leaking (046).
        Posts = await db.News
            .Where(n => n.Locale == Locales.Current && n.PublishedAt != null && n.PublishedAt <= now)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.PublishedAt)
            .Take(50)
            .Select(n => new PostRow(n.Slug, n.Title, n.Pinned, n.PublishedAt!.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
