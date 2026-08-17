using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Ratings;

namespace NextSkill.Api.Pages.Seasons;

/// <summary>
/// One season's standings (decisions 021, 051 §4).
///
/// <b>Frozen by timestamp, not by snapshot.</b> A closed season's table is the
/// append-only ledger read as at the moment it closed. Nothing is copied, so
/// there is no second copy to drift; a referee reversal after the fact changes
/// the live ranking and leaves the archive exactly as it was, which is what 021
/// meant by "never recomputed" and what 025 meant by "derived at read time" —
/// two claims that could not both hold until the ledger existed.
/// </summary>
public class DetailsModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed record Standing(int Rank, Guid TeamId, string Name, string Tag, double Rating, double Deviation);

    public Season? Season { get; private set; }
    public bool Frozen { get; private set; }
    public DateTimeOffset? AsAt { get; private set; }
    public IReadOnlyList<Standing> Standings { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "game")]
    public Game Game { get; set; } = Game.Cs2;

    public async Task<IActionResult> OnGetAsync(int number, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(Game)) Game = Game.Cs2;

        Season = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Number == number, cancellationToken);
        if (Season is null) return NotFound();

        // A closed season is read as at its closing instant; an open one is read
        // as at now, which is simply the live table under a season heading.
        Frozen = Season.Closed;
        AsAt = Frozen ? Season.ClosedAt : clock.GetUtcNow();

        // A season closed before this column existed has no instant to read at.
        // Saying so beats inventing one and silently publishing the wrong table.
        if (Frozen && AsAt is null) return Page();

        var cutoff = AsAt!.Value;

        // Every live team rating movement up to the cutoff. Superseded rows are
        // excluded: a reversal replaced them, and an archive built from
        // corrected-away numbers would be a worse record, not a more faithful one.
        var movements = await db.RatingChanges
            .Where(r => r.SubjectIsTeam &&
                        r.Game == Game &&
                        r.SupersededAt == null &&
                        r.ValueAfter != null &&
                        r.AppliedAt <= cutoff)
            .OrderByDescending(r => r.AppliedAt)
            .Select(r => new { r.SubjectId, r.ValueAfter, r.DeviationAfter })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (movements.Count == 0) return Page();

        // Last movement per team as at the cutoff — that IS their rating then.
        //
        // ponytail: reduced in memory over every movement up to the cutoff.
        // Ceiling: a long season pulls a lot of rows to produce one table.
        // Upgrade path: PostgreSQL DISTINCT ON (subject_id), which EF cannot
        // express and which is not worth raw SQL before a profiler asks.
        var latest = movements
            .GroupBy(m => m.SubjectId)
            .ToDictionary(g => g.Key, g => g.First());

        var teams = await db.Teams.AsNoTracking()
            .Where(t => latest.Keys.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new { t.Name, t.Tag }, cancellationToken);

        Standings = latest
            // Provisional teams are left out of the standings for the same
            // reason they are left out of the live ranking: "played two matches,
            // finished #1" is not a season result (025).
            .Where(x => x.Value.DeviationAfter <= Glicko2Rating.ProvisionalDeviation)
            .Where(x => teams.ContainsKey(x.Key))
            .OrderByDescending(x => x.Value.ValueAfter)
            .ThenBy(x => x.Key)
            .Select((x, index) => new Standing(
                index + 1,
                x.Key,
                teams[x.Key].Name,
                teams[x.Key].Tag,
                x.Value.ValueAfter!.Value,
                x.Value.DeviationAfter!.Value))
            .ToArray();

        return Page();
    }
}
