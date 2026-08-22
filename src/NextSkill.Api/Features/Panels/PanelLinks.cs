using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Features.Panels;

/// <summary>
/// Which panel sections the current visitor may actually reach (decisions 015,
/// 021, 036).
///
/// <b>Nine working pages had no link anywhere in the product.</b> Servers,
/// disputes, organiser staff and the whole of <c>/admin</c> were reachable only
/// by typing a URL — each slice skipped navigation on the grounds that the
/// pages beside it had none either, which is how a panel gets finished and
/// stays invisible.
///
/// Every entry is an <b>operation</b> question, never a role one (036), so the
/// nav cannot drift from what the page itself will allow: a link that appears
/// and then 403s is worse than no link.
/// </summary>
public sealed class PanelLinks(NextSkillDb db)
{
    public sealed record Link(string Path, string TextKey);

    /// <summary>
    /// Where the headbar entry points, or null when this visitor has no panel.
    /// </summary>
    /// <remarks>
    /// ponytail: one query per rendered page for signed-in visitors, the same
    /// shape the notification bell already pays, and deliberately coarser than
    /// <see cref="ForAsync"/> — the headbar only needs somewhere to start.
    /// Ceiling: a second round trip on every page. Upgrade path: fold both into
    /// one projection if a profiler ever cares, not before.
    /// </remarks>
    public async Task<string?> EntryAsync(
        ClaimsPrincipal user, string locale, CancellationToken ct = default)
    {
        if (Sessions.AccountIdOf(user) is not { } accountId) return null;

        var found = await db.Accounts
            .Where(a => a.Id == accountId && a.DeletedAt == null)
            .Select(a => new
            {
                a.PlatformRole,
                // Cross-tenant by nature and scoped to one account id: "does
                // this person work for anybody", which no single tenant's
                // filter can answer.
                Organiser = db.OrganiserStaff.IgnoreQueryFilters().Any(s => s.AccountId == accountId),
            })
            .FirstOrDefaultAsync(ct);

        if (found is null) return null;

        // Moderation is the platform panel's front door; everyone else — an
        // organiser's staff, and platform Support, who may only read health —
        // starts at the server list.
        if (found.PlatformRole is PlatformRole.Owner or PlatformRole.Staff)
            return $"/{locale}/admin/users";

        return found.PlatformRole is not null || found.Organiser
            ? $"/{locale}/org/servers"
            : null;
    }

    /// <summary>
    /// The reachable sections, in the order 015 lists them. Only called on a
    /// panel path, so the fuller authority lookup is paid where it is used.
    /// </summary>
    public async Task<IReadOnlyList<Link>> ForAsync(
        ClaimsPrincipal user, string locale, CancellationToken ct = default)
    {
        var actor = await Actors.CurrentAsync(db, user, ct);
        if (actor is null) return [];

        var links = new List<Link>();

        // Organiser scope: allowed in ANY organiser this person works for, since
        // the pages themselves project across exactly that set.
        var organisers = actor.Organisers?.Keys ?? [];

        bool InAny(Operation op) => organisers.Any(id => AccessPolicy.Can(actor, op, new ResourceRef(id)));

        if (InAny(Operation.ViewServerHealth) || actor.Platform is not null)
            links.Add(new($"/{locale}/org/servers", "Panel.Servers"));

        // Platform staff and the owner reach this without an organiser role,
        // which is [036](036-authorization.md)'s whole point: a dispute BETWEEN
        // two organisers has no referee inside either of them.
        if (InAny(Operation.ResolveDispute)
            || actor.Platform is PlatformRole.Owner or PlatformRole.Staff)
            links.Add(new($"/{locale}/org/disputes", "Panel.Disputes"));

        if (InAny(Operation.ManageOrganiserStaff) || actor.Platform == PlatformRole.Owner)
            links.Add(new($"/{locale}/org/staff", "Panel.Staff"));

        // Platform scope.
        if (AccessPolicy.Can(actor, Operation.ModerateUsers))
        {
            links.Add(new($"/{locale}/admin/users", "Panel.Users"));
            links.Add(new($"/{locale}/admin/reports", "Panel.Reports"));
        }
        if (AccessPolicy.Can(actor, Operation.ManageNews))
            links.Add(new($"/{locale}/admin/news", "Panel.News"));
        if (AccessPolicy.Can(actor, Operation.SetOrganiserTrusted))
            links.Add(new($"/{locale}/admin/organisers", "Panel.Organisers"));
        if (AccessPolicy.Can(actor, Operation.MergeAccounts))
            links.Add(new($"/{locale}/admin/merge", "Panel.Merge"));
        if (AccessPolicy.Can(actor, Operation.ViewAuditLog))
            links.Add(new($"/{locale}/admin/audit", "Panel.Audit"));

        return links;
    }
}
