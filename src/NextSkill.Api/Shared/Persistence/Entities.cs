using System.Net;
using System.Text.Json;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Matches;

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

public enum MatchStatus { Scheduled, VetoInProgress, AwaitingPlayers, Live, Completed, Forfeited, Cancelled }

public enum TournamentFormat { SingleElimination, DoubleElimination, RoundRobin, GroupStage, League }

public enum TournamentStatus { Draft, RegistrationOpen, RegistrationClosed, Running, Completed, Cancelled }

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
