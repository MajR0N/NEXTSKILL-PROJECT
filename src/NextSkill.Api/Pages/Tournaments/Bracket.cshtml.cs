using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// The bracket — the <b>wide canvas</b> archetype (decisions 023, 041).
///
/// Its own route rather than a tab, because it is a different page shape: full
/// bleed with its own horizontal scroll, while the tournament page is a 1120px
/// detail column. <c>/t/{id}/bracket</c> is also the link that actually gets
/// pasted mid-event (018).
/// </summary>
public class BracketModel(NextSkillDb db) : PageModel
{
    public sealed record Slot(string? Team, string? Tag, int? Seed, int? Score, bool Won, bool Lost);

    public sealed record Node(
        Guid Id, BracketSide Side, int Round, int Position,
        Slot A, Slot B, bool IsBye, bool IsConditional, bool Decided, bool Live,
        bool Forfeited, string? ForfeitReason, bool CanForfeit);

    [BindProperty(SupportsGet = true)]
    public bool Forfeited { get; set; }

    public Tournament? Tournament { get; private set; }
    public string OrganiserName { get; private set; } = "";

    /// <summary>Columns, in the order they are drawn: upper rounds, then lower, then the final.</summary>
    public IReadOnlyList<(string Label, IReadOnlyList<Node> Nodes)> Columns { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var tournament = await db.FindForManagementAsync(id);
        if (tournament is null) return NotFound();

        var actor = await Actors.CurrentAsync(db, User);
        var canManage = actor is not null
            && AccessPolicy.Can(actor, Operation.RunTournament, new ResourceRef(tournament.OrganiserId));

        // Same visibility rule as the tournament page: a draft belongs to its
        // organiser and nobody else (046).
        if (!TournamentLifecycle.IsPublic(tournament.Status) && !canManage) return NotFound();

        Tournament = tournament;
        OrganiserName = await db.Organisers.Where(o => o.Id == tournament.OrganiserId)
            .Select(o => o.Name).FirstAsync();

        // Cross-tenant read: a bracket is public, and the visitor has no
        // organiser in scope (036). Scoped to one tournament id.
        var matches = await db.Matches.IgnoreQueryFilters()
            .Where(m => m.TournamentId == id)
            .AsNoTracking()
            .ToListAsync();

        if (matches.Count == 0) return Page();

        var teamIds = matches.SelectMany(m => new[] { m.TeamAId, m.TeamBId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();

        var teams = await db.Teams.Where(t => teamIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Tag })
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Id);

        var seeds = await db.PublicRegistrations(id)
            .Where(r => r.Seed != null)
            .AsNoTracking()
            .ToDictionaryAsync(r => r.TeamId, r => r.Seed!.Value);

        Slot ToSlot(Guid? teamId, int? score, Guid? winner) =>
            teamId is not { } tid || !teams.TryGetValue(tid, out var team)
                ? new Slot(null, null, null, null, false, false)
                : new Slot(team.Name, team.Tag, seeds.GetValueOrDefault(tid),
                    score, winner == tid, winner is not null && winner != tid);

        var canAdminister = actor is not null &&
            AccessPolicy.Can(actor, Operation.AdvanceBracket, new ResourceRef(tournament.OrganiserId));

        bool CanForfeit(Match match) =>
            actor is not null &&
            tournament.Status == TournamentStatus.Running &&
            match.TeamAId is not null && match.TeamBId is not null &&
            match.SlotAResolved && match.SlotBResolved &&
            !match.IsBye && match.WinnerTeamId is null &&
            match.Status is not (MatchStatus.Completed or MatchStatus.Forfeited or MatchStatus.Cancelled) &&
            (canAdminister ||
             AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: match.TeamAId)) ||
             AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: match.TeamBId)));

        var nodes = matches.Select(m => new Node(
            m.Id, m.Side, m.Round, m.Position,
            ToSlot(m.TeamAId, m.ScoreA, m.WinnerTeamId),
            ToSlot(m.TeamBId, m.ScoreB, m.WinnerTeamId),
            m.IsBye, m.IsConditional,
            m.WinnerTeamId is not null,
            m.Status == MatchStatus.Live,
            m.Status == MatchStatus.Forfeited,
            m.ForfeitReason,
            CanForfeit(m))).ToList();

        var columns = new List<(string, IReadOnlyList<Node>)>();

        foreach (var side in new[] { BracketSide.Upper, BracketSide.Lower })
        {
            foreach (var round in nodes.Where(n => n.Side == side).Select(n => n.Round).Distinct().Order())
            {
                var inRound = nodes.Where(n => n.Side == side && n.Round == round)
                    .OrderBy(n => n.Position).ToList();

                columns.Add((RoundLabel(side, round, inRound.Count), inRound));
            }
        }

        var grandFinal = nodes.Where(n => n.Side == BracketSide.GrandFinal).OrderBy(n => n.Position).ToList();
        if (grandFinal.Count > 0) columns.Add(("Wielki finał", grandFinal));

        Columns = columns;
        return Page();
    }

    /// <summary>
    /// "Ćwierćfinał" beats "Runda 2" when it is true, and lying about it is
    /// worse than either — so the name comes from how many matches are actually
    /// in the round.
    /// </summary>
    private static string RoundLabel(BracketSide side, int round, int matchCount)
    {
        var prefix = side == BracketSide.Lower ? "Dolna · " : "";
        return prefix + matchCount switch
        {
            1 => side == BracketSide.Lower ? "finał dolnej" : "Finał",
            2 => "Półfinał",
            4 => "Ćwierćfinał",
            _ => $"Runda {round}",
        };
    }
}
