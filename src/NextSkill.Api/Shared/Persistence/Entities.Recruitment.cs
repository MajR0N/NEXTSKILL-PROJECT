using NextSkill.Domain.Teams;

namespace NextSkill.Api.Shared.Persistence;

// ── recruitment (decision 056) ───────────────────────────────────────────
//
// FIVE distinct objects, and deliberately not one polymorphic table whose
// nullable foreign keys change what a row means. The directions, permissions
// and lifecycle costs genuinely differ: an invitation costs a roster place and
// an expression of interest does not, which is the whole reason 045's roster
// caps could be reopened by getting this wrong.

/// <summary>
/// Where a request stands (decision 056). Transitions are allow-listed
/// commands, never entity binding.
/// </summary>
public enum RecruitmentState
{
    Pending,
    Accepted,
    Declined,
    Withdrawn,
    Expired,

    /// <summary>The advert behind it started a new cycle. Neutral, not a rejection.</summary>
    ListingChanged,

    OpeningClosed,

    /// <summary>Became a real roster invitation — the only thing that counts as success.</summary>
    Converted,
}

/// <summary>
/// What place on a roster is being sought or offered.
///
/// Separate from <see cref="RosterSlot"/> on purpose: this is a
/// <i>willingness</i>, and a player may accept several. A support player seeking
/// a starting place is not making the same request as one accepting bench duty.
/// </summary>
public enum RecruitmentSlot { Player, Substitute, Coach }

/// <summary>
/// A team's published vacancy (decision 056).
///
/// <b>Spare capacity is not an opening.</b> One exists only because a captain
/// deliberately published it, which is what stops the board filling with
/// theoretical vacancies nobody intends to fill.
/// </summary>
public class TeamOpening
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Game Game { get; set; }

    /// <summary>Exactly one roster place per opening.</summary>
    public RecruitmentSlot Slot { get; set; }

    /// <summary>
    /// Whose verified contact is revealed on mutual acceptance, and only theirs.
    ///
    /// Every current captain may manage the opening; only this one is ever
    /// exposed. If they leave or lose the role, approaches and grants pause
    /// until a replacement is chosen rather than silently exposing somebody
    /// else's identifier.
    /// </summary>
    public Guid? ContactCaptainAccountId { get; set; }

    // Structured vocabulary, not user-authored labels. Postgres arrays, the
    // same shape MatchVeto.MapPool already uses.
    public string[] Roles { get; set; } = [];
    public string[] Languages { get; set; } = [];
    public string[] Regions { get; set; } = [];

    /// <summary>
    /// Weekly availability as <b>UTC hours-of-week</b> (0–167).
    ///
    /// Normalised on write so that "do these two actually overlap?" is an array
    /// intersection the database can answer, rather than a timezone
    /// reconciliation done per row in memory. <see cref="TimeZoneId"/> keeps the
    /// publisher's own zone so their windows can be shown back to them labelled.
    /// </summary>
    public int[] AvailabilityHours { get; set; } = [];
    public string? TimeZoneId { get; set; }

    /// <summary>Plain text, 300 characters, rendered inert — no markup, no clickable links.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Increments on publication, explicit renewal or a material edit. A
    /// decline binds to the cycle, so a new cycle is a genuinely new approach
    /// rather than a way around a refusal.
    /// </summary>
    public int Cycle { get; set; } = 1;

    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Sent once, 48 hours before expiry. Keyed so a retry cannot send twice.</summary>
    public DateTimeOffset? ReminderSentAt { get; set; }

    /// <summary>Filled, withdrawn or the team disbanded. Never auto-renewed.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Taken down by platform staff after a report (decisions 015, 056). See
    /// <see cref="LftListing.ModeratedAt"/> — same rule, same reason.
    /// </summary>
    public DateTimeOffset? ModeratedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A player asking about one opening (decision 056).</summary>
public class OpeningApplication
{
    public Guid Id { get; set; }
    public Guid TeamOpeningId { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>
    /// The opening's cycle at the time of asking. A decline blocks resubmission
    /// within this cycle only.
    /// </summary>
    public int Cycle { get; set; }

    public RecruitmentState State { get; set; } = RecruitmentState.Pending;

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// A captain approaching one listed player <b>on behalf of one real opening</b>
/// (decision 056).
///
/// The opening is required, and that is the point: a context-free team approach
/// is a direct message, which recreates the spam this board exists to replace
/// and hides the vacancy actually being offered.
/// </summary>
public class TeamInterest
{
    public Guid Id { get; set; }
    public Guid TeamOpeningId { get; set; }
    public Guid AccountId { get; set; }
    public Guid LftListingId { get; set; }

    public int Cycle { get; set; }
    public RecruitmentState State { get; set; } = RecruitmentState.Pending;
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// A durable pair-level refusal (decision 056).
///
/// Distinct from a decline, which binds to one advert cycle, and distinct from a
/// report, which is an abuse allegation. Treating an ordinary decline as either
/// of the others would be putting words in somebody's mouth.
/// </summary>
public class RecruitmentBlock
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid TeamId { get; set; }

    /// <summary>Which side asked for it. Suppression applies in both directions regardless.</summary>
    public bool ByPlayer { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Mutually accepted permission to see one another's chosen verified contact
/// (decision 056).
///
/// <b>The platform owns the consent, not the conversation.</b> There is no
/// recruitment chat: this exists so a Discord tag can cross once, deliberately,
/// after both sides agreed — and so the platform can honestly say when its own
/// grant ends.
/// </summary>
public class ContactGrant
{
    public Guid Id { get; set; }
    public Guid TeamOpeningId { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>The designated captain at the moment of acceptance, frozen.</summary>
    public Guid CaptainAccountId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Thirty days, unless converted into a roster invitation. Revocable sooner
    /// by either side.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Somebody reported a public advert (decisions 015, 056).
///
/// <b>Report is not decline and not block.</b> 056 keeps all three separate on
/// purpose: an ordinary decline must never be silently recorded as an abuse
/// allegation, and a block is a private pair-level control that needs no
/// moderator at all. Only this one enters a queue a human reads.
///
/// Two nullable foreign keys with a database CHECK rather than a
/// kind-plus-id pair, for the reason stated at the top of this file: a row whose
/// meaning changes with which column is populated is the shape 056 rejected.
/// Real keys also mean an erased advert takes its reports with it.
/// </summary>
public class AdvertReport
{
    public Guid Id { get; set; }
    public Guid ReporterAccountId { get; set; }

    public Guid? LftListingId { get; set; }
    public Guid? TeamOpeningId { get; set; }

    /// <summary>Optional. Staff read the advert itself; this only says what to look at.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── the decision ─────────────────────────────────────────────────────

    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByAccountId { get; set; }

    /// <summary>True if the advert was taken down, false if the report was dismissed.</summary>
    public bool Removed { get; set; }
}
