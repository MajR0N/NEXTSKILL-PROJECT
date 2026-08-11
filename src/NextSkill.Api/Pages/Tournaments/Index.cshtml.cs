using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// The tournament list — every organiser's events in one place (decision 021).
///
/// This is the page that makes NEXTSKILL a platform rather than a set of
/// isolated installs, and it is the reason <see cref="PublicReads"/> exists:
/// a visitor has no organiser in scope, so the tenant filter would throw.
/// </summary>
public class IndexModel(NextSkillDb db) : PageModel
{
    public sealed record Row(
        Guid Id, string Name, string Organiser, Game Game, TeamSize TeamSize,
        TournamentFormat Format, TournamentStatus Status, int Teams, int MaxTeams,
        DateTimeOffset? StartsAt);

    /// <summary>Open first: the only group a visitor can act on (043).</summary>
    public IReadOnlyList<Row> Open { get; private set; } = [];
    public IReadOnlyList<Row> Live { get; private set; } = [];
    public IReadOnlyList<Row> Past { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var rows = await db.PublicTournaments()
            .Join(db.Organisers, t => t.OrganiserId, o => o.Id, (t, o) => new { t, o.Name })
            .Select(x => new Row(
                x.t.Id, x.t.Name, x.Name, x.t.Game, x.t.TeamSize, x.t.Format, x.t.Status,
                db.Registrations.IgnoreQueryFilters()
                    .Count(r => r.TournamentId == x.t.Id && r.WithdrawnAt == null),
                x.t.MaxTeams, x.t.StartsAt))
            .AsNoTracking()
            .ToListAsync();

        Open = [.. rows.Where(r => r.Status is TournamentStatus.RegistrationOpen or TournamentStatus.RegistrationClosed)
                       .OrderBy(r => r.StartsAt ?? DateTimeOffset.MaxValue).ThenBy(r => r.Name)];

        Live = [.. rows.Where(r => r.Status == TournamentStatus.Running).OrderBy(r => r.Name)];

        Past = [.. rows.Where(r => r.Status is TournamentStatus.Completed or TournamentStatus.Cancelled)
                       .OrderByDescending(r => r.StartsAt ?? DateTimeOffset.MinValue).ThenBy(r => r.Name)];
    }

    public static string Size(TeamSize size) => $"{(int)size}v{(int)size}";

    public static string FormatName(TournamentFormat format) => format switch
    {
        TournamentFormat.SingleElimination => "Pojedyncza eliminacja",
        TournamentFormat.DoubleElimination => "Podwójna eliminacja",
        TournamentFormat.RoundRobin => "Każdy z każdym",
        TournamentFormat.GroupStage => "Faza grupowa",
        TournamentFormat.League => "Liga",
        _ => format.ToString(),
    };
}
