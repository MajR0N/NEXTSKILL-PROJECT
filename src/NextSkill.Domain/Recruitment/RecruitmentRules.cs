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

    /// <summary>
    /// Time zones an advert may be published in, and a viewer may filter in.
    ///
    /// A platform-managed allow-list for the same reason <see cref="Regions"/>
    /// is one: the full IANA database in a select is a worse answer than five
    /// entries covering the scene, and adding a sixth is one line rather than a
    /// migration. It also means an unknown id is refused rather than resolved.
    /// </summary>
    public static readonly string[] TimeZones =
        ["Europe/Warsaw", "Europe/London", "Europe/Kyiv", "Europe/Moscow", "UTC"];

    /// <summary>The allow-listed zone, or null. Never resolves an id we did not offer.</summary>
    public static TimeZoneInfo? Zone(string? id) =>
        id is not null && TimeZones.Contains(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone
            : null;

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

    /// <summary>One weekly window in the publisher's local zone. <c>ToHour</c> is exclusive.</summary>
    public readonly record struct Window(int Day, int FromHour, int ToHour)
    {
        public bool IsValid => Day is >= 0 and <= 6
            && FromHour is >= 0 and <= 23
            && ToHour is >= 1 and <= 24
            && ToHour > FromHour;
    }

    /// <summary>
    /// The Monday 00:00 UTC of the week containing <paramref name="at"/>.
    ///
    /// <b>Every conversion is anchored to a real week, not to a fixed one.</b>
    /// The first version pinned January 2024, so every window was computed at
    /// winter offsets — self-consistent within one zone and quietly wrong
    /// between two zones whose daylight-saving rules differ. Moscow has been
    /// UTC+3 with no DST since 2014, so all summer a Moscow filter missed a
    /// Warsaw advert by exactly one hour, and no same-zone check could see it.
    /// </summary>
    public static DateTime WeekOf(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        var monday = utc.Date.AddDays(-(((int)utc.DayOfWeek + 6) % 7));
        return DateTime.SpecifyKind(monday, DateTimeKind.Utc);
    }

    /// <summary>
    /// Weekly windows in a publisher's zone, as UTC hours-of-week.
    ///
    /// Normalising on write is what lets "do these two genuinely overlap?" be an
    /// array intersection the database answers, instead of a timezone
    /// reconciliation performed per row in memory.
    ///
    /// ponytail: an advert stores the hours computed in the week it was
    /// published, so one that straddles a daylight-saving switch is an hour out
    /// for the rest of its life. Ceiling: at most 14 days, twice a year, in a
    /// discovery filter. Upgrade path: renormalise live adverts from the
    /// lifecycle worker that already sweeps them.
    /// </summary>
    public static int[] ToUtcHours(
        IReadOnlyList<Window> windows, TimeZoneInfo zone, DateTimeOffset reference)
    {
        var monday = WeekOf(reference);
        var hours = new List<int>();

        foreach (var window in windows)
        {
            if (!window.IsValid) continue;

            for (var hour = window.FromHour; hour < window.ToHour; hour++)
            {
                // The local wall clock in the reference week, so the offset used
                // is the one genuinely in force then. Normalised to the LOCAL
                // Monday rather than assuming the UTC Monday falls on a local
                // Monday — true for every zone currently offered, and a silent
                // day-shift for the first one added west of Greenwich.
                var local = LocalMonday(monday, zone).AddDays(window.Day).AddHours(hour);

                // Spring forward deletes an hour of local time. Converting it
                // throws, and inventing a nearby hour would silently move
                // somebody's evening — so the hour that did not exist is simply
                // not available.
                if (zone.IsInvalidTime(local)) continue;

                var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
                hours.Add(((int)utc.DayOfWeek + 6) % 7 * 24 + utc.Hour);   // Monday = 0
            }
        }

        return [.. hours.Distinct().Order()];
    }

    private static DateTime LocalMonday(DateTime utcMonday, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcMonday, zone);
        return local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
    }

    /// <summary>
    /// Reads stored hours back as local windows, <b>as maximal contiguous runs</b>.
    ///
    /// It decomposes rather than summarises, and that is the point: an earlier
    /// version returned one set of days with a single min/max hour range, which
    /// is only truthful when the stored set happens to be a rectangle. It always
    /// was — because the publish form could not produce anything else — so the
    /// guarantee lived in a form rather than in this function, and the day a
    /// second window arrived the board would have displayed a schedule nobody
    /// set. Now any set reads back exactly.
    /// </summary>
    public static IReadOnlyList<Window> FromUtcHours(
        IReadOnlyList<int> utcHours, TimeZoneInfo zone, DateTimeOffset reference)
    {
        if (utcHours.Count == 0) return [];

        var monday = WeekOf(reference);

        var local = utcHours
            .Where(h => h is >= 0 and <= 167)
            .Select(h =>
            {
                var at = TimeZoneInfo.ConvertTimeFromUtc(monday.AddHours(h), zone);
                return ((int)at.DayOfWeek + 6) % 7 * 24 + at.Hour;   // Monday = 0
            })
            .Distinct()
            .Order()
            .ToList();

        var windows = new List<Window>();
        var runStart = 0;

        for (var i = 0; i < local.Count; i++)
        {
            var endsRun = i == local.Count - 1
                || local[i + 1] != local[i] + 1
                // A run never crosses local midnight: two windows on two days
                // are two windows, whatever the arithmetic says.
                || local[i + 1] / 24 != local[i] / 24;

            if (!endsRun) continue;

            windows.Add(new Window(
                local[runStart] / 24,
                local[runStart] % 24,
                local[i] % 24 + 1));

            runStart = i + 1;
        }

        return windows;
    }
}
