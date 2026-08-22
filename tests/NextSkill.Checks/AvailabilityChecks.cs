using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Recruitment;

namespace NextSkill.Checks;

/// <summary>
/// Weekly availability, captured and compared (decision 056).
///
/// The columns and the UTC hour-of-week normaliser shipped with the board with
/// nothing writing them and nothing reading them. Three of the checks here
/// exist because the first working version was quietly wrong in ways no
/// same-zone test could see: it pinned a January reference week, it read any
/// stored set back as if it were a rectangle, and it converted through the
/// spring-forward hour that does not exist.
/// </summary>
public static class AvailabilityChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        var warsaw = RecruitmentRules.Zone("Europe/Warsaw")!;
        var moscow = RecruitmentRules.Zone("Europe/Moscow")!;

        var winter = new DateTimeOffset(2026, 1, 14, 12, 0, 0, TimeSpan.Zero);
        var summer = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        // ── the pure conversions ─────────────────────────────────────────

        check("availability: windows union across days, and an impossible one is ignored", () =>
        {
            var hours = RecruitmentRules.ToUtcHours(
                [new(0, 18, 20), new(2, 18, 20)], warsaw, winter);

            that(hours.Length == 4, $"two days of a two-hour window is four hours, got {hours.Length}");
            that(hours.SequenceEqual(hours.Order()), "stored ordered, so equality is comparable");

            that(RecruitmentRules.ToUtcHours([new(0, 20, 18)], warsaw, winter).Length == 0,
                "a window that ends before it starts is not a window");
            that(RecruitmentRules.ToUtcHours([], warsaw, winter).Length == 0,
                "no windows is no availability, not every hour");
            that(RecruitmentRules.Zone("Mars/Olympus") is null,
                "an unknown zone is refused rather than resolved — the list is platform-managed like regions");
        });

        // ── THE ONE NO SAME-ZONE CHECK COULD SEE ─────────────────────────
        check("availability: DAYLIGHT SAVING — two zones with different rules agree in BOTH seasons", () =>
        {
            // Warsaw observes DST; Moscow has been UTC+3 with none since 2014.
            // In January the evening is two hours apart, in July only one — so a
            // fixed reference week is wrong for whichever season it is not.
            that(RecruitmentRules.ToUtcHours([new(1, 18, 19)], warsaw, winter)
                    .SequenceEqual(RecruitmentRules.ToUtcHours([new(1, 20, 21)], moscow, winter)),
                "in January, 18:00 in Warsaw IS 20:00 in Moscow");

            that(RecruitmentRules.ToUtcHours([new(1, 18, 19)], warsaw, summer)
                    .SequenceEqual(RecruitmentRules.ToUtcHours([new(1, 19, 20)], moscow, summer)),
                "in July it is 19:00 — and pinning a winter reference made every summer " +
                "cross-zone comparison an hour out, invisibly, because both sides were wrong together");

            // The proof that the reference is doing work rather than cancelling.
            that(!RecruitmentRules.ToUtcHours([new(1, 18, 19)], warsaw, winter)
                    .SequenceEqual(RecruitmentRules.ToUtcHours([new(1, 18, 19)], warsaw, summer)),
                "the same local evening lands on a different UTC hour in the two seasons");
        });

        check("availability: the hour that does not exist is skipped, not invented", () =>
        {
            // Europe/Warsaw springs forward on the last Sunday of March: local
            // 02:00–03:00 never happens. Converting it throws, and nudging it to
            // a neighbouring hour would silently move somebody's morning.
            // The EU switches on the LAST Sunday of March: 2026-03-29. A
            // reference inside that week, so day 6 is the switchover itself —
            // the first version of this check pointed at 2026-03-30, a Monday,
            // and therefore at an ordinary Sunday a week later.
            var switchover = new DateTimeOffset(2026, 3, 25, 12, 0, 0, TimeSpan.Zero);
            var hours = RecruitmentRules.ToUtcHours([new(6, 1, 5)], warsaw, switchover);

            that(hours.Length == 3,
                $"01:00–05:00 across the spring-forward Sunday is three real hours, got {hours.Length}");

            // And an ordinary week keeps all four, so the check above is
            // measuring the switch rather than an off-by-one somewhere.
            that(RecruitmentRules.ToUtcHours([new(6, 1, 5)], warsaw, winter).Length == 4,
                "an ordinary Sunday has every hour it should");
        });

        check("availability: stored hours read back EXACTLY, including a shape no form can produce", () =>
        {
            // The earlier inverse collapsed everything to one set of days and a
            // single min/max range. That is only truthful for a rectangle — and
            // it always was one, because the form could not make anything else.
            // The guarantee lived in a form rather than in the function.
            var ragged = new RecruitmentRules.Window[]
            {
                new(0, 10, 12),   // Monday morning
                new(0, 18, 20),   // and Monday evening — two windows, one day
                new(2, 19, 23),
                new(5, 9, 12),
            };

            var stored = RecruitmentRules.ToUtcHours(ragged, warsaw, winter);
            var read = RecruitmentRules.FromUtcHours(stored, warsaw, winter);

            that(read.Count == 4,
                $"four separate windows must come back as four, not one range spanning them; got {read.Count}");
            that(ragged.All(read.Contains),
                "and each one exactly: " + string.Join(", ", read.Select(w => $"{w.Day}:{w.FromHour}-{w.ToHour}")));

            // A Monday-morning window wraps into the previous UTC day.
            var lateNight = RecruitmentRules.ToUtcHours([new(0, 0, 2)], warsaw, winter);
            var back = RecruitmentRules.FromUtcHours(lateNight, warsaw, winter);
            that(back.Count == 1 && back[0] == new RecruitmentRules.Window(0, 0, 2),
                "a Monday-morning window wraps to Sunday in UTC and must come back as Monday");
        });

        // ── and the same thing through the real forms ────────────────────

        using var factory = TestHost.Create();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(null));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        const string password = "a-long-enough-passphrase";

        var eveningNick = $"AvEv{stamp}";
        var anytimeNick = $"AvAn{stamp}";

        HttpClient SignedInAs(string nickname)
        {
            var http = factory.NewClient();
            http.PostFormAsync("/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            return http;
        }

        using var evening = SignedInAs(eveningNick);
        using var anytime = SignedInAs(anytimeNick);

        Guid eveningId, anytimeId;
        using (var db = Db())
        {
            eveningId = db.Accounts.Single(a => a.Nickname == eveningNick).Id;
            anytimeId = db.Accounts.Single(a => a.Nickname == anytimeNick).Id;

            // Publishing needs something shareable after mutual acceptance.
            foreach (var id in new[] { eveningId, anytimeId })
                db.Accounts.Single(a => a.Id == id).DiscordId = $"{id.GetHashCode() & 0x7fffffff}";
            db.SaveChanges();
        }

        check("availability: the form captures a DIFFERENT window per day", () =>
        {
            // Tuesday evening AND Saturday morning — the shape one window for
            // the whole week could not hold, which is why the row is per day.
            var fromHours = new[] { 0, 18, 0, 0, 0, 9, 0 };
            var toHours = new[] { 0, 23, 0, 0, 0, 12, 0 };

            var content = new List<KeyValuePair<string, string>>
            {
                new("Listing.Game", "Cs2"),
                new("Listing.Slots", "player"),
                new("Listing.Roles", "awp"),
                new("Listing.TimeZoneId", "Europe/Warsaw"),
                new("Listing.Note", $"Wieczory {stamp}"),
            };

            for (var day = 0; day < 7; day++)
            {
                content.Add(new("Listing.FromHours", fromHours[day].ToString()));
                content.Add(new("Listing.ToHours", toHours[day].ToString()));
            }

            var form = evening.GetStringAsync("/pl/me/recruitment").GetAwaiter().GetResult();
            content.Add(new("__RequestVerificationToken", TestHost.AntiforgeryToken(form)));

            var response = evening.PostAsync(
                "/pl/me/recruitment?handler=PublishListing",
                new FormUrlEncodedContent(content)).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"publishing should redirect, got {(int)response.StatusCode}");

            using var db = Db();
            var listing = db.LftListings.AsNoTracking().Single(l => l.AccountId == eveningId);

            that(listing.TimeZoneId == "Europe/Warsaw",
                "the publisher's zone is kept, because the board labels the window in it");
            that(listing.AvailabilityHours.Length == 8,
                $"five evening hours plus three morning ones, got {listing.AvailabilityHours.Length}");

            var read = RecruitmentRules.FromUtcHours(listing.AvailabilityHours, warsaw, listing.PublishedAt);
            that(read.Count == 2 &&
                 read.Contains(new RecruitmentRules.Window(1, 18, 23)) &&
                 read.Contains(new RecruitmentRules.Window(5, 9, 12)),
                "and both windows survive the round trip through the database");
        });

        check("availability: an advert without a window still publishes and is still listed", () =>
        {
            var response = anytime.PostFormAsync("/pl/me/recruitment?handler=PublishListing", new()
            {
                ["Listing.Game"] = "Cs2",
                ["Listing.Slots"] = "player",
                ["Listing.Note"] = $"Kiedykolwiek {stamp}",
            }, "/pl/me/recruitment").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"schedule NARROWS the board, it is not an entry requirement (056); got {(int)response.StatusCode}");

            using var db = Db();
            that(db.LftListings.AsNoTracking().Single(l => l.AccountId == anytimeId).AvailabilityHours.Length == 0,
                "and nothing is invented on their behalf");

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(
                spectator.GetStringAsync("/pl/lft?view=Players&game=Cs2").GetAwaiter().GetResult());
            that(html.Contains(anytimeNick, StringComparison.Ordinal),
                "an advert with no window must still be found when nobody is filtering by one");
        });

        check("availability: the overlap filter narrows to adverts that genuinely overlap", () =>
        {
            using var spectator = factory.NewClient();

            string Board(string query) => WebUtility.HtmlDecode(
                spectator.GetStringAsync($"/pl/lft?view=Players&game=Cs2&{query}").GetAwaiter().GetResult());

            var inside = Board("day=1&from=20&to=21&tz=Europe/Warsaw");
            that(inside.Contains(eveningNick, StringComparison.Ordinal),
                "20:00 on Tuesday falls inside an 18:00–23:00 Tuesday window");
            that(!inside.Contains(anytimeNick, StringComparison.Ordinal),
                "and an advert with NO window cannot be claimed to overlap one");

            that(!Board("day=1&from=8&to=9&tz=Europe/Warsaw").Contains(eveningNick, StringComparison.Ordinal),
                "08:00 does not overlap an evening, and a filter that returns everybody filters nothing");
            that(!Board("day=2&from=20&to=21&tz=Europe/Warsaw").Contains(eveningNick, StringComparison.Ordinal),
                "Wednesday is not Tuesday");
            that(Board("day=5&from=10&to=11&tz=Europe/Warsaw").Contains(eveningNick, StringComparison.Ordinal),
                "and the SECOND window is real — a Saturday morning is not an evening");
        });

        check("availability: a filter written in ANOTHER zone still finds the right adverts", () =>
        {
            using var spectator = factory.NewClient();

            string Board(string query) => WebUtility.HtmlDecode(
                spectator.GetStringAsync($"/pl/lft?view=Players&game=Cs2&{query}").GetAwaiter().GetResult());

            // London is an hour behind Warsaw in both seasons, so this check is
            // stable whenever in the year it runs.
            that(Board("day=1&from=17&to=18&tz=Europe/London").Contains(eveningNick, StringComparison.Ordinal),
                "17:00 in London IS 18:00 in Warsaw — the same hour, so it must overlap");
            that(!Board("day=1&from=23&to=24&tz=Europe/London").Contains(eveningNick, StringComparison.Ordinal),
                "and 23:00 in London is midnight in Warsaw, which is past the window");
        });

        check("availability: a bad window is REFUSED, not quietly dropped", () =>
        {
            using var spectator = factory.NewClient();

            foreach (var query in new[]
                     {
                         "tz=Mars/Olympus", "day=9&from=1&to=2", "from=20&to=20&day=1", "from=20&to=3&day=1",
                     })
            {
                var response = spectator
                    .GetAsync($"/pl/lft?view=Players&game=Cs2&{query}").GetAwaiter().GetResult();
                that(response.StatusCode == HttpStatusCode.BadRequest,
                    $"'{query}' must be refused — a filter that stops applying shows a broader set " +
                    $"than the URL claims; got {(int)response.StatusCode}");
            }
        });

        check("availability: the filter reaches SQL as an overlap the GIN index can answer", () =>
        {
            // An index nothing reaches is storage. `&&` is the array predicate
            // GIN answers; an EXISTS over unnest() reads every live advert
            // however many indexes exist beside it.
            using var db = Db();
            int[] wanted = [41, 42];
            var sql = db.LftListings
                .Where(l => l.AvailabilityHours.Any(h => wanted.Contains(h)))
                .ToQueryString();

            that(sql.Contains("&&", StringComparison.Ordinal),
                $"the schedule predicate must reach SQL as an array overlap. Got:\n{sql}");

            var indexes = db.Database
                .SqlQuery<string>($"SELECT indexdef FROM pg_indexes WHERE tablename IN ('lft_listings','team_openings')")
                .ToList();

            that(indexes.Count(i => i.Contains("gin", StringComparison.OrdinalIgnoreCase)
                                    && i.Contains("availability_hours", StringComparison.Ordinal)) == 2,
                "and both advert tables must actually carry the GIN index");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM lft_listings WHERE account_id IN ({0},{1}); " +
                "DELETE FROM sessions WHERE account_id IN ({0},{1}); " +
                "DELETE FROM accounts WHERE id IN ({0},{1});",
                eveningId, anytimeId);
        }
    }
}
