using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.Seasons;

/// <summary>The season archive index (decision 021).</summary>
public class IndexModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed record SeasonRow(
        int Number, string Name, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, bool Closed, bool Active);

    public IReadOnlyList<SeasonRow> Seasons { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        Seasons = await db.Seasons
            .OrderByDescending(s => s.Number)
            .Select(s => new SeasonRow(
                s.Number,
                s.Name,
                s.StartsAt,
                s.EndsAt,
                s.Closed,
                // The same definition the ranking page uses: started, not ended,
                // not frozen. Two pages disagreeing about which season is "now"
                // is the sort of thing nobody notices until it is embarrassing.
                !s.Closed && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt > now)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
