using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Shared.Auth;

/// <summary>
/// Builds the <see cref="Actor"/> that <see cref="AccessPolicy"/> reasons about
/// (decision 036).
///
/// <b>Roles are read fresh, never taken from the cookie.</b> Baking them into the
/// signed cookie would be one fewer query and a real bug: a captain removed from
/// a roster would keep captain powers until their cookie expired. Authorization
/// has to answer "now", not "when you logged in".
///
/// 036 deferred ASP.NET authorization handlers to this phase. Having arrived
/// here, they are still not worth building: the policy is already a pure
/// function with 30 checks behind it, and wrapping it in
/// <c>IAuthorizationHandler</c> plumbing would add indirection without adding a
/// single rule. Page handlers call <see cref="AccessPolicy.Can"/> directly.
/// </summary>
public static class Actors
{
    public static async Task<Actor?> CurrentAsync(
        NextSkillDb db, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (Sessions.AccountIdOf(user) is not { } accountId) return null;

        var platformRole = await db.Accounts
            .Where(a => a.Id == accountId && a.DeletedAt == null)
            .Select(a => a.PlatformRole)
            .FirstOrDefaultAsync(ct);

        // Only ACCEPTED, current memberships confer a role. A pending invite is
        // a membership row with no JoinedAt, and it must grant nothing at all.
        var teams = await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.JoinedAt != null && m.LeftAt == null)
            .ToDictionaryAsync(m => m.TeamId, m => m.Role, ct);

        // Restoration is earned from history, not from a current role. The
        // exact timestamp join identifies captains whose active tenure was
        // closed by this team's latest disband operation.
        var restorableTeams = await db.TeamMemberships
            .Where(m =>
                m.AccountId == accountId &&
                m.Role == TeamRole.Captain &&
                m.JoinedAt != null &&
                m.LeftAt != null)
            .Join(
                db.Teams.Where(t => t.DisbandedAt != null),
                m => new { m.TeamId, At = m.LeftAt },
                t => new { TeamId = t.Id, At = t.DisbandedAt },
                (_, t) => t.Id)
            .ToHashSetAsync(ct);

        // IgnoreQueryFilters is deliberate and named: we are asking "which
        // organisers does THIS PERSON work for", which is inherently a
        // cross-tenant question and cannot be answered inside one tenant's
        // filter. 036 permits the escape hatch precisely here — scoped to one
        // account id, so it can only ever return the caller's own rows.
        var organisers = await db.OrganiserStaff
            .IgnoreQueryFilters()
            .Where(s => s.AccountId == accountId)
            .ToDictionaryAsync(s => s.OrganiserId, s => s.Role, ct);

        return new Actor(accountId, platformRole, organisers, teams, restorableTeams);
    }
}
