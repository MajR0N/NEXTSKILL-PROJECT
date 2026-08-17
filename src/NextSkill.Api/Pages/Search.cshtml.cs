using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages;

/// <summary>
/// One search box, one results page (decisions 021, 043).
///
/// <b>A plain GET form that works with JavaScript disabled.</b> 018 makes that a
/// hard rule and 043 keeps typeahead for Phase 5 — a search that only exists
/// once a script runs is a search a crawler cannot follow, and links are the
/// growth mechanism.
/// </summary>
public class SearchModel(NextSkillDb db) : PageModel
{
    public const int MaxQueryLength = 60;
    private const int PerKind = 10;

    public sealed record Hit(string Kind, Guid Id, string Title, string? Subtitle);

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public IReadOnlyList<Hit> Teams { get; private set; } = [];
    public IReadOnlyList<Hit> Players { get; private set; } = [];
    public IReadOnlyList<Hit> Tournaments { get; private set; } = [];

    public bool Searched { get; private set; }
    public int Total => Teams.Count + Players.Count + Tournaments.Count;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        if (Q is null) return Page();

        if (Q.Length > MaxQueryLength)
            return BadRequest($"Zapytanie może mieć najwyżej {MaxQueryLength} znaków.");

        Searched = true;

        // `%` and `_` are LITERAL text here, not operators the visitor gets to
        // use — the same treatment the ranking filter already applies (039).
        // Without it, a single `%` matches everything and becomes a free scan.
        var pattern = "%" + Q.ToLowerInvariant()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        // f_unaccent on BOTH sides, so "lodz" finds "Łódź" and "Łódź" finds
        // "lodz". Matching only one side would be a half-fix that looks right in
        // whichever direction it was tested.
        Teams = await db.Teams
            .Where(t => t.DisbandedAt == null &&
                        (EF.Functions.Like(NextSkillDb.Unaccent(t.Name.ToLower()), NextSkillDb.Unaccent(pattern), "\\") ||
                         EF.Functions.Like(NextSkillDb.Unaccent(t.Tag.ToLower()), NextSkillDb.Unaccent(pattern), "\\")))
            .OrderBy(t => t.Name)
            .Take(PerKind)
            .Select(t => new Hit("team", t.Id, t.Name, t.Tag))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Deleted accounts keep a tombstone so old match links resolve, but they
        // are not findable — erasure that leaves you searchable is not erasure.
        Players = await db.Accounts
            .Where(a => a.DeletedAt == null &&
                        EF.Functions.Like(NextSkillDb.Unaccent(a.Nickname.ToLower()), NextSkillDb.Unaccent(pattern), "\\"))
            .OrderBy(a => a.Nickname)
            .Take(PerKind)
            .Select(a => new Hit("player", a.Id, a.Nickname, null))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Through the public seam, so a draft cannot be discovered by guessing
        // its name — the leak 046 closed for the list and by-id lookup (036).
        Tournaments = await db.PublicTournaments()
            .Where(t => EF.Functions.Like(NextSkillDb.Unaccent(t.Name.ToLower()), NextSkillDb.Unaccent(pattern), "\\"))
            .OrderByDescending(t => t.StartsAt ?? t.CreatedAt)
            .Take(PerKind)
            .Select(t => new Hit("tournament", t.Id, t.Name, t.Game == Game.Cs2 ? "CS2" : "CS 1.6"))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Page();
    }
}
