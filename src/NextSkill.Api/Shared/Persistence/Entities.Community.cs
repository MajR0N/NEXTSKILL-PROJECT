using System.Text.Json;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Api.Shared.Persistence;

// ── roles (decision 036) ────────────────────────────────────────────────
// Three independent scopes. A person can hold roles in all three at once.

/// <summary>Platform-wide staff. Null for ordinary users, which is nearly everyone.</summary>
public enum PlatformRole { Owner, Staff, Support }

/// <summary>Role within one organiser. Tenant-scoped.</summary>
public enum OrganiserRole { Owner, Admin, Referee }

/// <summary>Role within one team.</summary>
public enum TeamRole { Captain, Member }

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
    public TeamRole Role { get; set; }

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
