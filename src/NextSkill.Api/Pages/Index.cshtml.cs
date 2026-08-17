using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Ratings;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages;

/// <summary>
/// The home page (decision 043).
///
/// <b>It leads with activity, not with a pitch.</b> 043 settled the contents —
/// live matches, announcements, what is coming, what just happened, and who is
/// on top — because the visitor arriving here almost always followed a link
/// someone pasted into Discord, and what convinces them is seeing the thing
/// running rather than being told about it.
///
/// Every panel is server-rendered and every panel has a designed empty state.
/// Day one has no matches, and looking <i>early</i> beats looking
/// <i>abandoned</i> (020).
/// </summary>
public class IndexModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed record LiveMatch(Guid Id, string TeamA, string TeamB, int? ScoreA, int? ScoreB, string Tournament);
    public sealed record UpcomingTournament(Guid Id, string Name, Game Game, TournamentStatus Status, DateTimeOffset? StartsAt, int Registered);
    public sealed record RecentResult(Guid Id, string Winner, string Loser, int? WinnerScore, int? LoserScore, DateTimeOffset? EndedAt);
    public sealed record TopTeam(int Rank, Guid Id, string Name, string Tag, double Rating);
    public sealed record Announcement(string Slug, string Title, DateTimeOffset PublishedAt);

    public IReadOnlyList<LiveMatch> Live { get; private set; } = [];
    public IReadOnlyList<UpcomingTournament> Upcoming { get; private set; } = [];
    public IReadOnlyList<RecentResult> Recent { get; private set; } = [];
    public IReadOnlyList<TopTeam> TopTeams { get; private set; } = [];
    public IReadOnlyList<Announcement> Announcements { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var locale = Localisation.Locales.Current;

        // Every read here crosses organisers, so every one goes through the
        // sanctioned public seam rather than reaching for IgnoreQueryFilters in
        // a page (036).
        var live = await db.PublicMatches()
            .Where(m => m.Status == MatchStatus.Live || m.Status == MatchStatus.VetoInProgress)
            .OrderBy(m => m.StartedAt)
            .Take(6)
            .Select(m => new { m.Id, m.TeamAId, m.TeamBId, m.ScoreA, m.ScoreB, m.TournamentId })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var finished = await db.PublicMatches()
            .Where(m => m.WinnerTeamId != null && m.EndedAt != null && !m.IsBye &&
                        m.Integrity == MatchIntegrity.Confirmed)
            .OrderByDescending(m => m.EndedAt)
            .Take(6)
            .Select(m => new { m.Id, m.TeamAId, m.TeamBId, m.ScoreA, m.ScoreB, m.WinnerTeamId, m.EndedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var teamIds = live.SelectMany(m => new[] { m.TeamAId, m.TeamBId })
            .Concat(finished.SelectMany(m => new[] { m.TeamAId, m.TeamBId }))
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        var teams = teamIds.Count == 0
            ? []
            : await db.Teams.AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        string Name(Guid? id) => id is { } value ? teams.GetValueOrDefault(value, "—") : "—";

        var tournamentNames = live.Count == 0
            ? []
            : await db.PublicTournaments().AsNoTracking()
                .Where(t => live.Select(m => m.TournamentId).Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        Live = live.Select(m => new LiveMatch(
            m.Id, Name(m.TeamAId), Name(m.TeamBId), m.ScoreA, m.ScoreB,
            tournamentNames.GetValueOrDefault(m.TournamentId, "—"))).ToArray();

        Recent = finished.Select(m =>
        {
            var winnerIsA = m.WinnerTeamId == m.TeamAId;
            return new RecentResult(
                m.Id,
                Name(winnerIsA ? m.TeamAId : m.TeamBId),
                Name(winnerIsA ? m.TeamBId : m.TeamAId),
                winnerIsA ? m.ScoreA : m.ScoreB,
                winnerIsA ? m.ScoreB : m.ScoreA,
                m.EndedAt);
        }).ToArray();

        // Open for entry first, then already running. A visitor who can still
        // sign up is the one with something to do.
        Upcoming = await db.PublicTournaments()
            .Where(t => t.Status == TournamentStatus.RegistrationOpen || t.Status == TournamentStatus.Running)
            .OrderBy(t => t.Status == TournamentStatus.RegistrationOpen ? 0 : 1)
            .ThenBy(t => t.StartsAt ?? DateTimeOffset.MaxValue)
            .Take(6)
            .Select(t => new UpcomingTournament(
                t.Id, t.Name, t.Game, t.Status, t.StartsAt,
                db.Registrations.IgnoreQueryFilters()
                    .Count(r => r.TournamentId == t.Id && r.WithdrawnAt == null)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        TopTeams = (await db.Ratings
                .Where(r => r.TeamId != null && r.Game == Game.Cs2 &&
                            r.Deviation <= Glicko2Rating.ProvisionalDeviation)
                .OrderByDescending(r => r.Value)
                .Take(5)
                .Join(db.Teams, r => r.TeamId, t => t.Id,
                    (r, t) => new { t.Id, t.Name, t.Tag, r.Value })
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Select((t, index) => new TopTeam(index + 1, t.Id, t.Name, t.Tag, t.Value))
            .ToArray();

        // Titles only. The body is markdown and is rendered — safely — on the
        // news pages; the home page has no business running a renderer (051 §8).
        Announcements = await db.News
            .Where(n => n.Locale == locale && n.PublishedAt != null && n.PublishedAt <= now)
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => n.PublishedAt)
            .Take(3)
            .Select(n => new Announcement(n.Slug, n.Title, n.PublishedAt!.Value))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
