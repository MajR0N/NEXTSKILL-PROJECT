using System.Globalization;

namespace NextSkill.Api.Localisation;

/// <summary>
/// The four locales NEXTSKILL supports (decision 018). Polish is primary.
///
/// This list is deliberately closed. Adding a locale means adding translations
/// and an <c>hreflang</c> entry, so it is a decision rather than a config tweak.
/// </summary>
public static class Locales
{
    public const string Default = "pl";

    public static readonly string[] Supported = ["pl", "en", "ru", "de"];

    /// <summary>Route-constraint alternation, e.g. <c>pl|en|ru|de</c>.</summary>
    public static string RoutePattern => string.Join('|', Supported);

    public static readonly CultureInfo[] Cultures =
        [.. Supported.Select(c => new CultureInfo(c))];

    /// <summary>Open Graph locale codes — crawlers expect the underscore form.</summary>
    public static string OpenGraph(string locale) => locale switch
    {
        "en" => "en_GB",
        "ru" => "ru_RU",
        "de" => "de_DE",
        _ => "pl_PL",
    };

    public static bool IsSupported(string? locale) =>
        locale is not null && Supported.Contains(locale);
}
