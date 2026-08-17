using System.Text.Json;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Teams;

namespace NextSkill.Api.Shared.Persistence;

// Roles are DEFINED ONCE, in NextSkill.Domain.Access, and reused here.
//
// They were briefly defined in both places. A completeness check written to
// detect enum drift caught the duplication immediately — and the right fix was
// not to check for drift but to make it impossible. Two enums describing one
// concept will always diverge eventually, and in an authorization model that
// divergence means someone gets a permission nobody granted.

public enum NotificationType
{
    TeamInvite,
    TeamInviteAccepted,
    RosterChanged,
    MatchScheduled,
    MatchStartingSoon,
    ResultConfirmed,
    ResultDisputed,
    RatingUpdated,
}

// ── entities ────────────────────────────────────────────────────────────

/// <summary>
/// Who plays for which team. <b>Global</b>, like teams and accounts — a roster
/// is not owned by an organiser (decision 036).
/// </summary>
public class TeamMembership
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>Authority: captain or member (036).</summary>
    public TeamRole Role { get; set; }

    /// <summary>
    /// What kind of place they occupy: starting player, substitute, or coach
    /// (decision 045).
    ///
    /// <b>Deliberately a second axis, not a wider Role.</b> A coach can be the
    /// captain and a starting player can have no powers — folding them together
    /// would mean promoting somebody to captain silently put them in the lineup.
    /// </summary>
    public RosterSlot Slot { get; set; }

    /// <summary>Null until the invite is accepted — an invite is a pending membership.</summary>
    public DateTimeOffset? JoinedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}

/// <summary>Staff of one organiser. Tenant-scoped (decision 036).</summary>
public class OrganiserStaff : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid AccountId { get; set; }
    public OrganiserRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An in-app notification (decision 043).
///
/// <b>Global, not tenant-scoped</b> — a player may be invited by any organiser's
/// team, so scoping these would hide exactly the invites they need to see.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public NotificationType Type { get; set; }

    /// <summary>
    /// Rendering data — team name, match id, and so on.
    /// <b>Never a secret</b>: no link codes, no <c>setinfo</c> tokens
    /// (decision 012).
    /// </summary>
    public JsonDocument? Data { get; set; }

    /// <summary>Null while unread. Drives the bell count.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A looking-for-team listing (decisions 021, 043).
///
/// What makes this better than a Discord message is that the rating is
/// <b>real</b>, taken from match history rather than self-claimed.
/// </summary>
public class LftListing
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Game Game { get; set; }

    /// <summary>Self-declared: AWP, IGL, entry, support.</summary>
    public string? Role { get; set; }

    /// <summary>Free text: "wieczory", "weekendy".</summary>
    public string? Availability { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// <b>Expires after 14 days</b>, renewable in one click (decision 043).
    /// A stale board is worse than an empty one — nothing kills an LFT board
    /// faster than messaging five people who all quit months ago.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A season. Tournaments group into one; past seasons stay browsable with
/// standings frozen (decision 021).
/// </summary>
public class Season
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>
    /// Once closed, standings are <b>never recomputed</b>. A later result
    /// reversal does not rewrite history (decision 021).
    /// </summary>
    public bool Closed { get; set; }

    /// <summary>
    /// <b>When</b> it closed — and this is what makes the freeze work
    /// (decision 051 §4).
    ///
    /// 021 froze standings forever while 025 derived them at read time, and
    /// those two cannot both be true. The resolution is neither a snapshot table
    /// nor a recomputation ban: the ledger is append-only and every row carries
    /// <c>AppliedAt</c>, so a closed season's table is simply the ledger read
    /// with <c>AppliedAt &lt;= ClosedAt</c>. History stops moving without
    /// anything being copied, and a correction made afterwards is visible in the
    /// live ranking while leaving the archive alone.
    ///
    /// Null while open. <see cref="Closed"/> stays as the flag the queries
    /// filter on, because a boolean is what "is this over" should read like.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>
/// A news post — what the CMS from the original brief actually manages
/// (decision 021).
/// </summary>
public class NewsPost
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }

    /// <summary>
    /// One row per locale. A post may exist in fewer than four; the reader gets
    /// the Polish fallback rather than an empty page
    /// (decision 018).
    /// </summary>
    public required string Locale { get; set; }

    public required string Title { get; set; }

    /// <summary>
    /// <b>Sanitised markdown, never raw HTML</b> (decision 040 §5). Rendering
    /// user-supplied HTML on a page every visitor sees is a stored-XSS vector.
    /// </summary>
    public required string Body { get; set; }

    /// <summary>Pinned posts surface on the home page (decision 043).</summary>
    public bool Pinned { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
