using System.Text.Json;

namespace NextSkill.Contracts;

/// <summary>Event kinds the plugin may send.</summary>
public enum MatchEventType
{
    MatchLoaded,
    PlayerBound,
    PlayerConnected,
    PlayerDisconnected,
    Ready,
    KnifeResult,
    RoundEnd,
    MapEnd,
    SeriesEnd,
    Paused,
    Resumed,
    Forfeit,
}

/// <summary>
/// The envelope every event shares. Uniformity is what makes sequencing and
/// idempotency work the same way for all of them (decision 009).
/// </summary>
public sealed record MatchEvent
{
    public required int Contract { get; init; }
    public required string MatchId { get; init; }

    /// <summary>
    /// Monotonic per match. A <b>gap</b> marks the match <c>suspect</c>; a
    /// <b>repeat</b> is an idempotent no-op (decision 035, layer 2).
    ///
    /// This is what makes the plugin's retry-until-acknowledged behaviour safe:
    /// a retry after a timeout cannot double-count.
    /// </summary>
    public required long Seq { get; init; }

    public required DateTimeOffset SentAt { get; init; }
    public required MatchEventType Type { get; init; }

    /// <summary>Shape is determined by <see cref="Type"/>. Read with <see cref="PayloadAs{T}"/>.</summary>
    public required JsonElement Payload { get; init; }

    /// <summary>
    /// Reads the payload with the shared strict options — so an unknown or
    /// missing field fails here rather than becoming a silent default.
    /// </summary>
    public T PayloadAs<T>() =>
        Payload.Deserialize<T>(NextSkillJson.Options)
        ?? throw new JsonException($"Payload of {Type} deserialised to null.");
}

/// <summary>Round completed. One per round; the running score travels with it.</summary>
public sealed record RoundEndPayload
{
    public required int Round { get; init; }
    public required int ScoreA { get; init; }
    public required int ScoreB { get; init; }
    public required WireSide WinnerSide { get; init; }
}

/// <summary>
/// A map finished — <b>the integrity-critical event</b> (decision 035).
///
/// Carries the <b>complete authoritative final state</b>, not a delta. The panel
/// compares it against the round events it accumulated: a match means
/// <c>confirmed</c>, a mismatch means <c>disputed</c> and the bracket is blocked.
///
/// The plugin retries this until the panel returns 200, holding it on disk in
/// the meantime. Delivery is the plugin's responsibility because game servers
/// sit behind NAT and we can never dial in to them (decision 009).
/// </summary>
public sealed record MapEndPayload
{
    public required string Map { get; init; }

    /// <summary>Zero-indexed position within the series.</summary>
    public required int MapNumber { get; init; }

    public required int FinalScoreA { get; init; }
    public required int FinalScoreB { get; init; }
    public required int RoundsPlayed { get; init; }

    /// <summary>Per-player stats, keyed by roster slot.</summary>
    public required IReadOnlyList<PlayerStats> Players { get; init; }

    /// <summary>
    /// Always true on this event. Present so the panel can assert it explicitly
    /// rather than inferring authority from the event name.
    /// </summary>
    public required bool Authoritative { get; init; }
}

public sealed record PlayerStats
{
    public required string Slot { get; init; }
    public required string AccountId { get; init; }
    public required int Kills { get; init; }
    public required int Deaths { get; init; }
    public required int Assists { get; init; }
}

/// <summary>
/// A player proved who they are. Requires <b>both</b> factors: the persistent
/// <c>setinfo</c> token and the per-match link code (decision 011).
///
/// Neither alone is enough — a code can be handed to a ringer, and a token leaks
/// to every server the player joins.
/// </summary>
public sealed record PlayerBoundPayload
{
    public required string Slot { get; init; }
    public required string AccountId { get; init; }

    /// <summary>
    /// Whatever the game reported. Treated as an <b>opaque session label with no
    /// inherent trust</b> — on CS 1.6 it is spoofable (decision 010).
    /// </summary>
    public required string InGameId { get; init; }
}

/// <summary>Series finished. Sent after the last <see cref="MapEndPayload"/>.</summary>
public sealed record SeriesEndPayload
{
    public required int MapsWonA { get; init; }
    public required int MapsWonB { get; init; }
    public required string WinnerTeamId { get; init; }
}

/// <summary>
/// Liveness. Also how the panel learns the plugin version, so it can refuse to
/// allocate a match to an incompatible server — organisers control their own
/// upgrades and we cannot force one (decision 007).
/// </summary>
public sealed record HeartbeatRequest
{
    public required int Contract { get; init; }
    public required string ServerId { get; init; }
    public required string PluginVersion { get; init; }
    public required WireGame Game { get; init; }
    public string? CurrentMatchId { get; init; }
}
