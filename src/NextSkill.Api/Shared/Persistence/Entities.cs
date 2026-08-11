using System.Net;
using System.Text.Json;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Auth;
using NextSkill.Domain.Brackets;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Tournaments;

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
    /// ponytail: plaintext, exactly like <see cref="GameServer.HmacSecret"/>.
    /// Ceiling: a database dump lets an attacker generate valid codes for every
    /// account — it defeats the second factor entirely, though it does not by
    /// itself get past the password. Upgrade path: encrypt at rest with a key
    /// held OUTSIDE the database, before this goes near production (012).
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

    /// <summary>
    /// Native <c>inet</c>. GDPR: personal data, used for abuse detection only,
    /// never displayed (decision 012).
    /// </summary>
    public IPAddress? LastSeenIp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
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
    /// ponytail: stored in plaintext for local development only. Ceiling: a
    /// database dump currently exposes every server's signing secret. Upgrade
    /// path: encrypt at rest with a key held OUTSIDE the database, before this
    /// ever touches production — 012 requires it, and the column is here now so
    /// the migration cost is paid once.
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

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
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
    /// Soft, like every other departure in this schema. A team that withdrew
    /// from a running event is part of that event's history, and the partial
    /// unique index ignores withdrawn rows so a team can re-enter while
    /// registration is still open.
    /// </summary>
    public DateTimeOffset? WithdrawnAt { get; set; }
}
