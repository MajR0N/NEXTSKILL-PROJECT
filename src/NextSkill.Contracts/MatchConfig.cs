using System.Text.Json.Serialization;

namespace NextSkill.Contracts;

/// <summary>Wire protocol version. Bumped only on a breaking change.</summary>
public static class WireContract
{
    public const int Version = 1;

    /// <summary>Header carrying the version on every request (decision 009).</summary>
    public const string VersionHeader = "X-NextSkill-Contract";

    /// <summary>Header carrying the HMAC signature over the raw body (decision 012).</summary>
    public const string SignatureHeader = "X-NextSkill-Signature";

    /// <summary>Header carrying the request timestamp, used for the replay window.</summary>
    public const string TimestampHeader = "X-NextSkill-Timestamp";
}

public enum WireGame { Cs2, Cs16 }

public enum WireSide { Ct, T }

/// <summary>
/// What the plugin pulls before a match: everything already decided.
///
/// Note what is <b>absent</b> — there is no map pool and no veto rules, because
/// veto happens on the web before the server is contacted (decision 028). The
/// plugin receives a decided map order and decided starting sides, and never
/// implements a veto state machine in either game.
/// </summary>
public sealed record MatchConfig
{
    [JsonPropertyName("contract")]
    public required int Contract { get; init; }

    public required string MatchId { get; init; }
    public required WireGame Game { get; init; }

    /// <summary>1, 3 or 5. Length of <see cref="Maps"/> must match.</summary>
    public required int BestOf { get; init; }

    /// <summary>Decided by the web veto, in play order (decision 028).</summary>
    public required IReadOnlyList<MapAssignment> Maps { get; init; }

    public required TeamConfig TeamA { get; init; }
    public required TeamConfig TeamB { get; init; }

    /// <summary>
    /// Optional per-tournament setting, default off. Sides come from the veto,
    /// so a knife round is a ritual rather than a requirement (decision 028).
    /// </summary>
    public bool KnifeRound { get; init; }

    /// <summary>Where the plugin sends events. Configured, not guessed.</summary>
    public required string EventsUrl { get; init; }

    /// <summary>CS2 only — CS 1.6 has no server-side demo pipeline (decision 001).</summary>
    public string? DemoUploadUrl { get; init; }
}

/// <summary>One map, with the side each team starts on.</summary>
public sealed record MapAssignment
{
    public required string Map { get; init; }

    /// <summary>Which side team A starts on. Decided by the veto, not by a knife round.</summary>
    public required WireSide TeamAStartsAs { get; init; }
}

public sealed record TeamConfig
{
    public required string TeamId { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// The locked roster. The plugin enforces this as an <b>allowlist</b>:
    /// anyone not on it is held in spectator, and it fails closed — nobody plays
    /// until both identity factors verify (decision 011).
    /// </summary>
    public required IReadOnlyList<RosterEntry> Roster { get; init; }
}

public sealed record RosterEntry
{
    public required string AccountId { get; init; }

    /// <summary>Forced in-game, so scoreboards and demos cannot mislead.</summary>
    public required string Nickname { get; init; }

    /// <summary>
    /// Slot label, e.g. <c>a1</c>. The per-match link code is scoped to a slot,
    /// so a stolen code can only impersonate the slot it was issued for
    /// (decision 011).
    /// </summary>
    public required string Slot { get; init; }

    /// <summary>
    /// CS2 only. Valve authenticates it, so binding is automatic there. On
    /// CS 1.6 it is absent and meaningless — SteamIDs are spoofable on
    /// non-Steam servers, which is the entire reason for the two-factor
    /// binding (decision 010).
    /// </summary>
    public string? SteamId { get; init; }
}
