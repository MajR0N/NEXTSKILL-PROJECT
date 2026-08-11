using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Ratings;

namespace NextSkill.Api.Pages.Ranking;

public enum RankingScope { Teams, Players }

/// <summary>
/// The ranking page — the index archetype, and the most-visited page in the
/// product (decisions 041, 043).
///
/// A vertical slice: the query, the shaping and the view live together
/// (decision 006). There is no repository and no service layer, because there is
/// one caller and inventing an interface with a single implementation is the
/// abstraction 006 warns about.
/// </summary>
public class IndexModel(NextSkillDb db, IDataProtectionProvider dataProtection) : PageModel
{
    public const int PageSize = 20;
    public const int MaxQueryLength = 60;

    // Member-initialized rather than a positional record: EF can inline these
    // properties into WHERE/ORDER BY. A constructor projection becomes opaque
    // to its SQL translator as soon as a later clause reads a property.
    private sealed class Candidate
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public string? Tag { get; init; }
        public string? City { get; init; }
        public double Score { get; init; }
        public double Deviation { get; init; }
        public int MatchesPlayed { get; init; }
        public bool Provisional { get; init; }
    }

    private sealed record CursorState(
        int Version,
        RankingScope Scope,
        Game Game,
        string? Query,
        bool Provisional,
        double Score,
        Guid Id,
        int RankedSeen);

    private readonly IDataProtector cursorProtector =
        dataProtection.CreateProtector("NextSkill.Ranking.Cursor.v1");

    /// <summary>A ranked subject as the table needs it — nothing more.</summary>
    public sealed record Row(
        Guid Id,
        RankingScope Scope,
        int? Rank,
        string Name,
        string? Tag,
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

        /// <summary>Stable colour from the public identifier, consistent across ranking views.</summary>
        public int Palette => Math.Abs((Tag ?? Name).GetHashCode(StringComparison.Ordinal)) % 5 + 1;

        /// <summary>
        /// How much of the confidence bar is filled. Wide RD reads as nearly
        /// empty, which is what makes "we are not sure yet" visible rather than
        /// hidden (025).
        /// </summary>
        public int ConfidencePercent => Scope == RankingScope.Players
            ? (int)Math.Clamp(100 - Deviation / OpenSkill.DefaultSigma * 100, 0, 100)
            : (int)Math.Clamp(100 - (Deviation - 30) / (350 - 30) * 100, 0, 100);
    }

    [BindProperty(SupportsGet = true)]
    public Game Game { get; set; } = Game.Cs2;

    [BindProperty(SupportsGet = true)]
    public RankingScope Scope { get; set; } = RankingScope.Teams;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Cursor { get; set; }

    public string? ActiveSeasonName { get; private set; }
    public IReadOnlyList<Row> Ranked { get; private set; } = [];
    public IReadOnlyList<Row> Provisional { get; private set; } = [];
    public string? NextCursor { get; private set; }
    public bool IsAfterFirstPage => Cursor is not null;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!ModelState.IsValid || !Enum.IsDefined(Game) || !Enum.IsDefined(Scope))
            return BadRequest("Nieprawidłowy filtr rankingu.");

        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        if (Q?.Length > MaxQueryLength)
            return BadRequest($"Wyszukiwanie może mieć najwyżej {MaxQueryLength} znaków.");

        CursorState? after = null;
        if (Cursor is not null)
        {
            if (!TryDecodeCursor(Cursor, out var decoded) ||
                decoded.Scope != Scope || decoded.Game != Game || decoded.Query != Q)
            {
                return BadRequest("Kursor nie pasuje do tego rankingu.");
            }

            after = decoded;
        }

        var now = DateTimeOffset.UtcNow;

        // The eyebrow used to say "Sezon 1" regardless of what existed in the
        // database. That turns a harmless-looking label into a false product
        // claim as soon as Season 2 opens (021). A season is current only while
        // it has started, has not ended and has not been frozen. If there is no
        // current season, the view shows only the game instead of inventing one.
        ActiveSeasonName = await db.Seasons
            .Where(s => !s.Closed && s.StartsAt <= now && (s.EndsAt == null || s.EndsAt > now))
            .OrderByDescending(s => s.StartsAt)
            .ThenByDescending(s => s.Number)
            .Select(s => s.Name)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (Scope == RankingScope.Players)
            await LoadPlayersAsync(after);
        else
            await LoadTeamsAsync(after);

        return Page();
    }

    private async Task LoadTeamsAsync(CursorState? after)
    {
        // Teams, Accounts and Ratings are GLOBAL — cross-organiser ranking is
        // the point of a platform rather than isolated installs (036). No
        // tenant is needed, and none is in scope on this public page.
        var query = db.Ratings
            .Where(r => r.TeamId != null && r.Game == Game)
            .Join(db.Teams, r => r.TeamId, t => t.Id, (r, t) => new Candidate
            {
                Id = t.Id,
                Name = t.Name,
                Tag = t.Tag,
                City = t.City,
                Score = r.Value,
                Deviation = r.Deviation,
                MatchesPlayed = r.MatchesPlayed,
                Provisional = r.Deviation > Glicko2Rating.ProvisionalDeviation,
            });

        if (Q is not null)
        {
            var pattern = LiteralContainsPattern(Q);
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, pattern, "\\") ||
                EF.Functions.ILike(x.Tag!, pattern, "\\"));
        }

        await LoadPageAsync(query, RankingScope.Teams, after);
    }

    private async Task LoadPlayersAsync(CursorState? after)
    {
        var query = db.Ratings
            .Where(r => r.AccountId != null && r.Game == Game)
            .Join(db.Accounts.Where(a => a.DeletedAt == null),
                r => r.AccountId, a => a.Id, (r, a) => new Candidate
            {
                Id = a.Id,
                Name = a.Nickname,
                Score = r.Value - 3 * r.Deviation,
                Deviation = r.Deviation,
                MatchesPlayed = r.MatchesPlayed,
                Provisional = r.Deviation > PlayerRating.ProvisionalSigma,
            });

        if (Q is not null)
        {
            var pattern = LiteralContainsPattern(Q);
            query = query.Where(x => EF.Functions.ILike(x.Name, pattern, "\\"));
        }

        await LoadPageAsync(query, RankingScope.Players, after);
    }

    private async Task LoadPageAsync(
        IQueryable<Candidate> source,
        RankingScope rowScope,
        CursorState? after)
    {
        if (after is not null)
        {
            // Sort tuple: provisional ASC, score DESC, id ASC. The predicate
            // describes rows strictly AFTER that tuple; unlike OFFSET, a new
            // row inserted above the cursor cannot shift this boundary (039).
            source = after.Provisional
                ? source.Where(x => x.Provisional &&
                    (x.Score < after.Score ||
                     (x.Score == after.Score && x.Id.CompareTo(after.Id) > 0)))
                : source.Where(x => x.Provisional ||
                    (!x.Provisional &&
                     (x.Score < after.Score ||
                      (x.Score == after.Score && x.Id.CompareTo(after.Id) > 0))));
        }

        var fetched = await source
            .OrderBy(x => x.Provisional)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Id)
            .Take(PageSize + 1)
            .AsNoTracking()
            .ToListAsync();

        var hasMore = fetched.Count > PageSize;
        var page = fetched.Take(PageSize).ToList();
        var rankedSeen = after?.RankedSeen ?? 0;
        var mapped = new List<Row>(page.Count);

        foreach (var x in page)
        {
            int? rank = x.Provisional ? null : ++rankedSeen;
            mapped.Add(new Row(
                Id: x.Id, Scope: rowScope,
                Rank: rank,
                Name: x.Name, Tag: x.Tag, City: x.City,
                Rating: x.Score, Deviation: x.Deviation, MatchesPlayed: x.MatchesPlayed,
                Provisional: x.Provisional));
        }

        // Provisional subjects are rated internally but kept OFF the ranked
        // list — this stops "played two matches, ranked #1" (025). They are
        // shown separately rather than hidden, so newcomers can see progress.
        Ranked = [.. mapped.Where(r => !r.Provisional)
                           .Select(r => r)];
        Provisional = [.. mapped.Where(r => r.Provisional)];

        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            NextCursor = EncodeCursor(new CursorState(
                Version: 1, Scope, Game, Query: Q,
                last.Provisional, last.Score, last.Id, rankedSeen));
        }
    }

    private static string LiteralContainsPattern(string query) =>
        $"%{query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

    private string EncodeCursor(CursorState cursor) =>
        cursorProtector.Protect(JsonSerializer.Serialize(cursor));

    private bool TryDecodeCursor(string encoded, out CursorState cursor)
    {
        cursor = null!;
        if (encoded.Length > 4096) return false;

        try
        {
            var decoded = JsonSerializer.Deserialize<CursorState>(
                cursorProtector.Unprotect(encoded));

            if (decoded is not
            {
                Version: 1,
                RankedSeen: >= 0,
            } || !double.IsFinite(decoded.Score)) return false;

            cursor = decoded;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return false;
        }
    }
}
