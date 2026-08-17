using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Features.Notifications;

/// <summary>
/// The unread count in the headbar (decision 043).
///
/// Notification rows have been written on every invite and roster change since
/// the teams slice, and until now <b>nothing could read them</b> — invites were
/// findable only because the teams page listed them as a stopgap. This is the
/// smallest thing that makes the table mean something.
///
/// <b>Server-rendered, not polled.</b> 018 makes working without JavaScript a
/// hard rule, and a count that only appears once a script runs is a count that
/// is absent for a crawler, a text browser and anyone on a bad connection.
/// Polling is a Phase 5 enhancement on top of a number that is already correct.
/// </summary>
public sealed class NotificationBell(NextSkillDb db)
{
    /// <summary>
    /// Unread notifications for the signed-in visitor, or zero for a stranger.
    ///
    /// ponytail: one COUNT per rendered page for signed-in visitors, served by
    /// the <c>(AccountId, ReadAt, CreatedAt)</c> index. Ceiling: it is a query
    /// on every page view. Upgrade path: cache it in the session row and
    /// invalidate on write — worth doing when a profiler says so and not before.
    /// </summary>
    public async Task<int> UnreadCountAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (Sessions.AccountIdOf(user) is not { } accountId) return 0;

        return await db.Notifications
            .Where(n => n.AccountId == accountId && n.ReadAt == null)
            .CountAsync(ct);
    }
}
