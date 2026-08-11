using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Tournaments;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Pages.Tournaments;

/// <summary>
/// Records an honest exit from a running event. A captain may forfeit only
/// their own team; organiser staff may record either side's decision. Both
/// paths leave the same explicit audit trail and never create a rated score.
/// </summary>
public class ForfeitModel(NextSkillDb db, TenantContext tenant) : PageModel
{
    public sealed record TeamChoice(Guid Id, string Name, string Tag);

    [BindProperty]
    public Guid LosingTeamId { get; set; }

    [BindProperty]
    public string? ConfirmTag { get; set; }

    [BindProperty]
    [StringLength(500, MinimumLength = 3)]
    public string? Reason { get; set; }

    public Tournament? Tournament { get; private set; }
    public Match? TournamentMatch { get; private set; }
    public TeamChoice? TeamA { get; private set; }
    public TeamChoice? TeamB { get; private set; }
    public bool CanChooseTeam { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, Guid matchId, CancellationToken cancellationToken)
    {
        var guard = await GuardAndLoadAsync(id, matchId, cancellationToken);
        if (guard is not null) return guard;

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        CanChooseTeam = CanAdminister(actor, Tournament!);
        if (!CanChooseTeam)
        {
            var choices = new[] { TeamA!, TeamB! }
                .Where(team => AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: team.Id)))
                .ToList();
            if (choices.Count == 1) LosingTeamId = choices[0].Id;
            else if (choices.Count > 1) CanChooseTeam = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        Tournament = await db.Tournaments
            .FromSqlInterpolated($"SELECT * FROM tournaments WHERE id = {id} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (Tournament is null) return NotFound();

        TournamentMatch = await db.Matches
            .FromSqlInterpolated($"SELECT * FROM matches WHERE id = {matchId} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (TournamentMatch is null || TournamentMatch.TournamentId != id) return NotFound();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var canAdminister = CanAdminister(actor, Tournament);
        var isCaptain = AccessPolicy.Can(
            actor, Operation.ManageRoster, new ResourceRef(TeamId: LosingTeamId));
        if (!canAdminister && !isCaptain) return Forbid();

        if (TournamentMatch.Status is MatchStatus.Completed or MatchStatus.Forfeited or MatchStatus.Cancelled)
            return Redirect($"/{Locales.Current}/t/{id}/bracket");

        if (!IsActionable(Tournament, TournamentMatch))
            return NotFound();

        if (LosingTeamId != TournamentMatch.TeamAId && LosingTeamId != TournamentMatch.TeamBId)
        {
            if (canAdminister && LosingTeamId == Guid.Empty)
            {
                CanChooseTeam = true;
                await LoadTeamsAsync(cancellationToken);
                ModelState.AddModelError(nameof(LosingTeamId), "Wybierz drużynę oddającą walkower.");
                return Page();
            }

            return NotFound();
        }

        // Captains may only remove their own team. Staff use the same flow but
        // may select either side when they receive the forfeit off-platform.
        if (!canAdminister && !AccessPolicy.Can(
                actor, Operation.ManageRoster, new ResourceRef(TeamId: LosingTeamId)))
            return Forbid();

        CanChooseTeam = canAdminister ||
            (AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: TournamentMatch.TeamAId)) &&
             AccessPolicy.Can(actor, Operation.ManageRoster, new ResourceRef(TeamId: TournamentMatch.TeamBId)));
        await LoadTeamsAsync(cancellationToken);
        var selected = LosingTeamId == TeamA?.Id ? TeamA : TeamB;
        if (selected is null) return NotFound();

        Reason = Reason?.Trim();
        if (Reason is null || Reason.Length is < 3 or > 500)
            ModelState.AddModelError(nameof(Reason), "Podaj powód (od 3 do 500 znaków).");

        if (!string.Equals(ConfirmTag?.Trim(), selected.Tag, StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(ConfirmTag),
                $"Wpisz dokładnie „{selected.Tag}”, żeby potwierdzić walkower.");

        if (!ModelState.IsValid) return Page();

        tenant.Set(Tournament.OrganiserId);
        await BracketBuilder.ForfeitAsync(
            db,
            TournamentMatch,
            LosingTeamId,
            actor.AccountId,
            Reason!,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/t/{id}/bracket?forfeited=true");
    }

    private async Task<IActionResult?> GuardAndLoadAsync(
        Guid tournamentId,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        Tournament = await db.FindForManagementAsync(tournamentId);
        TournamentMatch = await db.Matches.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == matchId && m.TournamentId == tournamentId, cancellationToken);
        if (Tournament is null || TournamentMatch is null || !IsActionable(Tournament, TournamentMatch))
            return NotFound();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var canAdminister = CanAdminister(actor, Tournament);
        var captainsEitherSide = new[] { TournamentMatch.TeamAId, TournamentMatch.TeamBId }
            .Where(teamId => teamId is not null)
            .Any(teamId => AccessPolicy.Can(
                actor, Operation.ManageRoster, new ResourceRef(TeamId: teamId)));
        if (!canAdminister && !captainsEitherSide) return Forbid();

        await LoadTeamsAsync(cancellationToken);
        return null;
    }

    private async Task LoadTeamsAsync(CancellationToken cancellationToken)
    {
        if (TournamentMatch?.TeamAId is not { } teamAId || TournamentMatch.TeamBId is not { } teamBId)
            return;

        var teams = await db.Teams
            .Where(t => t.Id == teamAId || t.Id == teamBId)
            .Select(t => new TeamChoice(t.Id, t.Name, t.Tag))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        TeamA = teams.FirstOrDefault(t => t.Id == teamAId);
        TeamB = teams.FirstOrDefault(t => t.Id == teamBId);
    }

    private static bool CanAdminister(Actor actor, Tournament tournament) =>
        AccessPolicy.Can(actor, Operation.AdvanceBracket, new ResourceRef(tournament.OrganiserId));

    private static bool IsActionable(Tournament tournament, Match match) =>
        tournament.Status == TournamentStatus.Running &&
        match.TeamAId is not null && match.TeamBId is not null &&
        match.SlotAResolved && match.SlotBResolved &&
        !match.IsBye &&
        match.WinnerTeamId is null &&
        match.Status is not (MatchStatus.Completed or MatchStatus.Forfeited or MatchStatus.Cancelled);
}
