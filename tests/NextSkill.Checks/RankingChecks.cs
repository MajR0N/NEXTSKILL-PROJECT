using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Pages.Ranking;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Ratings;

namespace NextSkill.Checks;

/// <summary>
/// Ranking-page checks against the real database (decisions 021, 025).
///
/// The season label looks cosmetic, but a hardcoded label silently becomes a
/// false claim when the calendar advances. These checks exercise the same EF
/// query as the page, inside rolled-back transactions so the development
/// database is left exactly as it was.
/// </summary>
public static class RankingChecks
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => null;
        public Guid RequireOrganiserId() =>
            throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new NoTenant());
        var cursorProtection = new EphemeralDataProtectionProvider();
        IndexModel Page(NextSkillDb db) => new(db, cursorProtection);

        check("ranking: the current season label comes from live data", () =>
        {
            using var db = Db();
            using var transaction = db.Database.BeginTransaction();
            var now = DateTimeOffset.UtcNow;
            var nextNumber = db.Seasons.Max(s => (int?)s.Number) is { } highest ? highest + 1 : 1;
            var expected = $"Aktualny {Guid.NewGuid():N}";

            // Existing open seasons are made ineligible only inside this
            // transaction. Disposal rolls the whole arrangement back.
            foreach (var season in db.Seasons.Where(s => !s.Closed))
                season.Closed = true;

            db.Seasons.AddRange(
                new Season
                {
                    Id = Guid.NewGuid(), Number = nextNumber, Name = expected,
                    StartsAt = now.AddDays(-1), Closed = false,
                },
                new Season
                {
                    Id = Guid.NewGuid(), Number = nextNumber + 1, Name = "Zamknięty",
                    StartsAt = now.AddDays(-2), EndsAt = now.AddDays(-1), Closed = true,
                },
                new Season
                {
                    Id = Guid.NewGuid(), Number = nextNumber + 2, Name = "Jeszcze nie",
                    StartsAt = now.AddDays(1), Closed = false,
                });
            db.SaveChanges();

            var page = Page(db);
            page.OnGetAsync().GetAwaiter().GetResult();

            that(page.ActiveSeasonName == expected,
                $"expected the live season name, got '{page.ActiveSeasonName ?? "<none>"}'");
        });

        check("ranking: no active season means no fabricated season label", () =>
        {
            using var db = Db();
            using var transaction = db.Database.BeginTransaction();

            foreach (var season in db.Seasons.Where(s => !s.Closed))
                season.Closed = true;
            db.SaveChanges();

            var page = Page(db);
            page.OnGetAsync().GetAwaiter().GetResult();

            that(page.ActiveSeasonName is null,
                $"the page invented season '{page.ActiveSeasonName}' when none is active");
        });

        check("ranking: player rows use OpenSkill's conservative score and provisional gate", () =>
        {
            using var db = Db();
            using var transaction = db.Database.BeginTransaction();
            var stamp = Guid.NewGuid().ToString("N")[..12];

            Account Player(string label) => new()
            {
                Id = Guid.NewGuid(),
                Email = $"{label}-{stamp}@nextskill.test",
                Nickname = $"{label}{stamp}",
                PasswordHash = "not-used",
                CreatedAt = DateTimeOffset.UtcNow,
            };

            var proven = Player("Proven");
            var runnerUp = Player("Runner");
            var newcomer = Player("New");
            var otherGame = Player("Other");
            var team = new Team
            {
                Id = Guid.NewGuid(), Name = $"Not a player {stamp}", Tag = $"NP{stamp[..4]}",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AddRange(proven, runnerUp, newcomer, otherGame, team);
            db.Ratings.AddRange(
                PlayerRatingRow(proven.Id, Game.Cs16, mu: 30, sigma: 2, matches: 12),
                PlayerRatingRow(runnerUp.Id, Game.Cs16, mu: 29, sigma: 2, matches: 11),
                // Highest stored mean, but too uncertain to receive a rank.
                PlayerRatingRow(newcomer.Id, Game.Cs16, mu: 40, sigma: 8, matches: 1),
                PlayerRatingRow(otherGame.Id, Game.Cs2, mu: 50, sigma: 1, matches: 20),
                new Rating
                {
                    Id = Guid.NewGuid(), TeamId = team.Id, Game = Game.Cs16,
                    Value = 9999, Deviation = 30, Volatility = 0.06, MatchesPlayed = 30,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            db.SaveChanges();

            var page = Page(db);
            page.Scope = RankingScope.Players;
            page.Game = Game.Cs16;
            page.OnGetAsync().GetAwaiter().GetResult();

            var first = page.Ranked.Single(r => r.Id == proven.Id);
            var second = page.Ranked.Single(r => r.Id == runnerUp.Id);
            that(Math.Abs(first.Rating - new PlayerRating(30, 2).Ordinal) < 0.0001,
                "the public number must be mu - 3 sigma, never the stored mean");
            that(first.Rank < second.Rank,
                "players must be ordered by the conservative public score");
            that(page.Provisional.Any(r => r.Id == newcomer.Id) &&
                 page.Ranked.All(r => r.Id != newcomer.Id),
                "an uncertain newcomer must be visible as provisional but receive no rank");
            that(page.Ranked.Concat(page.Provisional).All(r => r.Id != otherGame.Id && r.Id != team.Id),
                "the player view must not mix games or team ratings into its rows");
        });

        check("ranking: player scope is selectable through a shareable URL", () =>
        {
            using var factory = TestHost.Create(b =>
                b.UseSetting("ConnectionStrings:NextSkill", connectionString));
            using var http = factory.NewClient();

            var response = http.GetAsync("/pl/ranking?scope=Players&game=Cs16")
                .GetAwaiter().GetResult();
            var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                $"player ranking returned {(int)response.StatusCode}");
            that(html.Contains("<h1>Ranking graczy</h1>") && html.Contains("ostrożny wynik OpenSkill"),
                "the URL must render the player surface and explain its conservative score");
            that(html.Contains("scope=Players") && html.Contains("game=Cs16"),
                "switching the game or scope must keep both filters in the URL");
        });

        check("ranking: cursor pagination returns every row once and caps the page", () =>
        {
            using var db = Db();
            using var transaction = db.Database.BeginTransaction();
            var prefix = $"Pg{Guid.NewGuid():N}"[..14];

            for (var i = 0; i < 25; i++)
            {
                var player = new Account
                {
                    Id = Guid.NewGuid(), Email = $"{prefix}-{i}@nextskill.test",
                    Nickname = $"{prefix}{i:D2}", PasswordHash = "not-used",
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Accounts.Add(player);
                db.Ratings.Add(PlayerRatingRow(
                    player.Id, Game.Cs2, mu: 100 - i, sigma: 1, matches: 20));
            }
            db.SaveChanges();

            var first = Page(db);
            first.Scope = RankingScope.Players;
            first.Game = Game.Cs2;
            first.Q = prefix;
            var firstResult = first.OnGetAsync().GetAwaiter().GetResult();

            that(firstResult is PageResult && first.Ranked.Count == IndexModel.PageSize,
                $"the first page must contain exactly {IndexModel.PageSize} rows");
            that(first.NextCursor is not null,
                "twenty-five matching rows must advertise a next page");

            var tamperedChars = first.NextCursor!.ToCharArray();
            var changedAt = tamperedChars.Length / 2;
            tamperedChars[changedAt] = tamperedChars[changedAt] == 'A' ? 'B' : 'A';
            var tampered = Page(db);
            tampered.Scope = RankingScope.Players;
            tampered.Game = Game.Cs2;
            tampered.Q = prefix;
            tampered.Cursor = new string(tamperedChars);
            that(tampered.OnGetAsync().GetAwaiter().GetResult() is BadRequestObjectResult,
                "a cursor whose rank count was edited must be rejected as tampered");

            var second = Page(db);
            second.Scope = RankingScope.Players;
            second.Game = Game.Cs2;
            second.Q = prefix;
            second.Cursor = first.NextCursor;
            var secondResult = second.OnGetAsync().GetAwaiter().GetResult();

            var firstIds = first.Ranked.Select(r => r.Id).ToHashSet();
            var secondIds = second.Ranked.Select(r => r.Id).ToHashSet();
            that(secondResult is PageResult && second.Ranked.Count == 5 && second.NextCursor is null,
                "the final page must contain the five remaining rows and no next cursor");
            that(!firstIds.Overlaps(secondIds) && firstIds.Count + secondIds.Count == 25,
                "cursor pages must neither repeat nor skip a player");
            that(first.Ranked.First().Rank == 1 && first.Ranked.Last().Rank == 20 &&
                 second.Ranked.First().Rank == 21 && second.Ranked.Last().Rank == 25,
                "rank numbers must continue across the cursor boundary");
        });

        check("ranking: percent and underscore are literal search text, not wildcards", () =>
        {
            using var db = Db();
            using var transaction = db.Database.BeginTransaction();
            var stamp = Guid.NewGuid().ToString("N")[..10];
            var literal = new Account
            {
                Id = Guid.NewGuid(), Email = $"pct-{stamp}@nextskill.test",
                Nickname = $"Pct%{stamp}", PasswordHash = "not-used",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var ordinary = new Account
            {
                Id = Guid.NewGuid(), Email = $"plain-{stamp}@nextskill.test",
                Nickname = $"Plain{stamp}", PasswordHash = "not-used",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var literalTeam = new Team
            {
                Id = Guid.NewGuid(), Name = $"Literal team {stamp}", Tag = $"U_{stamp[..4]}",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var ordinaryTeam = new Team
            {
                Id = Guid.NewGuid(), Name = $"Ordinary team {stamp}", Tag = $"UT{stamp[..4]}",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AddRange(literal, ordinary, literalTeam, ordinaryTeam);
            db.Ratings.AddRange(
                PlayerRatingRow(literal.Id, Game.Cs16, 30, 2, 10),
                PlayerRatingRow(ordinary.Id, Game.Cs16, 31, 2, 10),
                TeamRatingRow(literalTeam.Id, Game.Cs16),
                TeamRatingRow(ordinaryTeam.Id, Game.Cs16));
            db.SaveChanges();

            var page = Page(db);
            page.Scope = RankingScope.Players;
            page.Game = Game.Cs16;
            page.Q = "%";
            page.OnGetAsync().GetAwaiter().GetResult();

            var ids = page.Ranked.Concat(page.Provisional).Select(r => r.Id).ToList();
            that(ids.SequenceEqual([literal.Id]),
                "a percent sign must find the literal nickname, not behave like 'match everything'");

            var teams = Page(db);
            teams.Scope = RankingScope.Teams;
            teams.Game = Game.Cs16;
            teams.Q = "_";
            teams.OnGetAsync().GetAwaiter().GetResult();
            var teamIds = teams.Ranked.Concat(teams.Provisional).Select(r => r.Id).ToList();
            that(teamIds.SequenceEqual([literalTeam.Id]),
                "an underscore must find the literal team tag, not match any character");
        });

        check("ranking: malformed cursors and oversized searches are rejected", () =>
        {
            using var db = Db();
            var badCursor = Page(db);
            badCursor.Cursor = "not-a-cursor";
            var badCursorResult = badCursor.OnGetAsync().GetAwaiter().GetResult();
            that(badCursorResult is BadRequestObjectResult,
                "a malformed cursor must be 400, not silently restart at page one");

            var tooLong = Page(db);
            tooLong.Q = new string('x', IndexModel.MaxQueryLength + 1);
            var tooLongResult = tooLong.OnGetAsync().GetAwaiter().GetResult();
            that(tooLongResult is BadRequestObjectResult,
                "an unbounded search term must be rejected before it reaches PostgreSQL");
        });

        static Rating PlayerRatingRow(Guid accountId, Game game, double mu, double sigma, int matches) => new()
        {
            Id = Guid.NewGuid(), AccountId = accountId, Game = game,
            Value = mu, Deviation = sigma, Volatility = 0, MatchesPlayed = matches,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        static Rating TeamRatingRow(Guid teamId, Game game) => new()
        {
            Id = Guid.NewGuid(), TeamId = teamId, Game = game,
            Value = 1500, Deviation = 80, Volatility = 0.06, MatchesPlayed = 10,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }
}
