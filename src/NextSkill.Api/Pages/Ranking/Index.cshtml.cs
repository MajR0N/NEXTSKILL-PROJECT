using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Ratings;

namespace NextSkill.Api.Pages.Ranking;

/// <summary>
/// The ranking page — the index archetype, and the most-visited page in the
/// product (decisions 041, 043).
///
/// A vertical slice: the query, the shaping and the view live together
/// (decision 006). There is no repository and no service layer, because there is
/// one caller and inventing an interface with a single implementation is the
/// abstraction 006 warns about.
/// </summary>
public class IndexModel(NextSkillDb db) : PageModel
{
    /// <summary>A team as the table needs it — nothing more.</summary>
    public sealed record Row(
        int? Rank,
        string Name,
        string Tag,
        string? City,
        double Rating,
        double Deviation,
        int MatchesPlayed,
        bool Provisional)
    {
        /// <summary>Generated monogram, so a brand-new team never shows a grey placeholder (019).</summary>
        public string Initials => string.Concat(
            Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(w => char.ToUpperInvariant(w[0])));

        /// <summary>Stable colour from the tag, so a team looks the same on every page.</summary>
        public int Palette => Math.Abs(Tag.GetHashCode(StringComparison.Ordinal)) % 5 + 1;

        /// <summary>
        /// How much of the confidence bar is filled. Wide RD reads as nearly
        /// empty, which is what makes "we are not sure yet" visible rather than
        /// hidden (025).
        /// </summary>
        public int ConfidencePercent =>
            (int)Math.Clamp(100 - (Deviation - 30) / (350 - 30) * 100, 0, 100);
    }

    [BindProperty(SupportsGet = true)]
    public Game Game { get; set; } = Game.Cs2;

    public IReadOnlyList<Row> Ranked { get; private set; } = [];
    public IReadOnlyList<Row> Provisional { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Teams and Ratings are GLOBAL — cross-organiser ranking is the entire
        // point of a platform rather than isolated installs (036). No tenant is
        // needed, and none is in scope on a public page.
        var rows = await db.Ratings
            .Where(r => r.TeamId != null && r.Game == Game)
            .Join(db.Teams, r => r.TeamId, t => t.Id, (r, t) => new
            {
                t.Name, t.Tag, t.City,
                r.Value, r.Deviation, r.MatchesPlayed,
            })
            .OrderByDescending(x => x.Value)
            .AsNoTracking()
            .ToListAsync();

        var mapped = rows.Select(x => new Row(
            Rank: null,
            Name: x.Name, Tag: x.Tag, City: x.City,
            Rating: x.Value, Deviation: x.Deviation, MatchesPlayed: x.MatchesPlayed,
            // Same threshold as the engine, not a copy of the number (025).
            Provisional: new Glicko2Rating(x.Value, x.Deviation, 0.06).IsProvisional))
            .ToList();

        // Provisional teams are rated internally but kept OFF the ranked list —
        // this is what stops "played two matches, ranked #1" (025). They are
        // shown separately rather than hidden, so a new team can see itself.
        Ranked = [.. mapped.Where(r => !r.Provisional)
                           .Select((r, i) => r with { Rank = i + 1 })];
        Provisional = [.. mapped.Where(r => r.Provisional)];
    }
}
