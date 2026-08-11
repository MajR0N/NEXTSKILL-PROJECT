using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Teams;

/// <summary>
/// Restores a soft-disbanded team without rewriting its old roster (decision
/// 045). Eligibility comes from the history itself: the caller must have been
/// an active captain whose membership closed at the exact disband moment.
/// </summary>
public class RestoreModel(NextSkillDb db) : PageModel
{
    [BindProperty]
    public string? ConfirmTag { get; set; }

    public Team? Team { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var guard = await GuardAsync(id);
        return guard ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialises double submits and two eligible former captains racing.
        // Exactly one new captain membership is created.
        Team = await db.Teams
            .FromSqlInterpolated($"SELECT * FROM teams WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (Team is null) return NotFound();
        if (Team.DisbandedAt is null)
            return Redirect($"/{Locales.Current}/teams/{id}");

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.RestoreTeam, new ResourceRef(TeamId: id)))
            return Forbid();

        var formerCaptain = await FormerCaptainAsync(id, accountId, Team.DisbandedAt.Value, cancellationToken);
        if (formerCaptain is null) return Forbid();

        // Restoration is less destructive than disbanding, but it makes a
        // public organisation live again. Retyping its reserved tag prevents an
        // accidental click from doing that silently.
        if (!string.Equals(ConfirmTag?.Trim(), Team.Tag, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ConfirmTag), $"Wpisz dokładnie „{Team.Tag}”, żeby potwierdzić.");
            return Page();
        }

        var restoredAt = DateTimeOffset.UtcNow;
        Team.DisbandedAt = null;

        // A NEW membership preserves the previous tenure's LeftAt. Former
        // players are not silently signed back up; the restoring captain can
        // invite them again and they choose whether to accept.
        db.TeamMemberships.Add(new TeamMembership
        {
            Id = Guid.NewGuid(),
            TeamId = id,
            AccountId = accountId,
            Role = TeamRole.Captain,
            Slot = formerCaptain.Slot,
            JoinedAt = restoredAt,
            CreatedAt = restoredAt,
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/teams/{id}?restored=true");
    }

    private async Task<IActionResult?> GuardAsync(Guid id)
    {
        Team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (Team is null || Team.DisbandedAt is null) return NotFound();
        var actor = await Actors.CurrentAsync(db, User);
        if (actor is null) return Challenge();

        return AccessPolicy.Can(actor, Operation.RestoreTeam, new ResourceRef(TeamId: id))
            ? null
            : Forbid();
    }

    private Task<TeamMembership?> FormerCaptainAsync(
        Guid teamId,
        Guid accountId,
        DateTimeOffset disbandedAt,
        CancellationToken cancellationToken = default) =>
        db.TeamMemberships
            .AsNoTracking()
            .Where(m =>
                m.TeamId == teamId &&
                m.AccountId == accountId &&
                m.Role == TeamRole.Captain &&
                m.JoinedAt != null &&
                m.LeftAt == disbandedAt)
            .OrderByDescending(m => m.JoinedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
