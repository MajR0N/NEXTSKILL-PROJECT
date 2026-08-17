using System.Text.Json;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Auth;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;
using NextSkill.Domain.Veto;

namespace NextSkill.Api.Shared.Persistence;

// ── enums ────────────────────────────────────────────────────────────────
// Stored as strings, not ints: readable in psql, and immune to someone
// reordering the enum and silently reinterpreting existing rows.

/// <summary>Both games are first-class (decision 001). CS:GO is out of scope.</summary>
public enum Game { Cs2, Cs16 }

// MatchIntegrity is DEFINED ONCE, in NextSkill.Domain.Matches, and reused here
// (decision 035). Defining it twice would mean a state could be added to one and
// not the other, and the mapping would fall into its default case — which in an
// integrity gate means ADVANCING when it should block.

/// <summary>What an out-of-band code is for (decision 047).</summary>
public enum OneTimeCodePurpose { PhoneVerification, SmsSignIn }

public enum MatchStatus { Scheduled, VetoInProgress, AwaitingPlayers, Live, Completed, Forfeited, Cancelled }

public enum DiscordAnnouncementType { RegistrationOpened, MatchStarted, ResultConfirmed }

// TournamentFormat, TournamentStatus and TeamSize are DEFINED ONCE, in
// NextSkill.Domain.Tournaments, and reused here — the same call already made for
// MatchIntegrity and the role enums. The rules that reason about a status are
// pure functions, and a second copy of the enum is how those rules and the
// database drift apart.

// ── entities ─────────────────────────────────────────────────────────────

/// <summary>An organiser: the tenant boundary (decision 037).</summary>
public class Organiser
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }

    /// <summary>
    /// Trust is a per-organiser attribute, not a global assumption (CONTEXT §3b).
    /// A trusted organiser skips the result-confirmation gate; everyone else gets it.
    /// Pukawka is partner #1, not partner #50.
    /// </summary>
    public bool Trusted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A person. <b>Global, not tenant-scoped</b> — ratings and profiles span
/// organisers deliberately, which is the whole point of a platform rather than
/// isolated installs (decision 036).
/// </summary>
public class Account
{
    public Guid Id { get; set; }
    public required string Email { get; set; }

    /// <summary>Argon2id. Never reversible, never logged (decision 011).</summary>
    public required string PasswordHash { get; set; }

    public required string Nickname { get; set; }

    /// <summary>
    /// Hash of the persistent <c>setinfo</c> token (decision 011). Stored hashed
    /// like a password because we only ever need to <i>verify</i> it — there is
    /// no "reveal token" anywhere in the product, only rotate.
    /// </summary>
    public string? SetinfoTokenHash { get; set; }

    /// <summary>
    /// Platform staff role, or null for ordinary users — which is nearly
    /// everyone (decision 036). Organiser and team roles live in their own
    /// tables, because a person can hold all three independently.
    /// </summary>
    public PlatformRole? PlatformRole { get; set; }

    // ── second factor (decision 047) ─────────────────────────────────────

    /// <summary>
    /// Which second factor this account uses, or null for none.
    ///
    /// Two-factor is <b>opt-in for players and expected for staff</b>: an
    /// account that can cancel a tournament is worth more than an account that
    /// plays in one.
    /// </summary>
    public TwoFactorMethod? TwoFactorMethod { get; set; }

    /// <summary>
    /// Base32 TOTP secret.
    ///
    /// <b>Encrypted at rest</b>, exactly like <see cref="GameServer.HmacSecret"/>
    /// and for the same reason: deriving the current code means replaying the
    /// seed, so it cannot be hashed. Without encryption a database dump would
    /// let an attacker generate valid codes for every account — defeating the
    /// second factor entirely, though not by itself getting past the password
    /// (012, 047).
    /// </summary>
    public string? TwoFactorSecret { get; set; }

    /// <summary>Set when the first correct code proves the app is really set up.</summary>
    public DateTimeOffset? TwoFactorEnabledAt { get; set; }

    /// <summary>E.164, for the SMS factor. Personal data under GDPR (011).</summary>
    public string? PhoneNumber { get; set; }

    public DateTimeOffset? PhoneVerifiedAt { get; set; }

    /// <summary>Steam/Discord links are optional (decision 011).</summary>
    public string? SteamId { get; set; }
    public string? DiscordId { get; set; }

    // ── moderation (decisions 015, 036) ──────────────────────────────────
    //
    // Distinct from DeletedAt, and not a softer version of it. Erasure is the
    // account holder's right and destroys their data; a ban is our decision and
    // deliberately keeps everything, because the record of why someone was
    // removed is the point.

    /// <summary>
    /// Set when platform staff removed this account's access.
    ///
    /// Enforced in <c>Sessions.ValidateAsync</c>, so an existing session dies on
    /// its next request rather than surviving until the cookie expires — the
    /// same reason 011 pairs the signed cookie with a session row at all.
    /// </summary>
    public DateTimeOffset? BannedAt { get; set; }

    /// <summary>Why. Shown to the banned person: a sanction nobody can understand cannot be appealed.</summary>
    public string? BanReason { get; set; }

    public Guid? BannedByAccountId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// An erased account remains as a non-identifying tombstone because ratings,
    /// memberships and future match-participant history refer to its stable id.
    /// Every identifying or authenticating field is destroyed at this point.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>
/// A logged-in session (decision 011).
///
/// The cookie is signed by ASP.NET Data Protection, so the id it carries cannot
/// be forged — but a signed cookie alone is <b>unrevocable</b>: it stays valid
/// until it expires no matter what happens server-side. This row is what makes
/// logout real. Deleting it ends the session immediately, everywhere.
///
/// There is no <c>RevokedAt</c> column on purpose: revocation is the row not
/// existing, which cannot be misread.
/// </summary>
public class Session
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Also what lets expired rows be pruned without reading cookies.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// An outstanding password-reset link (decision 011).
///
/// The token itself is <b>never stored</b> — only its SHA-256 hash. A database
/// leak must not hand an attacker a working reset link for every account that
/// happens to have one open.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>SHA-256 of the emailed token, base64. Unique, and the lookup key.</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    // There is no `UsedAt`. Single use is enforced by DELETING the row, for the
    // same reason a session has no `revoked` flag: a flag is a state every query
    // has to remember to check, and forgetting once means a reset link that
    // works twice.
}

/// <summary>
/// A single-use recovery code (decision 047).
///
/// <b>Without these, enabling two-factor is a way to lose your account.</b> A
/// lost or wiped phone would otherwise be permanent — the exact lockout the
/// password-reset flow exists to prevent, reintroduced one layer up.
///
/// Hashed, single use, and deleted on use for the same reason a reset token is:
/// a flag is a check somebody eventually forgets.
/// </summary>
public class RecoveryCode
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A short-lived numeric code delivered out of band — SMS today, and the same
/// table if anything else is added (decision 047).
/// </summary>
public class OneTimeCode
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public OneTimeCodePurpose Purpose { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>A team. Global, like accounts — rosters cross organisers.</summary>
public class Team
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Tag { get; set; }
    public string? City { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Set when the team is disbanded. <b>Never a row delete</b> (decision 045).
    ///
    /// Matches and ratings reference this team by id. Deleting the row would
    /// leave finished tournaments pointing at nothing — history rewritten by a
    /// captain clicking a button months later. So the team stops being usable
    /// and stays readable, which is the same call
    /// <see cref="TeamMembership.LeftAt"/> makes for people.
    /// </summary>
    public DateTimeOffset? DisbandedAt { get; set; }
}

/// <summary>A registered game server. Tenant-scoped: it belongs to an organiser (decision 007).</summary>
public class GameServer : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }

    public required string Host { get; set; }
    public int Port { get; set; }
    public Game Game { get; set; }

    /// <summary>
    /// Reported by the plugin. The panel refuses to allocate a match to an
    /// incompatible version, because organisers control their own upgrades
    /// and we cannot force one (decision 007).
    /// </summary>
    public string? PluginVersion { get; set; }

    /// <summary>Proven by challenge-response at registration (decision 007).</summary>
    public bool Verified { get; set; }

    /// <summary>
    /// Shared secret for HMAC request signing (decisions 009, 012).
    ///
    /// <b>Encrypted at rest</b> via <see cref="Shared.Auth.SecretProtector"/>,
    /// with a key ASP.NET Data Protection holds outside the database — so a
    /// stolen dump no longer contains a usable signing credential (012).
    /// It cannot be hashed: verifying a plugin's signature means replaying it.
    ///
    /// Shown to the organiser exactly once, at registration, and never again.
    /// </summary>
    public string? HmacSecret { get; set; }

    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A tournament. Tenant-scoped.</summary>
public class Tournament : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }

    public required string Name { get; set; }
    public Game Game { get; set; }
    public TournamentFormat Format { get; set; }
    public TournamentStatus Status { get; set; }

    /// <summary>1v1, 2v2 or 5v5 (decision 045). Decides how many fieldable
    /// players a team needs before it may register.</summary>
    public TeamSize TeamSize { get; set; }

    /// <summary>
    /// Registration cap. An uncapped community cup that attracts three hundred
    /// teams is not a nice problem to have — it is an event nobody can run
    /// (decision 046).
    /// </summary>
    public int MaxTeams { get; set; }

    /// <summary>Free text shown on the tournament page: rules, prize, contact.</summary>
    public string? Description { get; set; }

    /// <summary>Groups tournaments into a season (decision 021).</summary>
    public Guid? SeasonId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>
    /// Monotonic occurrence number for registration-open announcements. A
    /// reopened tournament is a new event, while an HTTP retry is not.
    /// </summary>
    public int RegistrationOpenRevision { get; set; }
}

/// <summary>
/// A tournament's outgoing Discord credential (decision 014). The URL is
/// protected with an application key outside PostgreSQL and is never projected
/// back into a page or API response.
/// </summary>
public class TournamentDiscordWebhook : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid TournamentId { get; set; }
    public string? ProtectedUrl { get; set; }
    public DateTimeOffset ConfiguredAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Durable transactional outbox for Discord. Event facts are stored without
/// the webhook credential, so a database reader cannot post to the channel and
/// a process crash cannot lose an announcement committed with the event.
/// </summary>
public class DiscordAnnouncement : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid TournamentId { get; set; }
    public Guid? MatchId { get; set; }
    public DiscordAnnouncementType Type { get; set; }
    public required string DeduplicationKey { get; set; }
    public required JsonDocument Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? AbandonedAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// A named, organiser-owned snapshot of repeatable tournament setup (decision
/// 015). Cloning copies values into a new draft; the draft never retains a
/// live link to this row, so later template changes cannot rewrite an event.
/// </summary>
public class TournamentTemplate : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }

    public required string Name { get; set; }

    /// <summary>Lowercase name used for case-insensitive uniqueness.</summary>
    public required string NameKey { get; set; }

    public Game Game { get; set; }
    public TournamentFormat Format { get; set; }
    public TeamSize TeamSize { get; set; }
    public int MaxTeams { get; set; }
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A match. Tenant-scoped through its tournament, but carries
/// <c>OrganiserId</c> directly so the query filter is a single column
/// comparison rather than a join.
/// </summary>
public class Match : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid TournamentId { get; set; }

    /// <summary>Game discriminator, in the first migration by design (decision 001).</summary>
    public Game Game { get; set; }

    public MatchStatus Status { get; set; }

    /// <summary>Gate on bracket advancement (decision 035).</summary>
    public MatchIntegrity Integrity { get; set; }

    public Guid? ServerId { get; set; }
    public Guid? TeamAId { get; set; }
    public Guid? TeamBId { get; set; }
    public int? ScoreA { get; set; }
    public int? ScoreB { get; set; }
    public string? Map { get; set; }

    /// <summary>
    /// Who won. <b>Stored, not inferred from the score.</b>
    ///
    /// A forfeit has no score, a bye has no match, and a 16-14 that was later
    /// reversed still has its old numbers on it. Deriving the winner from
    /// ScoreA/ScoreB would be right most of the time, and advancement is not a
    /// place where "most of the time" is good enough — it decides who plays next
    /// (decisions 023, 035).
    /// </summary>
    public Guid? WinnerTeamId { get; set; }

    // ── bracket position and routing (decision 023) ──────────────────────

    /// <summary>Upper, lower, or grand final. Single elimination is all upper.</summary>
    public BracketSide Side { get; set; }

    public int Round { get; set; }
    public int Position { get; set; }

    /// <summary>
    /// Where the winner goes, as a <b>real foreign key</b> to another match.
    ///
    /// The generator works in per-bracket integers; those ids are turned into
    /// Guids before anything is written, so routing becomes a constraint the
    /// database enforces rather than an invariant the application hopes for.
    /// <b>A dangling routing link is now impossible</b> — which is exactly the
    /// bug that shipped and was caught in Phase 2, where upper round 2's losers
    /// routed nowhere and round 1's routed twice.
    ///
    /// Null is legal and meaningful: the final routes its winner nowhere.
    /// </summary>
    public Guid? WinnerToMatchId { get; set; }
    public SlotRef? WinnerToSlot { get; set; }

    /// <summary>Where the loser goes — null in single elimination, and for the final.</summary>
    public Guid? LoserToMatchId { get; set; }
    public SlotRef? LoserToSlot { get; set; }

    /// <summary>
    /// The grand-final reset: generated up front, inactive unless the
    /// lower-bracket team wins the first grand final (decision 022). It exists
    /// from the start so the most commonly forgotten rule in tournament
    /// software cannot be forgotten.
    /// </summary>
    public bool IsConditional { get; set; }

    /// <summary>
    /// A walkover. The occupying team advances immediately and the match is
    /// <b>never rated</b> — a free win must not move a rating (022, 025).
    /// </summary>
    public bool IsBye { get; set; }

    /// <summary>
    /// A slot can be resolved without containing a team: a bye or an event
    /// forfeit propagates a real vacancy. Without this bit, null means both
    /// "not known yet" and "known empty", and double-elimination cannot safely
    /// remove a forfeiting team from every later round.
    /// </summary>
    public bool SlotAResolved { get; set; }
    public bool SlotBResolved { get; set; }

    /// <summary>An event forfeit is a human result with an audit trail.</summary>
    public Guid? ForfeitedTeamId { get; set; }
    public Guid? ForfeitedByAccountId { get; set; }
    public string? ForfeitReason { get; set; }

    /// <summary>
    /// Highest <c>seq</c> accepted for this match. Gaps mark the match
    /// <see cref="MatchIntegrity.Suspect"/>; repeats are idempotent no-ops
    /// (decision 035).
    /// </summary>
    public long LastEventSeq { get; set; }

    /// <summary>
    /// Per-game variable data as <c>jsonb</c>, GIN-indexed (decision 008).
    /// CS2 and CS 1.6 report genuinely different things; this is why Postgres
    /// was chosen over MySQL, where every new query would need a generated
    /// column and a migration.
    /// </summary>
    public JsonDocument? Payload { get; set; }

    // ── the second ending: when the result becomes FINAL (decision 050) ──
    //
    // Receiving a result moves the bracket; only a FINAL result moves a rating.
    // Two nullable timestamps rather than a confirmations table: there are
    // exactly two teams, and a table for a two-valued fact never pays its rent.

    public DateTimeOffset? TeamAConfirmedAt { get; set; }
    public DateTimeOffset? TeamBConfirmedAt { get; set; }

    /// <summary>
    /// A captain's claim that the result is <b>illegitimate</b>.
    ///
    /// <b>Not <see cref="MatchIntegrity.Disputed"/>.</b> Integrity is a machine
    /// statement about whether we received the result correctly; this is a human
    /// statement about whether it should stand. Merging them would let a captain
    /// retroactively flip a machine-verified fact and make reconciliation
    /// non-deterministic on replay (050).
    ///
    /// It holds the rating and <b>never un-advances the bracket</b>: human
    /// judgement gets the reversible thing and never the irreversible one.
    /// </summary>
    public DateTimeOffset? DisputedAt { get; set; }
    public Guid? DisputedByAccountId { get; set; }
    public string? DisputeReason { get; set; }

    /// <summary>
    /// When the rating sweeper settled this match — including settling it as
    /// <i>unrated</i>, which is why this is not derived from the ledger.
    ///
    /// Both this and the ledger's unique index exist deliberately: the index is
    /// the correctness guarantee, this column is what stops the sweeper
    /// rescanning every completed match on the platform forever (050).
    /// </summary>
    public DateTimeOffset? RatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>
/// Privileged decisions that must never be silent
/// (decisions 012 §7, 015, 036, 051 §7).
///
/// <b>Phase 4 records decisions only.</b> Authentication events are deferred
/// with the staff panel that would read them: an append-only log nobody can
/// view is storage rather than security, and a row per login accrues GDPR
/// weight from day one against the IP minimisation that already shipped.
/// </summary>
public enum AuditAction
{
    ForfeitMatch,
    ReverseResult,
    ResolveDispute,
    RegisterServer,
    SetOrganiserTrusted,
    GrantRole,
    BanUser,

    /// <summary>
    /// Someone asked for an RCON credential, a <c>setinfo</c> token or a server
    /// signing secret. It is <b>always</b> denied, to everyone, including the
    /// platform owner (007, 012) — which is exactly why the attempt is the thing
    /// worth recording.
    /// </summary>
    ReadSecret,
}

/// <summary>
/// Whether the decision happened.
///
/// <b>This column is why the audit log has to be a table.</b>
/// [036](036-authorization.md):173 requires that a denied request for a
/// server's credential is audited — and a <i>denied</i> attempt changes no row,
/// so there is nothing for per-row attribution to attach itself to. Forfeits
/// were called "audited" while meaning three columns on the match; that
/// mechanism could never have covered a refusal.
/// </summary>
public enum AuditOutcome { Allowed, Denied }

/// <summary>
/// How long a row is kept (decision 051 §7).
///
/// Present from the first migration purely so authentication events can be
/// swept on a different clock later without a schema change.
/// </summary>
public enum AuditRetention
{
    /// <summary>Competition history. Kept — it explains a bracket years later.</summary>
    Decision,

    /// <summary>Security telemetry. Sweepable once something reads it.</summary>
    Security,
}

/// <summary>
/// One recorded human decision. Append-only (decisions 012 §7, 036, 051 §7).
///
/// <b>Global rather than tenant-filtered</b>, and deliberately: platform-scope
/// actions have no organiser at all, and staff read across organisers by
/// definition. A nullable column cannot carry the standard filter without
/// silently hiding exactly the rows staff exist to see, so reads here are
/// explicit — the same call already made for <see cref="Rating"/>.
///
/// <b>Erasure needs no new thinking.</b> Rows reference an account id, and
/// erasure turns that account into a tombstone. Nothing here is edited and
/// nothing personal survives it, so append-only and GDPR erasure stop
/// competing.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; }

    /// <summary>Who decided. Survives their erasure as a tombstoned id.</summary>
    public Guid ActorAccountId { get; set; }

    /// <summary>Null for platform-scope actions, which belong to no organiser.</summary>
    public Guid? OrganiserId { get; set; }

    public AuditAction Action { get; set; }
    public AuditOutcome Outcome { get; set; }

    /// <summary>
    /// What was acted on. The kind is implied by <see cref="Action"/>, so there
    /// is no second column repeating it back — a <c>ForfeitMatch</c> row's
    /// resource is a match and can be nothing else.
    /// </summary>
    public Guid? ResourceId { get; set; }

    /// <summary>
    /// The human's stated reason. <b>Never the secret involved</b> (012 §7).
    /// </summary>
    public string? Reason { get; set; }

    public AuditRetention Retention { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// How confident we are that this person actually played (decision 050).
/// </summary>
public enum ParticipantProvenance
{
    /// <summary>
    /// A captain's attributable statement, frozen when the match loaded.
    ///
    /// <b>This does not reopen 029.</b> That decision refused to infer an old
    /// match's roster from <i>current</i> team membership, and it was right — a
    /// roster read today says nothing about a roster last March. A declared row
    /// is not an inference; it is a claim made at a known moment and rendered
    /// as one.
    /// </summary>
    Declared,

    /// <summary>Proven in game by both factors of 011's binding.</summary>
    Bound,
}

/// <summary>
/// Who played, and how confidently (decision 050). One row per
/// <c>(match, account)</c>.
///
/// <b>Both provenances count toward a player rating.</b> Rating only what the
/// plugin proved was rejected because 005 gives CS 1.6 no binding guarantee on
/// non-Steam clients (V3, still unverified) — and CS 1.6 is the moat. A
/// bind-only rule would have quietly meant no player ratings in the game the
/// product is differentiated by.
/// </summary>
public class MatchParticipant : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid MatchId { get; set; }
    public Guid AccountId { get; set; }
    public Guid TeamId { get; set; }

    /// <summary>The roster slot they occupied (decision 045). Never a coach.</summary>
    public RosterSlot Slot { get; set; }

    public ParticipantProvenance Provenance { get; set; }

    /// <summary>
    /// Factor 2 of 011's binding: shown to this one player in the lobby, typed
    /// in game, and good for exactly one bind.
    ///
    /// <b>Encrypted at rest</b>, not hashed. Hashing is impossible here — the
    /// player has to be shown their own code — but a database reader has no
    /// business seeing live ones either, and once
    /// <see cref="Shared.Auth.SecretProtector"/> existed for the HMAC and TOTP
    /// secrets there was no reason left to leave this in the clear.
    ///
    /// Its real security remains structural rather than cryptographic: four
    /// characters are useless without the account's <c>setinfo</c> token, the
    /// code is scoped to one slot in one match, and the first successful bind
    /// consumes it — after which <see cref="Provenance"/> itself is the used
    /// marker, so there is no separate consumed flag to forget to set.
    /// </summary>
    public string? LinkCode { get; set; }

    public DateTimeOffset? BoundAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// One immutable rating movement (decision 050 §3). Append-only.
///
/// <b>Global, like <see cref="Rating"/> itself</b> — a rating trend that spans
/// organisers is the whole point of a platform rather than isolated installs
/// (036).
///
/// One table does four jobs, which is the tell that it is the right seam:
/// 049's trend chart reads it directly, 025's recomputation seam replays it,
/// the unique index <b>is</b> the sweeper's idempotency — a database constraint
/// rather than application logic hoping to be right — and <i>"why didn't this
/// match move my rating?"</i> answers itself, which is why unrated rows carry a
/// reason string rather than a bare flag.
/// </summary>
public class RatingChange
{
    public Guid Id { get; set; }

    /// <summary>
    /// <b>Nullable on purpose</b>: 021 promises a soft RD widening at season
    /// start, which is a rating change with no match behind it.
    /// </summary>
    public Guid? MatchId { get; set; }

    /// <summary>A team id or an account id — see <see cref="SubjectIsTeam"/>.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Teams are Glicko-2, players are OpenSkill; they never mix (025).</summary>
    public bool SubjectIsTeam { get; set; }

    public Game Game { get; set; }

    // Null on an unrated row. Nothing moved, so there is nothing to record —
    // writing the unchanged value in both columns would make "no change" and
    // "changed by zero" indistinguishable.
    public double? ValueBefore { get; set; }
    public double? ValueAfter { get; set; }
    public double? DeviationBefore { get; set; }
    public double? DeviationAfter { get; set; }
    public double? VolatilityBefore { get; set; }
    public double? VolatilityAfter { get; set; }

    /// <summary>
    /// Null when the match was rated. Otherwise the reason
    /// <see cref="NextSkill.Domain.Ratings.RatingPolicy.ExcludedBecause"/>
    /// returned, verbatim.
    /// </summary>
    public string? ExcludedBecause { get; set; }

    /// <summary>
    /// What freezes a season archive. 021 freezes standings forever and 025
    /// derives them at read time; 051 §4 resolves the contradiction by filtering
    /// this append-only ledger on <c>AppliedAt &lt;= Season.ClosedAt</c> rather
    /// than snapshotting anything.
    /// </summary>
    public DateTimeOffset AppliedAt { get; set; }

    /// <summary>
    /// Set when a referee reversal invalidated this row (050 §5).
    ///
    /// <b>Superseding rather than deleting is what makes the ledger a ledger.</b>
    /// The old numbers stay readable, so "my rating changed and nobody told me
    /// why" has an answer that survives the correction.
    ///
    /// It is also part of the unique index's filter: idempotency must hold over
    /// the <i>live</i> generation of rows, while a replayed generation has to be
    /// allowed to exist alongside the one it replaces.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; set; }
}

/// <summary>
/// Durable pre-match veto state. One row per match; actions and decided maps
/// grow behind this seam as the veto advances (decisions 028, 051).
/// </summary>
public class MatchVeto : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid MatchId { get; set; }

    public DateTimeOffset? TeamAReadyAt { get; set; }
    public DateTimeOffset? TeamBReadyAt { get; set; }
    public DateTimeOffset? TurnStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int CurrentStep { get; set; }
    public int BestOf { get; set; } = 1;
    public string[] MapPool { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>An append-only captain or expiry action in one veto.</summary>
public class MatchVetoAction : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid MatchVetoId { get; set; }
    public int Sequence { get; set; }
    public Guid TeamId { get; set; }
    public VetoActionKind Kind { get; set; }
    public required string Map { get; set; }
    public VetoStartingSide? Side { get; set; }
    public bool Automatic { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable server-facing map order produced by a completed web veto.</summary>
public class MatchMap : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid MatchId { get; set; }
    public int Order { get; set; }
    public required string Map { get; set; }
    public VetoStartingSide TeamAStartsAs { get; set; }
}

/// <summary>
/// A rating. Global (spans organisers) and <b>one row per subject per game</b> —
/// CS2 and CS 1.6 ratings never mix (decision 025).
/// </summary>
public class Rating
{
    public Guid Id { get; set; }

    /// <summary>Team ratings use Glicko-2; player ratings use OpenSkill (decision 025).</summary>
    public Guid? TeamId { get; set; }
    public Guid? AccountId { get; set; }

    public Game Game { get; set; }

    public double Value { get; set; }

    /// <summary>
    /// Rating deviation — how confident the system is. Shown openly in the UI
    /// as <c>1847 ±32</c>, because hiding uncertainty is what makes a returning
    /// team's rating jump look arbitrary (decision 025).
    /// </summary>
    public double Deviation { get; set; }

    public double Volatility { get; set; }
    public int MatchesPlayed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A team entered into a tournament (decision 046).
///
/// Tenant-scoped like the tournament it belongs to, and carrying
/// <c>OrganiserId</c> directly so the filter stays a single column comparison
/// rather than a join — the same shape <see cref="Match"/> uses.
/// </summary>
public class TournamentRegistration : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid OrganiserId { get; set; }
    public Guid TournamentId { get; set; }
    public Guid TeamId { get; set; }

    /// <summary>
    /// Assigned when registration closes, not when a team signs up. Null until
    /// then, because seeding needs the whole field to exist before it can be
    /// ordered (decision 046).
    /// </summary>
    public int? Seed { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// Captain confirmation after registration closes. Reopening registration
    /// clears it, because the field and schedule are changing again.
    /// </summary>
    public DateTimeOffset? CheckedInAt { get; set; }

    /// <summary>
    /// Exit after the bracket exists. Distinct from pre-start withdrawal: this
    /// one creates a match result and is never rated.
    /// </summary>
    public DateTimeOffset? ForfeitedAt { get; set; }
    public Guid? ForfeitedByAccountId { get; set; }
    public string? ForfeitReason { get; set; }

    /// <summary>
    /// Soft, like every other departure in this schema. A team that withdrew
    /// from a running event is part of that event's history, and the partial
    /// unique index ignores withdrawn rows so a team can re-enter while
    /// registration is still open.
    /// </summary>
    public DateTimeOffset? WithdrawnAt { get; set; }
}
