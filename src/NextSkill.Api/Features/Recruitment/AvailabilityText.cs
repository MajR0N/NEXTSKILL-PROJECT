using NextSkill.Domain.Recruitment;

namespace NextSkill.Api.Features.Recruitment;

/// <summary>
/// Weekly availability, written out for a reader (decision 056).
///
/// <b>In the publisher's own zone, labelled.</b> 056 is explicit that a viewer
/// who has not supplied a zone is shown the publisher's rather than a guess —
/// and nothing on an account stores a zone yet, so that is every viewer today.
/// A silently mis-shifted schedule is worse than a labelled one.
/// </summary>
public static class AvailabilityText
{
    /// <summary>Monday first, matching the hour-of-week index the database stores.</summary>
    public static readonly string[] Weekdays = ["pon", "wt", "śr", "czw", "pt", "sob", "ndz"];

    /// <summary>
    /// Null when the advert carries no window — which stays allowed.
    /// </summary>
    /// <param name="reference">
    /// The instant the hours were computed against, which is the advert's own
    /// publication time. Reading them back against any other week would shift
    /// the display across a daylight-saving boundary.
    /// </param>
    public static string? Describe(
        IReadOnlyList<int> utcHours, string? timeZoneId, DateTimeOffset reference)
    {
        if (utcHours.Count == 0 || RecruitmentRules.Zone(timeZoneId) is not { } zone) return null;

        var windows = RecruitmentRules.FromUtcHours(utcHours, zone, reference);
        if (windows.Count == 0) return null;

        // Days sharing identical hours are grouped, because "pon, wt, czw
        // 18:00–23:00" is what somebody meant and three separate lines is what
        // the storage happens to look like.
        var grouped = windows
            .GroupBy(w => (w.FromHour, w.ToHour))
            .OrderBy(g => g.Key.FromHour)
            .Select(g =>
            {
                var days = string.Join(", ", g.Select(w => w.Day)
                    .Where(d => d is >= 0 and <= 6)
                    .Distinct()
                    .Order()
                    .Select(d => Weekdays[d]));
                return $"{days} {g.Key.FromHour:D2}:00–{g.Key.ToHour:D2}:00";
            });

        return $"{string.Join(" · ", grouped)} ({timeZoneId})";
    }
}
