using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Ratings;

namespace NextSkill.Api.Pages.Players;

/// <summary>
/// A player's public identity (decision 029).
///
/// <b>The account entity is never materialised here.</b> The first projection
/// selects the public allow-list deliberately, so adding another credential or
/// piece of personal data to <see cref="Account"/> cannot make it leak from this
/// page by accident.
/// </summary>
public class DetailsModel(NextSkillDb db) : PageModel
{
    public sealed record PublicAccount(
        Guid Id,
        string Nickname,
        bool SteamLinked,
        bool DiscordLinked,
        DateTimeOffset CreatedAt,
        bool Deleted);

    public sealed record RatingRow(
        Game Game,
        double Score,
        double Deviation,
        int MatchesPlayed,
        bool Provisional)
    {
        public int ConfidencePercent =>
            (int)Math.Clamp(100 - Deviation / OpenSkill.DefaultSigma * 100, 0, 100);
    }

    public sealed record TeamRow(
        Guid Id,
        string Name,
        string Tag,
        DateTimeOffset JoinedAt,
        DateTimeOffset? LeftAt);

    public sealed record LftRow(
        Game Game,
        string? Role,
        string? Availability,
        string? Note,
        DateTimeOffset ExpiresAt);

    public PublicAccount? Player { get; private set; }
    public IReadOnlyList<RatingRow> Ratings { get; private set; } = [];
    public IReadOnlyList<TeamRow> CurrentTeams { get; private set; } = [];
    public IReadOnlyList<TeamRow> PastTeams { get; private set; } = [];
    public IReadOnlyList<LftRow> LftListings { get; private set; } = [];
    public bool IsOwnProfile { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        // Public allow-list: nickname and link PRESENCE, never the external ids.
        Player = await db.Accounts
            .Where(a => a.Id == id)
            .Select(a => new PublicAccount(
                a.Id,
                a.Nickname,
                a.SteamId != null,
                a.DiscordId != null,
                a.CreatedAt,
                a.DeletedAt != null))
            .AsNoTracking()
            .SingleOrDefaultAsync();

        if (Player is null) return NotFound();

        // Keep a stable, anonymous destination for historical match links, but
        // do not reconstruct a deleted person's ratings, teams or activity.
        if (Player.Deleted) return Page();

        IsOwnProfile = Sessions.AccountIdOf(User) == id;

        Ratings = await db.Ratings
            .Where(r => r.AccountId == id)
            .OrderBy(r => r.Game)
            .Select(r => new RatingRow(
                r.Game,
                r.Value - 3 * r.Deviation,
                r.Deviation,
                r.MatchesPlayed,
                r.Deviation > PlayerRating.ProvisionalSigma))
            .AsNoTracking()
            .ToListAsync();

        var teams = await db.TeamMemberships
            // A pending invitation is not part of someone's public team history.
            .Where(m => m.AccountId == id && m.JoinedAt != null)
            // Order before projecting the positional record. EF cannot look
            // back through that constructor to recover the SQL expression.
            .OrderByDescending(m => m.JoinedAt)
            .Join(db.Teams, m => m.TeamId, t => t.Id,
                (m, t) => new TeamRow(t.Id, t.Name, t.Tag, m.JoinedAt!.Value, m.LeftAt))
            .AsNoTracking()
            .ToListAsync();

        CurrentTeams = [.. teams.Where(t => t.LeftAt is null)];
        PastTeams = [.. teams.Where(t => t.LeftAt is not null)];

        var now = DateTimeOffset.UtcNow;
        LftListings = await db.LftListings
            .Where(l => l.AccountId == id && l.ExpiresAt > now)
            .OrderBy(l => l.Game)
            .Select(l => new LftRow(l.Game, l.Role, l.Availability, l.Note, l.ExpiresAt))
            .AsNoTracking()
            .ToListAsync();

        return Page();
    }

    public static string Initials(string nickname) => string.Concat(
        nickname.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0])));

    public static int Palette(string nickname) =>
        Math.Abs(nickname.GetHashCode(StringComparison.Ordinal)) % 5 + 1;

    public static string GameName(Game game) => game == Game.Cs2 ? "CS2" : "CS 1.6";
}
