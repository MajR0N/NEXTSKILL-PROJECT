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

    /// <summary>
    /// Which round the <b>mobile</b> view shows (decision 023).
    ///
    /// A query parameter rather than a script, so the arrows are ordinary links:
    /// the view survives a reload, and a link pasted into Discord opens on the
    /// round the sender was looking at. Desktop renders every column regardless
    /// and never reads it.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "round")]
    public int? Round { get; set; }

    /// <summary>Index into <see cref="Columns"/>, already clamped.</summary>
    public int ShownRound { get; private set; }

    /// <summary>
    /// Follow one team's route through the bracket (decision 023).
    ///
    /// <b>023 calls this the more important of the two mobile views</b>, and it
    /// is the one a shared Discord link arrives at: somebody opening a bracket
    /// on a phone is asking "did we advance, and when do we play next", not
    /// "what is the shape of the tournament". A tree answers the second
    /// question at the cost of the first.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "team")]
    public Guid? FollowTeamId { get; set; }

    public sealed record Step(
        string Label, string Opponent, int? ScoreFor, int? ScoreAgainst,
        bool Played, bool Won, bool Live, bool Forfeited, Guid MatchId);

    public string? FollowTeamName { get; private set; }

    /// <summary>Played, next and still-unreachable matches, in bracket order.</summary>
    public IReadOnlyList<Step> Route { get; private set; } = [];

    /// <summary>Teams that can be followed, for the picker.</summary>
    public IReadOnlyList<(Guid Id, string Name)> Entrants { get; private set; } = [];

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

        var matchesById = matches.ToDictionary(m => m.Id);

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

        // Default to the round being played, because "when do we play next" is
        // the question a phone arrives with (023). An inactive reset match is
        // not an undecided round — it is a match that may never exist.
        var current = columns.FindIndex(c => c.Item2.Any(n =>
            !n.Decided && !n.IsBye && (!n.IsConditional || n.A.Team is not null)));

        ShownRound = Math.Clamp(
            Round ?? (current < 0 ? columns.Count - 1 : current),
            0, Math.Max(0, columns.Count - 1));

        // ── follow one team (023) ────────────────────────────────────────
        Entrants = [.. matches
            .SelectMany(m => new[] { m.TeamAId, m.TeamBId })
            .Where(x => x is not null).Select(x => x!.Value).Distinct()
            .Where(teams.ContainsKey)
            .Select(x => (x, teams[x].Name))
            .OrderBy(x => x.Name)];

        if (FollowTeamId is { } followed && teams.TryGetValue(followed, out var followedTeam))
        {
            FollowTeamName = followedTeam.Name;

            // Every match this team is IN, in the order the columns are drawn —
            // which is already upper, then lower, then the final, so a lower
            // bracket run reads in the sequence it was played.
            var order = columns
                .SelectMany(column => column.Item2.Select(node => (Node: node, Label: column.Item1)))
                .ToList();

            Route = [.. order
                .Where(x => matchesById[x.Node.Id] is { } m &&
                            (m.TeamAId == followed || m.TeamBId == followed))
                .Select(x =>
                {
                    var m = matchesById[x.Node.Id];
                    var isA = m.TeamAId == followed;
                    var opponentId = isA ? m.TeamBId : m.TeamAId;
                    var opponentResolved = isA ? m.SlotBResolved : m.SlotAResolved;

                    return new Step(
                        x.Label,
                        opponentId is { } o && teams.TryGetValue(o, out var opponent)
                            ? opponent.Name
                            // A resolved empty opponent is a walkover, and an
                            // unresolved one is a match still waiting upstream.
                            // Saying "—" for both would hide a free win.
                            : opponentResolved ? "wolny los" : "czeka na rywala",
                        isA ? m.ScoreA : m.ScoreB,
                        isA ? m.ScoreB : m.ScoreA,
                        m.WinnerTeamId is not null,
                        m.WinnerTeamId == followed,
                        m.Status == MatchStatus.Live,
                        m.Status == MatchStatus.Forfeited,
                        m.Id);
                })];
        }

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
