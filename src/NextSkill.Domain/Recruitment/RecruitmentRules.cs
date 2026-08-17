namespace NextSkill.Domain.Recruitment;

/// <summary>
/// The recruitment vocabulary and its limits (decision 056).
///
/// <b>Pure, and kept away from the database on purpose.</b> These are the rules
/// people argue about — how many approaches is too many, what counts as a
/// material change, whether a note edit should buy fresh prominence — so they
/// live in one small file that can be exhausted by a test rather than being
/// scattered through handlers.
/// </summary>
public static class RecruitmentRules
{
    // ── vocabulary ───────────────────────────────────────────────────────
    //
    // Product vocabulary rather than user-authored labels, because the whole
    // advantage over a Discord advert is that these mean the same thing to
    // everybody. `coach` is NOT here: it is a roster slot, and 045 says so.

    public static readonly string[] Roles =
        ["igl", "awp", "rifler", "entry", "support", "lurker", "flexible"];

    public static readonly string[] Slots = ["player", "substitute", "coach"];

    /// <summary>
    /// A platform-managed allow-list rather than a compiled country enum, so
    /// Brazil or CIS can be added without a migration. Latency, not nationality.
    /// </summary>
    public static readonly string[] Regions = ["eu-west", "eu-central", "eu-east", "na", "sa", "other"];

    public static readonly string[] Languages = ["pl", "en", "ru", "de", "cs", "uk"];

    // ── limits ───────────────────────────────────────────────────────────

    public const int MaxRoles = 2;
    public const int MaxNoteLength = 300;

    /// <summary>
    /// Ten pending approaches per side. Without a cap, `TeamInterest` reopens
    /// the two-hundred-invite hole 045 deliberately closed — an invite costs a
    /// roster place and an approach does not, which is exactly why the approach
    /// needs its own limit (040, 051 §5).
    /// </summary>
    public const int MaxPendingPerSide = 10;

    public static readonly TimeSpan AdvertLifetime = TimeSpan.FromDays(14);
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(48);
    public static readonly TimeSpan ContactGrantLifetime = TimeSpan.FromDays(30);

    /// <summary>How long an unsuccessful closed request stays privately visible.</summary>
    public static readonly TimeSpan ClosedRequestRetention = TimeSpan.FromDays(14);

    // ── validation ───────────────────────────────────────────────────────

    /// <summary>
    /// Why a publication is invalid, or null when it is fine.
    ///
    /// Returns a reason rather than a bool for the same reason
    /// <c>RatingPolicy.ExcludedBecause</c> does: somebody has to be told what to
    /// fix.
    /// </summary>
    public static string? RejectPublication(
        IReadOnlyList<string> roles,
        IReadOnlyList<string> slots,
        IReadOnlyList<string> languages,
        IReadOnlyList<string> regions,
        IReadOnlyList<int> availabilityHours,
        string? note)
    {
        if (roles.Count > MaxRoles)
            return $"Wybierz najwyżej {MaxRoles} role.";

        if (roles.Any(r => !Roles.Contains(r)))
            return "Nieznana rola.";

        if (slots.Count == 0)
            return "Wybierz przynajmniej jedno miejsce w składzie.";

        if (slots.Any(s => !Slots.Contains(s)))
            return "Nieznane miejsce w składzie.";

        if (languages.Any(l => !Languages.Contains(l)))
            return "Nieznany język.";

        if (regions.Any(r => !Regions.Contains(r)))
            return "Nieznany region.";

        // 168 hours in a week. An out-of-range value means the caller built the
        // array wrong, and silently clamping it would hide that.
        if (availabilityHours.Any(h => h is < 0 or > 167))
            return "Nieprawidłowa dostępność.";

        if (note is { Length: > MaxNoteLength })
            return $"Notatka może mieć najwyżej {MaxNoteLength} znaków.";

        return null;
    }

    /// <summary>
    /// Whether a change starts a NEW advert cycle.
    ///
    /// <b>The note is deliberately excluded.</b> If editing a note bumped the
    /// advert, every serious advertiser would learn to tweak a full stop each
    /// morning, and freshness ordering would stop meaning anything — which is
    /// the same dishonesty as automatic renewal, arrived at from a different
    /// direction (056).
    /// </summary>
    public static bool IsMaterialChange(
        IReadOnlyList<string> oldRoles, IReadOnlyList<string> newRoles,
        IReadOnlyList<string> oldSlots, IReadOnlyList<string> newSlots,
        IReadOnlyList<string> oldLanguages, IReadOnlyList<string> newLanguages,
        IReadOnlyList<string> oldRegions, IReadOnlyList<string> newRegions,
        IReadOnlyList<int> oldHours, IReadOnlyList<int> newHours) =>
        !Same(oldRoles, newRoles) ||
        !Same(oldSlots, newSlots) ||
        !Same(oldLanguages, newLanguages) ||
        !Same(oldRegions, newRegions) ||
        !Same(oldHours.Select(h => h.ToString()).ToArray(), newHours.Select(h => h.ToString()).ToArray());

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x));

    /// <summary>
    /// Converts a weekly window in the publisher's zone into UTC hours-of-week.
    ///
    /// Normalising on write is what lets "do these two genuinely overlap?" be an
    /// array intersection the database answers, instead of a timezone
    /// reconciliation performed per row in memory.
    ///
    /// ponytail: uses a fixed reference week, so a window spanning a daylight
    /// saving change is off by an hour for part of the year. Ceiling: exactly
    /// that one hour, on two weekends a year, in a discovery filter. Upgrade
    /// path: store the local window and resolve per viewer — worth it only if
    /// somebody actually reports it.
    /// </summary>
    public static int[] ToUtcHours(int weekday, int startHour, int endHour, TimeZoneInfo zone)
    {
        if (weekday is < 0 or > 6 || startHour is < 0 or > 23 || endHour is < 1 or > 24 || endHour <= startHour)
            return [];

        // A Monday, chosen as the reference week's day 0.
        var reference = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var hours = new List<int>();
        for (var hour = startHour; hour < endHour; hour++)
        {
            var local = reference.AddDays(weekday).AddHours(hour);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            var index = ((int)utc.DayOfWeek + 6) % 7 * 24 + utc.Hour;   // Monday = 0
            hours.Add(index);
        }

        return [.. hours.Distinct().Order()];
    }
}
