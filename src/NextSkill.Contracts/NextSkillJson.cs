using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextSkill.Contracts;

/// <summary>
/// The single JSON configuration used by <b>both</b> the panel and the game
/// server plugin.
///
/// Sharing these options is the point: if the two sides serialised differently,
/// a payload could be written one way and read another, and the mismatch would
/// surface as a wrong score rather than an error. Same library, same options,
/// same behaviour.
/// </summary>
public static class NextSkillJson
{
    /// <summary>
    /// Strict by design (decision 035, layer 1).
    ///
    /// <b>Unknown members are rejected, never ignored.</b> The tempting default
    /// is to skip fields you don't recognise — but that turns a renamed field
    /// into silence instead of an error, and silence is exactly how a wrong
    /// score reaches a bracket.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("Payload deserialised to null.");
}
