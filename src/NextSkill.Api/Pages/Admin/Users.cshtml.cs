using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Admin;

/// <summary>
/// Platform moderation (decisions 015, 036).
///
/// <b>A ban is not a soft delete.</b> Erasure is the account holder's right and
/// destroys their data; a ban is our decision and keeps everything, because the
/// record of why someone was removed is the entire value of having removed them.
/// </summary>
public class UsersModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed class BanInput
    {
        public Guid AccountId { get; set; }
        public string Reason { get; set; } = "";
    }

    public sealed record UserRow(Guid Id, string Nickname, DateTimeOffset? BannedAt, string? BanReason);

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public BanInput Input { get; set; } = new();

    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await GuardAsync(cancellationToken) is { } denied) return denied;

        Q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();

        // Deliberately not an "all users" list. A moderation screen that dumps
        // every account by default invites browsing people rather than acting on
        // a report, and the page exists for the second thing.
        if (Q is null) return Page();

        var pattern = "%" + Q.ToLowerInvariant()
            .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        Users = await db.Accounts
            .Where(a => a.DeletedAt == null &&
                        EF.Functions.Like(NextSkillDb.Unaccent(a.Nickname.ToLower()), NextSkillDb.Unaccent(pattern), "\\"))
            .OrderBy(a => a.Nickname)
            .Take(25)
            .Select(a => new UserRow(a.Id, a.Nickname, a.BannedAt, a.BanReason))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Page();
    }

    public Task<IActionResult> OnPostBanAsync(CancellationToken cancellationToken) =>
        DecideAsync(ban: true, cancellationToken);

    public Task<IActionResult> OnPostUnbanAsync(CancellationToken cancellationToken) =>
        DecideAsync(ban: false, cancellationToken);

    private async Task<IActionResult> DecideAsync(bool ban, CancellationToken cancellationToken)
    {
        var reason = Input.Reason.Trim();
        if (reason.Length is 0 or > 500) return BadRequest();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ModerateUsers)) return Forbid();

        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == Input.AccountId && a.DeletedAt == null, cancellationToken);
        if (target is null) return NotFound();

        // Staff moderate players, not each other, and certainly not the owner.
        // Without this, one compromised staff account can lock out the people
        // who would notice.
        if (target.PlatformRole is not null) return Forbid();

        var now = clock.GetUtcNow();

        target.BannedAt = ban ? now : null;
        target.BanReason = ban ? reason : null;
        target.BannedByAccountId = ban ? actor.AccountId : null;

        if (ban)
        {
            // Kill every live session immediately. Leaving them alive would mean
            // a banned account keeps its access until a cookie expires, which is
            // the exact failure the session row exists to prevent (011, 047).
            await db.Sessions
                .Where(s => s.AccountId == target.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        Audit.Record(
            db, actor.AccountId, AuditAction.BanUser, now,
            resourceId: target.Id,
            reason: ban ? reason : $"unban: {reason}");

        await db.SaveChangesAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/admin/users?q={Uri.EscapeDataString(target.Nickname)}");
    }

    private async Task<IActionResult?> GuardAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        return AccessPolicy.Can(actor, Operation.ModerateUsers) ? null : Forbid();
    }
}
