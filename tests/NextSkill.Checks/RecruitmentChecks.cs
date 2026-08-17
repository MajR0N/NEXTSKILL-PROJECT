using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NextSkill.Api.Features.Recruitment;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Recruitment;
using NextSkill.Domain.Teams;

namespace NextSkill.Checks;

/// <summary>
/// The LFT board and recruitment (decision 056).
///
/// The rules under test are the ones that separate this from a Discord advert:
/// freshness never yields to rating, a team approach always names a real
/// vacancy, approaches are capped in both directions, and a contact crosses only
/// after both sides agreed.
/// </summary>
public static class RecruitmentChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        // ── the pure rules, with no database in the way ──────────────────
        check("recruitment: publication validation is a rule, not a form hint", () =>
        {
            that(RecruitmentRules.RejectPublication(
                ["igl", "awp", "entry"], ["player"], [], [], [], null) is not null,
                "more than two roles must be refused");

            that(RecruitmentRules.RejectPublication(
                ["superstar"], ["player"], [], [], [], null) is not null,
                "an invented role must be refused — the vocabulary is the product's, not the user's");

            that(RecruitmentRules.RejectPublication(
                ["igl"], [], [], [], [], null) is not null,
                "an advert with no roster slot says nothing");

            that(RecruitmentRules.RejectPublication(
                ["igl"], ["player"], [], [], [], new string('x', 301)) is not null,
                "a note over 300 characters must be refused");

            that(RecruitmentRules.RejectPublication(
                ["igl", "awp"], ["player", "substitute"], ["pl"], ["eu-central"], [12, 13], "cześć") is null,
                "and a well-formed advert must pass");
        });

        check("recruitment: a note edit is NOT a material change", () =>
        {
            // If it were, every advertiser would learn to tweak a full stop each
            // morning and freshness ordering would stop meaning anything.
            that(!RecruitmentRules.IsMaterialChange(
                    ["igl"], ["igl"], ["player"], ["player"],
                    ["pl"], ["pl"], ["eu-central"], ["eu-central"], [10], [10]),
                "identical structure must not start a new cycle whatever the note says");

            that(RecruitmentRules.IsMaterialChange(
                    ["igl"], ["awp"], ["player"], ["player"],
                    ["pl"], ["pl"], ["eu-central"], ["eu-central"], [10], [10]),
                "changing the role IS a new advert");

            that(RecruitmentRules.IsMaterialChange(
                    ["igl"], ["igl"], ["player"], ["substitute"],
                    ["pl"], ["pl"], ["eu-central"], ["eu-central"], [10], [10]),
                "so is changing the roster slot — it is a different request entirely");

            // Order must not matter: a checkbox list has no meaningful order.
            that(!RecruitmentRules.IsMaterialChange(
                    ["igl", "awp"], ["awp", "igl"], ["player"], ["player"],
                    [], [], [], [], [], []),
                "the same roles in a different order are the same advert");
        });

        // ── and the board, through the real pipeline ─────────────────────
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        using var factory = TestHost.Create(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            // Driven by hand, so the lifecycle is deterministic rather than
            // racing a five-minute timer.
            services.RemoveAll<IHostedService>();
        }));

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(null));

        bool Sweep()
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<RecruitmentLifecycle>()
                .SweepAsync().GetAwaiter().GetResult();
        }

        var stamp = DateTimeOffset.UtcNow.Ticks;
        const string password = "a-long-enough-passphrase";

        var captainNick = $"Cap{stamp}";
        var playerNick = $"Lft{stamp}";
        var otherNick = $"Oth{stamp}";

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

        using var captain = SignedInAs(captainNick);
        using var lftPlayer = SignedInAs(playerNick);
        using var other = SignedInAs(otherNick);

        Guid captainId, playerId, otherId;
        var teamId = Guid.NewGuid();
        var teamName = $"Rekrut {stamp}";

        using (var db = Db())
        {
            captainId = db.Accounts.Single(a => a.Nickname == captainNick).Id;
            playerId = db.Accounts.Single(a => a.Nickname == playerNick).Id;
            otherId = db.Accounts.Single(a => a.Nickname == otherNick).Id;

            // Publishing needs a shareable verified contact, because the whole
            // exchange ends in one crossing.
            foreach (var id in new[] { captainId, playerId, otherId })
                db.Accounts.Single(a => a.Id == id).DiscordId = $"{id.GetHashCode() & 0x7fffffff}";

            db.Teams.Add(new Team
            {
                Id = teamId, Name = teamName, Tag = $"RK{stamp % 100000}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.TeamMemberships.Add(new TeamMembership
            {
                Id = Guid.NewGuid(), TeamId = teamId, AccountId = captainId,
                Role = TeamRole.Captain, Slot = RosterSlot.Player,
                JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        check("lft: an anonymous visitor gets the CS2 team view, complete, and noindex", () =>
        {
            using var spectator = factory.NewClient();
            var response = spectator.GetAsync("/pl/lft").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.OK,
                $"the board must be browsable without an account, got {(int)response.StatusCode}");

            that(response.Headers.TryGetValues("X-Robots-Tag", out var robots) &&
                 robots.Any(v => v.Contains("noindex")),
                "volatile recruitment state must not be indexed, while links out of it still follow");

            var html = WebUtility.HtmlDecode(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            that(html.Contains("Drużyny rekrutują", StringComparison.Ordinal),
                "and it defaults to teams recruiting — the arriving visitor is usually looking for a team");
        });

        check("lft: an unknown filter value is refused, never silently ignored", () =>
        {
            using var spectator = factory.NewClient();
            var response = spectator.GetAsync("/pl/lft?role=superstar").GetAwaiter().GetResult();
            that(response.StatusCode == HttpStatusCode.BadRequest,
                $"silently dropping it would show a broader set than the URL claims, got {(int)response.StatusCode}");
        });

        check("lft: a player may advertise while unrated, and is labelled honestly", () =>
        {
            var response = lftPlayer.PostFormAsync("/pl/me/recruitment?handler=PublishListing", new()
            {
                ["Listing.Game"] = "Cs2",
                ["Listing.Slots"] = "player",
                ["Listing.Roles"] = "awp",
                ["Listing.Languages"] = "pl",
                ["Listing.Regions"] = "eu-central",
                ["Listing.Note"] = "Gram wieczorami",
            }, "/pl/me/recruitment").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"publishing should redirect, got {(int)response.StatusCode}");

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator
                .GetStringAsync("/pl/lft?view=Players&game=Cs2").GetAwaiter().GetResult());

            that(html.Contains(playerNick, StringComparison.Ordinal),
                "the listing must appear on the players side");
            that(html.Contains("Bez ratingu", StringComparison.Ordinal),
                "an unrated player is labelled Unrated — requiring a rating would make finding " +
                "a first team depend on already having had one (056)");
            that(html.Contains("Gram wieczorami", StringComparison.Ordinal),
                "and the note is shown as plain text");
        });

        check("lft: an opening cannot exceed the roster capacity behind it", () =>
        {
            // Five player places, one occupied by the captain: four openings fit,
            // the fifth is a promise the roster cannot keep (045).
            for (var i = 0; i < 4; i++)
            {
                var ok = captain.PostFormAsync("/pl/me/recruitment?handler=PublishOpening", new()
                {
                    ["Opening.TeamId"] = teamId.ToString(),
                    ["Opening.Game"] = "Cs2",
                    ["Opening.Slot"] = "Player",
                    ["Opening.Roles"] = "entry",
                }, "/pl/me/recruitment").GetAwaiter().GetResult();
                that(ok.StatusCode == HttpStatusCode.Redirect,
                    $"opening {i + 1} should be allowed, got {(int)ok.StatusCode}");
            }

            var overflow = captain.PostFormAsync("/pl/me/recruitment?handler=PublishOpening", new()
            {
                ["Opening.TeamId"] = teamId.ToString(),
                ["Opening.Game"] = "Cs2",
                ["Opening.Slot"] = "Player",
                ["Opening.Roles"] = "entry",
            }, "/pl/me/recruitment").GetAwaiter().GetResult();

            that(overflow.StatusCode == HttpStatusCode.OK,
                "the fifth must be refused with an explanation, not accepted");

            using var db = Db();
            that(db.TeamOpenings.Count(o => o.TeamId == teamId && o.ClosedAt == null) == 4,
                "spare capacity bounds how many vacancies can be advertised at once");
        });

        check("lft: a non-captain cannot publish a vacancy for somebody else's team", () =>
        {
            var response = other.PostFormAsync("/pl/me/recruitment?handler=PublishOpening", new()
            {
                ["Opening.TeamId"] = teamId.ToString(),
                ["Opening.Game"] = "Cs2",
                ["Opening.Slot"] = "Substitute",
            }, "/pl/me/recruitment").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"only a current captain recruits on a team's behalf, got {(int)response.StatusCode}");
        });

        Guid openingId;
        using (var db = Db())
            openingId = db.TeamOpenings.AsNoTracking().First(o => o.TeamId == teamId).Id;

        check("lft: freshness orders the board and a rating never buys prominence", () =>
        {
            using (var db = Db())
            {
                // Give the team a strong settled rating. If rating influenced
                // order, this row would climb.
                db.Ratings.Add(new Rating
                {
                    Id = Guid.NewGuid(), TeamId = teamId, Game = Game.Cs2,
                    Value = 2400, Deviation = 35, Volatility = 0.06,
                    MatchesPlayed = 40, UpdatedAt = clock.GetUtcNow(),
                });

                // And publish a NEWER, unrated opening from another team.
                var rivalTeam = Guid.NewGuid();
                db.Teams.Add(new Team
                {
                    Id = rivalTeam, Name = $"Nowsi {stamp}", Tag = $"NW{stamp % 100000}",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.TeamOpenings.Add(new TeamOpening
                {
                    Id = Guid.NewGuid(), TeamId = rivalTeam, Game = Game.Cs2,
                    Slot = RecruitmentSlot.Player,
                    ContactCaptainAccountId = otherId,
                    PublishedAt = clock.GetUtcNow().AddMinutes(5),
                    ExpiresAt = clock.GetUtcNow() + RecruitmentRules.AdvertLifetime,
                    CreatedAt = clock.GetUtcNow(),
                });
                db.SaveChanges();
            }

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator
                .GetStringAsync("/pl/lft?view=Teams&game=Cs2").GetAwaiter().GetResult());

            var newer = html.IndexOf($"Nowsi {stamp}", StringComparison.Ordinal);
            var rated = html.IndexOf(teamName, StringComparison.Ordinal);

            that(newer >= 0 && rated >= 0, "both openings must be on the board");
            that(newer < rated,
                "THE NEWER ADVERT COMES FIRST even though the other team is rated 2400. " +
                "Rating-first ordering turns evidence into permanent visibility and starves " +
                "the new teams the board exists to serve (056)");
        });

        check("lft: a team cannot approach a player without a real opening behind it", () =>
        {
            // Not a UI convenience: the server refuses it. A context-free
            // approach is a direct message, which recreates the spam the board
            // exists to replace.
            var response = captain.PostFormAsync("/pl/lft?view=Players&handler=Invite", new()
            {
                ["Action.Id"] = Guid.NewGuid().ToString(),
                ["Action.OpeningId"] = Guid.NewGuid().ToString(),
            }, "/pl/lft?view=Players").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.NotFound,
                $"an approach naming no real opening must fail, got {(int)response.StatusCode}");
        });

        check("lft: an application and a team interest both work, and cap at ten", () =>
        {
            var applied = other.PostFormAsync("/pl/lft?handler=Apply", new()
            {
                ["Action.Id"] = openingId.ToString(),
                ["Action.Note"] = "Chętnie zagram",
            }, "/pl/lft").GetAwaiter().GetResult();

            that(applied.StatusCode == HttpStatusCode.Redirect,
                $"a player may apply without having a public advert (049), got {(int)applied.StatusCode}");

            using (var db = Db())
            {
                that(db.OpeningApplications.Count(a => a.TeamOpeningId == openingId && a.AccountId == otherId) == 1,
                    "exactly one application is recorded");
            }

            // A second application to the same opening in the same cycle is
            // refused by the database, not merely by a screen.
            var again = other.PostFormAsync("/pl/lft?handler=Apply", new()
            {
                ["Action.Id"] = openingId.ToString(),
            }, "/pl/lft").GetAwaiter().GetResult();
            that(again.StatusCode == HttpStatusCode.OK,
                "a repeat application must be refused with an explanation");

            using var check2 = Db();
            that(check2.OpeningApplications.Count(a => a.TeamOpeningId == openingId && a.AccountId == otherId) == 1,
                "and must not create a second row");
        });

        check("lft: the pending cap holds — ten approaches, then no more", () =>
        {
            using (var db = Db())
            {
                // Nine more pending applications for this player against other
                // openings, bringing them to the cap.
                var filler = new List<TeamOpening>();
                for (var i = 0; i < 9; i++)
                {
                    var fillerTeam = Guid.NewGuid();
                    db.Teams.Add(new Team
                    {
                        Id = fillerTeam, Name = $"Cap{i}-{stamp}", Tag = $"C{i}{stamp % 10000}",
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                    var opening = new TeamOpening
                    {
                        Id = Guid.NewGuid(), TeamId = fillerTeam, Game = Game.Cs2,
                        Slot = RecruitmentSlot.Player, ContactCaptainAccountId = captainId,
                        PublishedAt = clock.GetUtcNow(),
                        ExpiresAt = clock.GetUtcNow() + RecruitmentRules.AdvertLifetime,
                        CreatedAt = clock.GetUtcNow(),
                    };
                    filler.Add(opening);
                    db.TeamOpenings.Add(opening);
                }

                db.SaveChanges();

                foreach (var opening in filler)
                {
                    db.OpeningApplications.Add(new OpeningApplication
                    {
                        Id = Guid.NewGuid(), TeamOpeningId = opening.Id, AccountId = otherId,
                        Cycle = 1, State = RecruitmentState.Pending,
                        CreatedAt = clock.GetUtcNow(),
                        ExpiresAt = clock.GetUtcNow() + RecruitmentRules.RequestLifetime,
                    });
                }

                db.SaveChanges();
            }

            using var db2 = Db();
            var eleventh = db2.TeamOpenings.AsNoTracking()
                .First(o => o.TeamId == teamId && o.Id != openingId);

            var response = other.PostFormAsync("/pl/lft?handler=Apply", new()
            {
                ["Action.Id"] = eleventh.Id.ToString(),
            }, "/pl/lft").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                "the eleventh pending approach must be refused");

            using var db3 = Db();
            that(db3.OpeningApplications.Count(a => a.AccountId == otherId && a.State == RecruitmentState.Pending) == 10,
                "without this cap, TeamInterest reopens the 200-invite hole 045 deliberately closed (051 §5)");
        });

        check("lft: acceptance reveals exactly one contact, and only after both agreed", () =>
        {
            Guid applicationId;
            using (var lookup = Db())
                applicationId = lookup.OpeningApplications.AsNoTracking()
                    .First(a => a.TeamOpeningId == openingId && a.AccountId == otherId).Id;

            // Before acceptance the identifier appears nowhere public.
            using (var spectator = factory.NewClient())
            {
                var board = spectator.GetStringAsync("/pl/lft?view=Players").GetAwaiter().GetResult();
                that(!board.Contains("Discord:", StringComparison.Ordinal),
                    "no contact identifier may appear in a public projection");
            }

            var accepted = captain.PostFormAsync("/pl/me/recruitment?handler=Accept", new()
            {
                ["Request.Id"] = applicationId.ToString(),
            }, "/pl/me/recruitment").GetAwaiter().GetResult();
            that(accepted.StatusCode == HttpStatusCode.Redirect,
                $"the captain accepts contact, got {(int)accepted.StatusCode}");

            var mine = WebUtility.HtmlDecode(
                other.GetStringAsync("/pl/me/recruitment").GetAwaiter().GetResult());
            that(mine.Contains("Discord:", StringComparison.Ordinal),
                "the applicant now sees the designated captain's chosen contact");
            that(mine.Contains("Kontakty", StringComparison.Ordinal),
                "and it appears as a mutual contact in the private workspace");

            using var db = Db();
            that(db.ContactGrants.Count(g => g.TeamOpeningId == openingId && g.AccountId == otherId) == 1,
                "one grant, bounded and revocable");
        });

        check("lft: a block suppresses later approaches in both directions", () =>
        {
            using (var db = Db())
            {
                db.RecruitmentBlocks.Add(new RecruitmentBlock
                {
                    Id = Guid.NewGuid(), AccountId = playerId, TeamId = teamId,
                    ByPlayer = true, CreatedAt = clock.GetUtcNow(),
                });
                db.SaveChanges();
            }

            var response = lftPlayer.PostFormAsync("/pl/lft?handler=Apply", new()
            {
                ["Action.Id"] = openingId.ToString(),
            }, "/pl/lft").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                "a blocked pair must not be able to approach");

            using var db2 = Db();
            that(!db2.OpeningApplications.Any(a => a.TeamOpeningId == openingId && a.AccountId == playerId),
                "and nothing may be written — a block is durable, unlike a decline which binds to one cycle");
        });

        check("lft: expiry removes an advert from the board, and no path auto-renews it", () =>
        {
            clock.Advance(RecruitmentRules.AdvertLifetime + TimeSpan.FromHours(1));

            using var spectator = factory.NewClient();
            var html = WebUtility.HtmlDecode(spectator
                .GetStringAsync("/pl/lft?view=Players&game=Cs2").GetAwaiter().GetResult());

            that(!html.Contains(playerNick, StringComparison.Ordinal),
                "PUBLIC READS EXCLUDE EXPIRED ROWS WITHOUT THE WORKER — a late sweeper must " +
                "never be what leaves a stale advert on the board");

            Sweep();

            using var after = Db();
            var listing = after.LftListings.AsNoTracking().Single(l => l.AccountId == playerId);
            that(listing.ExpiresAt <= clock.GetUtcNow(),
                "nothing may quietly extend an advert — automatic renewal makes board activity dishonest");

            that(after.OpeningApplications.Count(a => a.AccountId == otherId && a.State == RecruitmentState.Pending) == 0,
                "and pending requests expire too, which is what frees the sender's cap");
        });

        check("lft: erasure removes recruitment state immediately", () =>
        {
            using (var db = Db())
            {
                that(db.ContactGrants.Any(g => g.AccountId == otherId),
                    "arrange: the grant exists before erasure");
            }

            // A fresh sign-in, because the expiry check above advanced the clock
            // past Sessions.Lifetime and every cookie in this suite died with
            // it — which is the session lifetime working, not a problem.
            using var returning = factory.NewClient();
            returning.PostFormAsync("/pl/account/login", new()
            {
                ["Input.Email"] = $"{otherNick.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();

            var erased = returning.PostFormAsync("/pl/me?handler=DeleteAccount", new()
            {
                ["DeleteAccount.Password"] = password,
                ["DeleteAccount.ConfirmationNickname"] = otherNick,
            }, tokenPage: "/pl/me").GetAwaiter().GetResult();

            that(erased.StatusCode == HttpStatusCode.Redirect,
                $"erasure should complete and sign the person out, got {(int)erased.StatusCode}");

            using var db2 = Db();
            that(!db2.ContactGrants.Any(g => g.AccountId == otherId),
                "a live contact grant is exactly the transient personal data erasure is for");
            that(!db2.OpeningApplications.Any(a => a.AccountId == otherId),
                "and so are pending approaches");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM contact_grants WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM opening_applications WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM team_interests WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM recruitment_blocks WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM lft_listings WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM notifications WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM team_openings WHERE contact_captain_account_id IN ({0},{1},{2}) OR team_id={3}; " +
                "DELETE FROM ratings WHERE team_id={3}; " +
                "DELETE FROM team_memberships WHERE team_id={3}; " +
                "DELETE FROM sessions WHERE account_id IN ({0},{1},{2}); " +
                "DELETE FROM accounts WHERE id IN ({0},{1},{2});",
                captainId, playerId, otherId, teamId);

            db.Database.ExecuteSqlRaw(
                "DELETE FROM team_openings WHERE team_id IN (SELECT id FROM teams WHERE tag LIKE {0}); " +
                "DELETE FROM teams WHERE tag LIKE {0} OR id={1};",
                $"%{stamp % 10000}", teamId);
        }
    }
}
