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

    /// <summary>
    /// One match this player is recorded as having played (decisions 029, 050).
    /// </summary>
    /// <param name="Bound">
    /// Whether the game server proved it, as opposed to a captain declaring it.
    /// Shown rather than flattened, because the difference is the honest part.
    /// </param>
    public sealed record MatchRow(
        Guid MatchId,
        Game Game,
        string Team,
        string Opponent,
        bool Won,
        bool Bound,
        DateTimeOffset? EndedAt);

    public IReadOnlyList<MatchRow> RecentMatches { get; private set; } = [];

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

        await LoadRecentMatchesAsync(id);

        return Page();
    }

    /// <summary>
    /// The match history 029 refused to fabricate and can now report.
    ///
    /// Read from the participant rows frozen when each match loaded, so the
    /// answer to "who played this?" is a statement made at the time rather than
    /// today's roster projected backwards.
    /// </summary>
    private async Task LoadRecentMatchesAsync(Guid id)
    {
        var played = await db.PublicMatchesOf(id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .Select(p => new { p.MatchId, p.TeamId, p.Provenance })
            .AsNoTracking()
            .ToListAsync();

        if (played.Count == 0) return;

        var matchIds = played.Select(p => p.MatchId).ToList();
        var matches = await db.Matches
            .IgnoreQueryFilters()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Game, m.TeamAId, m.TeamBId, m.WinnerTeamId, m.EndedAt })
            .AsNoTracking()
            .ToDictionaryAsync(m => m.Id);

        var teamIds = played.Select(p => p.TeamId)
            .Concat(matches.Values.SelectMany(m => new[] { m.TeamAId, m.TeamBId })
                .Where(t => t is not null).Select(t => t!.Value))
            .Distinct()
            .ToList();

        var teams = await db.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        RecentMatches = played
            .Where(p => matches.ContainsKey(p.MatchId))
            .Select(p =>
            {
                var match = matches[p.MatchId];
                var opponentId = match.TeamAId == p.TeamId ? match.TeamBId : match.TeamAId;
                return new MatchRow(
                    p.MatchId,
                    match.Game,
                    teams.GetValueOrDefault(p.TeamId, "—"),
                    opponentId is { } other ? teams.GetValueOrDefault(other, "—") : "—",
                    match.WinnerTeamId == p.TeamId,
                    p.Provenance == ParticipantProvenance.Bound,
                    match.EndedAt);
            })
            .OrderByDescending(m => m.EndedAt)
            .ToArray();
    }

    public static string Initials(string nickname) => string.Concat(
        nickname.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(word => char.ToUpperInvariant(word[0])));

    public static int Palette(string nickname) =>
        Math.Abs(nickname.GetHashCode(StringComparison.Ordinal)) % 5 + 1;

    public static string GameName(Game game) => game == Game.Cs2 ? "CS2" : "CS 1.6";
}
