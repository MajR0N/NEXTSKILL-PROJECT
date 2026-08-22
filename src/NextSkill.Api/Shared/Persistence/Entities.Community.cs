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

    /// <summary>
    /// The published vacancy this invitation came from, when it came from one
    /// (decision 056).
    ///
    /// <b>The board's only real success metric depends on this column.</b> 056
    /// is explicit that mutual contact is a conversation start and only an
    /// accepted, slot-specific roster invitation counts as recruitment success —
    /// and that the two must stay <i>separately measurable</i>. Without a link
    /// back to the opening, "somebody joined a team" and "the LFT board filled a
    /// vacancy" are the same row and the claim cannot be made honestly.
    ///
    /// It is also what lets acceptance close the opening as filled, rather than
    /// conversion optimistically closing it before the player agreed.
    /// </summary>
    public Guid? FromTeamOpeningId { get; set; }

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

    /// <summary>Free text, kept for the pre-056 rows. Superseded by <see cref="Roles"/>.</summary>
    public string? Role { get; set; }

    /// <summary>Free text, kept for the pre-056 rows. Superseded by <see cref="AvailabilityHours"/>.</summary>
    public string? Availability { get; set; }

    /// <summary>Plain text, 300 characters, rendered inert — no markup, no clickable links.</summary>
    public string? Note { get; set; }

    // ── structured, from 056 ─────────────────────────────────────────────
    //
    // Product vocabulary rather than user-authored labels, because the entire
    // advantage over a Discord advert is that these fields mean the same thing
    // for everybody. `coach` is deliberately NOT an in-game role — it is a
    // roster slot, and 045 already says so.

    /// <summary>Up to two of IGL, AWP, rifler, entry, support, lurker, flexible.</summary>
    public string[] Roles { get; set; } = [];

    /// <summary>Stable language codes; never inferred from nationality.</summary>
    public string[] Languages { get; set; } = [];

    /// <summary>Playable latency, not nationality. A platform-managed allow-list.</summary>
    public string[] Regions { get; set; } = [];

    /// <summary>
    /// Which roster places this player will accept — player, substitute, coach.
    /// A separate axis from in-game role: seeking a starting place and accepting
    /// bench duty are different requests.
    /// </summary>
    public string[] Slots { get; set; } = [];

    /// <summary>
    /// Weekly availability as <b>UTC hours-of-week</b> (0–167), so genuine
    /// overlap is an array intersection the database can answer rather than a
    /// timezone reconciliation done per row.
    /// </summary>
    public int[] AvailabilityHours { get; set; } = [];

    /// <summary>The publisher's zone, so their own windows read back correctly.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Increments on publication, explicit renewal or a material edit — never on
    /// a note edit, which must not buy fresh prominence.
    /// </summary>
    public int Cycle { get; set; } = 1;

    /// <summary>
    /// What the board orders by. Distinct from <see cref="CreatedAt"/> so a
    /// renewal moves the row while the advert keeps its identity.
    /// </summary>
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>
    /// <b>Expires after 14 days</b> and <b>never auto-renews</b> (043, 056).
    /// A stale board is worse than an empty one — nothing kills an LFT board
    /// faster than messaging five people who all quit months ago. Automatic
    /// renewal would make board activity dishonest, which is the same failure
    /// wearing a friendlier face.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Sent once, 48 hours before expiry.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    /// <summary>Closed by the player. Kept privately for republication.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Taken down by platform staff after a report (decisions 015, 056).
    ///
    /// A takedown sets <see cref="ClosedAt"/> too, so every public read already
    /// hides it and no query had to change. This column is what stops the
    /// author simply republishing the same row a minute later — without it a
    /// moderation queue would be theatre.
    /// </summary>
    public DateTimeOffset? ModeratedAt { get; set; }

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
