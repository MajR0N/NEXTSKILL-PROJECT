namespace NextSkill.Domain.Access;

/// <summary>Platform-wide staff role. Null for ordinary users (decision 036).</summary>
public enum PlatformRole { Owner, Staff, Support }

/// <summary>Role within one organiser.</summary>
public enum OrganiserRole { Owner, Admin, Referee }

/// <summary>Role within one team.</summary>
public enum TeamRole { Captain, Member }

/// <summary>
/// Everything an action can be. Endpoints ask about an <b>operation</b>, never
/// about a role (decision 036).
///
/// Asking "is this user an organiser_admin?" scatters policy across the codebase
/// and makes changing it a search-and-replace. Asking "may this user advance
/// this bracket?" keeps the answer in one place.
/// </summary>
public enum Operation
{
    // organiser scope
    ManageTournament,       // create, edit, delete a tournament
    RunTournament,          // seed, start, close registration
    AdvanceBracket,         // move a result through the bracket
    ResolveDispute,         // decide a disputed match
    RegisterServer,
    ViewServerHealth,
    ManageOrganiserStaff,
    DeleteOrganiser,

    // team scope
    ManageRoster,           // invite, remove, change roles
    ManageTeam,             // rename, re-tag, disband — the team, not its people
    RestoreTeam,            // captain whose tenure ended at the disband moment
    ConfirmResult,          // captain agrees the score
    DisputeResult,          // captain contests the score

    // platform scope
    ModerateUsers,
    ViewAuditLog,
    SetMaintenanceMode,
    SetOrganiserTrusted,

    /// <summary>
    /// Reading an RCON password, a <c>setinfo</c> token, a webhook URL or a
    /// server signing secret.
    ///
    /// <b>Denied to everyone, including the platform owner</b>
    /// (decisions 007, 012). It exists as an operation precisely so the denial
    /// is explicit and testable rather than merely absent.
    /// </summary>
    ReadSecret,
}

/// <summary>What the operation is being performed on.</summary>
public readonly record struct ResourceRef(Guid? OrganiserId = null, Guid? TeamId = null);

/// <summary>
/// Who is asking. Roles in three independent scopes, because a person routinely
/// holds all three — an organiser is nearly always a player too.
/// </summary>
public sealed record Actor(
    Guid AccountId,
    PlatformRole? Platform = null,
    IReadOnlyDictionary<Guid, OrganiserRole>? Organisers = null,
    IReadOnlyDictionary<Guid, TeamRole>? Teams = null,
    IReadOnlySet<Guid>? RestorableTeams = null)
{
    public OrganiserRole? RoleIn(Guid? organiserId) =>
        organiserId is { } id && Organisers?.TryGetValue(id, out var r) == true ? r : null;

    public TeamRole? RoleInTeam(Guid? teamId) =>
        teamId is { } id && Teams?.TryGetValue(id, out var r) == true ? r : null;

    public bool MayRestoreTeam(Guid? teamId) =>
        teamId is { } id && RestorableTeams?.Contains(id) == true;
}

/// <summary>
/// The whole authorization policy, as one pure function (decision 036).
///
/// Pure on purpose: the rules that decide who may delete a tournament or
/// resolve a dispute are far easier to trust when every combination can be
/// enumerated in a test without HTTP, a database or a logged-in user.
/// </summary>
public static class AccessPolicy
{
    public static bool Can(Actor actor, Operation op, ResourceRef on = default)
    {
        // ── the one rule nothing overrides ───────────────────────────────
        //
        // No role reads a secret. Not staff, not the owner. The panel shows
        // STATUS, never secrets — which removes insider risk and the temptation
        // along with it (007, 012). Placed first so no later branch can grant it.
        if (op == Operation.ReadSecret) return false;

        // ── platform scope ───────────────────────────────────────────────
        if (op is Operation.SetMaintenanceMode or Operation.SetOrganiserTrusted)
            return actor.Platform == PlatformRole.Owner;

        if (op is Operation.ModerateUsers)
            return actor.Platform is PlatformRole.Owner or PlatformRole.Staff;

        if (op is Operation.ViewAuditLog)
            return actor.Platform is PlatformRole.Owner or PlatformRole.Staff;

        // Platform owner may act inside any organiser — but this is deliberately
        // NOT extended to staff. Staff moderate people; they do not run other
        // people's tournaments.
        var organiserRole = actor.RoleIn(on.OrganiserId);
        var isPlatformOwner = actor.Platform == PlatformRole.Owner;

        // ── organiser scope ──────────────────────────────────────────────
        switch (op)
        {
            case Operation.DeleteOrganiser:
                return isPlatformOwner || organiserRole == OrganiserRole.Owner;

            case Operation.ManageOrganiserStaff:
                return isPlatformOwner || organiserRole == OrganiserRole.Owner;

            case Operation.ManageTournament:
            case Operation.RegisterServer:
                return isPlatformOwner
                    || organiserRole is OrganiserRole.Owner or OrganiserRole.Admin;

            case Operation.RunTournament:
                // A referee runs matches, not tournament structure (036).
                return isPlatformOwner
                    || organiserRole is OrganiserRole.Owner or OrganiserRole.Admin;

            case Operation.AdvanceBracket:
            case Operation.ResolveDispute:
                // Referees exist for exactly this. Every use is audited (036).
                return isPlatformOwner
                    || organiserRole is OrganiserRole.Owner or OrganiserRole.Admin or OrganiserRole.Referee
                    // Staff resolve CROSS-ORGANISER disputes, which is why they
                    // reach this one operation without an organiser role.
                    || (op == Operation.ResolveDispute && actor.Platform == PlatformRole.Staff);

            case Operation.ViewServerHealth:
                return isPlatformOwner
                    || actor.Platform is PlatformRole.Staff or PlatformRole.Support
                    || organiserRole is not null;
        }

        // ── team scope ───────────────────────────────────────────────────
        var teamRole = actor.RoleInTeam(on.TeamId);

        return op switch
        {
            // Only the captain changes the roster — and a platform owner cannot,
            // because that is not a moderation action, it is playing someone
            // else's game for them.
            Operation.ManageRoster => teamRole == TeamRole.Captain,

            // Separate from ManageRoster on purpose. Removing a player and
            // deleting the whole team are the same authority today, but they are
            // not the same ACT — and the moment a manager-style role exists that
            // may shuffle a roster without being able to dissolve the team, the
            // split is already where it needs to be. One enum value now beats
            // untangling a merged rule later.
            Operation.ManageTeam => teamRole == TeamRole.Captain,

            // Restoration is a historical entitlement, not a current team
            // role: only a captain whose tenure ended at this team's latest
            // disband moment may make it live again.
            Operation.RestoreTeam => actor.MayRestoreTeam(on.TeamId),

            // Confirming and disputing a result are the corroboration that
            // anti-match-fixing rests on (007), so they belong to the captain
            // of a team that actually played.
            Operation.ConfirmResult or Operation.DisputeResult => teamRole == TeamRole.Captain,

            _ => false,   // unknown operation: deny. Fail closed.
        };
    }

    /// <summary>
    /// Throwing variant for call sites where a denial is exceptional.
    /// </summary>
    public static void Require(Actor actor, Operation op, ResourceRef on = default)
    {
        if (!Can(actor, op, on))
            throw new UnauthorizedAccessException($"{op} denied for account {actor.AccountId}.");
    }
}
