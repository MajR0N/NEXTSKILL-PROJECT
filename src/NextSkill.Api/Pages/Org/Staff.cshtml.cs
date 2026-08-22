using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Org;

/// <summary>
/// Who works for an organiser, and in what capacity (decisions 015, 036).
///
/// <b>The last unbuilt piece of the organiser panel.</b> <c>organiser_staff</c>
/// has been read on every request since Phase 1 — <see cref="Actors.CurrentAsync"/>
/// builds the whole organiser half of an <see cref="Actor"/> from it — while the
/// only way to write a row was by hand in the database. Servers, disputes and
/// tournaments all gate on a role nobody could grant.
///
/// <b>Owner-only</b>, because a referee who can appoint referees is not a
/// referee. That rule already lives in <see cref="Operation.ManageOrganiserStaff"/>;
/// this page only asks.
/// </summary>
public class StaffModel(
    NextSkillDb db,
    TenantContext tenant,
    TimeProvider clock,
    IStringLocalizer<UiText> text) : PageModel
{
    public sealed class StaffInput
    {
        public Guid OrganiserId { get; set; }
        public string Nickname { get; set; } = "";
        public OrganiserRole Role { get; set; } = OrganiserRole.Referee;

        /// <summary>Revoke only — the person losing the role.</summary>
        public Guid AccountId { get; set; }
    }

    public sealed record OrganiserChoice(Guid Id, string Name);

    public sealed record StaffRow(
        Guid OrganiserId,
        string Organiser,
        Guid AccountId,
        string Nickname,
        OrganiserRole Role,
        DateTimeOffset CreatedAt);

    [BindProperty]
    public StaffInput Input { get; set; } = new();

    public IReadOnlyList<OrganiserChoice> ManageableOrganisers { get; private set; } = [];
    public IReadOnlyList<StaffRow> Staff { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is { } denied) return denied;
        Input.OrganiserId = ManageableOrganisers[0].Id;
        return Page();
    }

    /// <summary>Appoint someone, or change the role they already hold.</summary>
    public Task<IActionResult> OnPostGrantAsync(CancellationToken cancellationToken) =>
        MutateAsync(AuditAction.GrantRole, cancellationToken, async () =>
        {
            var nickname = Input.Nickname.Trim();
            if (nickname.Length == 0 || !Enum.IsDefined(Input.Role))
            {
                ModelState.AddModelError(string.Empty, text["Staff.Error.Invalid"]);
                return null;
            }

            var target = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(
                a => a.DeletedAt == null && a.Nickname.ToLower() == nickname.ToLower(),
                cancellationToken);
            if (target is null)
            {
                ModelState.AddModelError(string.Empty, text["Staff.Error.NoSuchPlayer"]);
                return null;
            }

            var existing = await db.OrganiserStaff
                .FirstOrDefaultAsync(s => s.AccountId == target.Id, cancellationToken);

            if (existing is null)
            {
                db.OrganiserStaff.Add(new OrganiserStaff
                {
                    Id = Guid.NewGuid(),
                    OrganiserId = Input.OrganiserId,
                    AccountId = target.Id,
                    Role = Input.Role,
                    CreatedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                if (await WouldOrphanAsync(existing, Input.Role, cancellationToken)) return null;
                existing.Role = Input.Role;
            }

            return (target.Id, Input.Role.ToString());
        });

    /// <summary>Take the role away entirely.</summary>
    public Task<IActionResult> OnPostRevokeAsync(CancellationToken cancellationToken) =>
        MutateAsync(AuditAction.RevokeRole, cancellationToken, async () =>
        {
            var existing = await db.OrganiserStaff
                .FirstOrDefaultAsync(s => s.AccountId == Input.AccountId, cancellationToken);
            if (existing is null)
            {
                ModelState.AddModelError(string.Empty, text["Staff.Error.NoSuchStaff"]);
                return null;
            }

            if (await WouldOrphanAsync(existing, null, cancellationToken)) return null;

            db.OrganiserStaff.Remove(existing);
            return (existing.AccountId, existing.Role.ToString());
        });

    /// <summary>
    /// An organiser must never be left with nobody able to appoint anybody — the
    /// same safeguard as "the last captain cannot leave" (045), and the same one
    /// <c>AccountDeletion</c> already enforces from the other side.
    ///
    /// Correct only because the caller holds the organiser row lock: without it
    /// two owners could each demote the other, both having read "there are two
    /// of us".
    /// </summary>
    private async Task<bool> WouldOrphanAsync(
        OrganiserStaff existing, OrganiserRole? newRole, CancellationToken cancellationToken)
    {
        if (existing.Role != OrganiserRole.Owner || newRole == OrganiserRole.Owner) return false;

        var owners = await db.OrganiserStaff
            .CountAsync(s => s.Role == OrganiserRole.Owner, cancellationToken);
        if (owners > 1) return false;

        ModelState.AddModelError(string.Empty, text["Staff.Error.LastOwner"]);
        return true;
    }

    /// <summary>
    /// The shared shape: authorize, act, audit — all inside one transaction
    /// holding the organiser row, so the audit row cannot outlive a rolled-back
    /// decision and two staff edits cannot interleave.
    /// </summary>
    private async Task<IActionResult> MutateAsync(
        AuditAction action,
        CancellationToken cancellationToken,
        Func<Task<(Guid Subject, string Detail)?>> act)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        if (!AccessPolicy.Can(actor, Operation.ManageOrganiserStaff, new ResourceRef(Input.OrganiserId)))
        {
            // Granting yourself a role is the textbook privilege-escalation
            // attempt, and a refused one writes no row — so only the log can
            // hold it (036:173, 051 §7).
            Audit.Record(
                db, actor.AccountId, action, clock.GetUtcNow(), AuditOutcome.Denied,
                organiserId: Input.OrganiserId, retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            return Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var exists = await db.Organisers
            .FromSqlInterpolated($"SELECT * FROM organisers WHERE id = {Input.OrganiserId} FOR UPDATE")
            .AnyAsync(cancellationToken);
        if (!exists) return NotFound();

        tenant.Set(Input.OrganiserId);

        var outcome = await act();
        if (outcome is not { } result)
        {
            if (await LoadAsync(cancellationToken) is { } denied) return denied;
            return Page();
        }

        Audit.Record(
            db, actor.AccountId, action, clock.GetUtcNow(),
            organiserId: Input.OrganiserId, resourceId: result.Subject, reason: result.Detail);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/org/staff");
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var mine = actor.Organisers?.Keys.ToHashSet() ?? [];
        var candidates = await db.Organisers.AsNoTracking()
            .Where(o => mine.Contains(o.Id) || actor.Platform == PlatformRole.Owner)
            .OrderBy(o => o.Name)
            .Select(o => new OrganiserChoice(o.Id, o.Name))
            .ToListAsync(cancellationToken);

        ManageableOrganisers = candidates
            .Where(o => AccessPolicy.Can(actor, Operation.ManageOrganiserStaff, new ResourceRef(o.Id)))
            .ToList();

        if (ManageableOrganisers.Count == 0) return Forbid();

        var ids = ManageableOrganisers.Select(o => o.Id).ToArray();
        var rows = await db.OrganiserStaff
            // Intentional cross-tenant management read, the same shape as the
            // server list: authority is reduced to concrete organiser ids BEFORE
            // the filter is bypassed, so it can only ever widen to organisers
            // this actor already owns (036).
            .IgnoreQueryFilters()
            .Where(s => ids.Contains(s.OrganiserId))
            .Join(db.Organisers, s => s.OrganiserId, o => o.Id,
                (s, o) => new { s.OrganiserId, Organiser = o.Name, s.AccountId, s.Role, s.CreatedAt })
            .Join(db.Accounts, x => x.AccountId, a => a.Id,
                (x, a) => new { x.OrganiserId, x.Organiser, x.AccountId, a.Nickname, x.Role, x.CreatedAt })
            .AsNoTracking()
            .OrderBy(x => x.Organiser).ThenBy(x => x.Nickname)
            .ToListAsync(cancellationToken);

        Staff = [.. rows.Select(x => new StaffRow(
            x.OrganiserId, x.Organiser, x.AccountId, x.Nickname, x.Role, x.CreatedAt))];

        return null;
    }
}
