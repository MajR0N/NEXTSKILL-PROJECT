using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;

namespace NextSkill.Checks;

/// <summary>
/// Reporting and moderating a public advert (decisions 015, 056).
///
/// [056](056-lft-board.md) kept <b>report</b> apart from decline and block
/// precisely so that one of the three would reach a human — then nothing was
/// built for that human to read. The check that carries the slice is the last
/// one: a takedown the author can undo with one click is a queue that does
/// nothing.
/// </summary>
public static class AdvertModerationChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = TestHost.Create();

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(null));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        const string password = "a-long-enough-passphrase";

        var staffNick = $"AMSt{stamp}";
        var authorNick = $"AMAu{stamp}";
        var reporterNick = $"AMRe{stamp}";
        var otherNick = $"AMOt{stamp}";

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

        using var staff = SignedInAs(staffNick);
        using var author = SignedInAs(authorNick);
        using var reporter = SignedInAs(reporterNick);
        using var other = SignedInAs(otherNick);

        Guid staffId, authorId, reporterId;
        using (var db = Db())
        {
            staffId = db.Accounts.Single(a => a.Nickname == staffNick).Id;
            authorId = db.Accounts.Single(a => a.Nickname == authorNick).Id;
            reporterId = db.Accounts.Single(a => a.Nickname == reporterNick).Id;

            db.Accounts.Single(a => a.Id == staffId).PlatformRole = PlatformRole.Staff;

            // Publishing requires something shareable after mutual acceptance.
            db.Accounts.Single(a => a.Id == authorId).DiscordId = $"d{stamp}";
            db.SaveChanges();
        }

        // The author publishes through the real form, so the republication guard
        // below is exercised on the same code path that created the advert.
        HttpResponseMessage Publish() =>
            author.PostFormAsync("/pl/me/recruitment?handler=PublishListing", new()
            {
                ["Listing.Game"] = nameof(Game.Cs2),
                ["Listing.Roles"] = "igl",
                ["Listing.Slots"] = "player",
                ["Listing.Note"] = $"Szukam składu {stamp}",
            }, "/pl/me/recruitment").GetAwaiter().GetResult();

        Publish();

        Guid listingId;
        using (var db = Db())
        {
            listingId = db.LftListings.AsNoTracking().Single(l => l.AccountId == authorId).Id;
        }

        HttpResponseMessage Report(HttpClient http, string reason) =>
            http.PostFormAsync("/pl/lft?handler=Report", new()
            {
                ["Action.Id"] = listingId.ToString(),
                ["Action.Note"] = reason,
            }, "/pl/lft?view=players").GetAwaiter().GetResult();

        HttpResponseMessage Decide(HttpClient http, string handler, Guid reportId, string tokenPage) =>
            http.PostFormAsync($"/pl/admin/reports?handler={handler}", new()
            {
                ["Input.ReportId"] = reportId.ToString(),
                ["Input.Reason"] = "sprawdzone",
            }, tokenPage).GetAwaiter().GetResult();

        int OpenReports()
        {
            using var db = Db();
            return db.AdvertReports.AsNoTracking()
                .Count(r => r.LftListingId == listingId && r.ResolvedAt == null);
        }

        check("advert: an anonymous visitor cannot report — browsing is open, flagging is not", () =>
        {
            using var anonymous = factory.NewClient();
            // The board itself renders no form for a visitor who is not signed
            // in — which is the first half of the answer — so the token has to
            // come from a page that does.
            var response = anonymous.PostFormAsync("/pl/lft?handler=Report", new()
            {
                ["Action.Id"] = listingId.ToString(),
            }, "/pl/account/login").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"an unauthenticated report must be sent to log in, got {(int)response.StatusCode}");
            that(OpenReports() == 0, "and must write nothing");
        });

        check("advert: a report is recorded once per person, however many times they click", () =>
        {
            var first = Report(reporter, "spam");
            that(first.StatusCode == HttpStatusCode.OK,
                $"reporting re-renders the board with a confirmation, got {(int)first.StatusCode}");
            that(OpenReports() == 1, "the report must be recorded");

            var again = Report(reporter, "spam again");
            that(again.StatusCode == HttpStatusCode.OK,
                "a second click is the same intent, not an error");
            that(OpenReports() == 1,
                "the database cap is what stops one reporter flooding the queue");

            Report(other, "też spam");
            that(OpenReports() == 2, "a DIFFERENT person reporting is a different fact");
        });

        check("advert: a player cannot reach the queue, and a decision POST is refused AND recorded", () =>
        {
            var page = reporter.GetAsync("/pl/admin/reports").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.Forbidden,
                $"reporting is public, deciding is not; got {(int)page.StatusCode}");

            Guid reportId;
            using (var db = Db())
            {
                reportId = db.AdvertReports.AsNoTracking()
                    .First(r => r.LftListingId == listingId && r.ResolvedAt == null).Id;
            }

            var response = Decide(reporter, "Remove", reportId, "/pl/me");
            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"and skipping the page must not skip the check; got {(int)response.StatusCode}");
            that(OpenReports() == 2, "nothing may have been decided");

            using var after = Db();
            that(after.AuditEntries.Count(a =>
                    a.ActorAccountId == reporterId && a.Action == AuditAction.ModerateAdvert &&
                    a.Outcome == AuditOutcome.Denied) == 1,
                "a refused moderation changes no row — only the log can hold the attempt");
        });

        check("advert: dismissing answers ONE report and leaves the advert up", () =>
        {
            Guid reportId;
            using (var db = Db())
            {
                reportId = db.AdvertReports.AsNoTracking()
                    .First(r => r.LftListingId == listingId && r.ResolvedAt == null).Id;
            }

            var response = Decide(staff, "Dismiss", reportId, "/pl/admin/reports");
            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a decision redirects, got {(int)response.StatusCode}");

            that(OpenReports() == 1,
                "dismissing is a judgement about ONE complaint; the other stands until it is answered too");

            using var after = Db();
            var listing = after.LftListings.AsNoTracking().Single(l => l.Id == listingId);
            that(listing.ClosedAt is null && listing.ModeratedAt is null,
                "and an advert found acceptable must not be touched");
        });

        check("advert: a takedown hides it, answers every open report about it, and BLOCKS republication", () =>
        {
            Guid reportId;
            using (var db = Db())
            {
                // Put the dismissed reporter back in the queue, so this decision
                // has two open reports to answer rather than one.
                db.AdvertReports
                    .Where(r => r.LftListingId == listingId)
                    .ExecuteUpdate(s => s.SetProperty(r => r.ResolvedAt, (DateTimeOffset?)null));

                reportId = db.AdvertReports.AsNoTracking().First(r => r.LftListingId == listingId).Id;
            }
            that(OpenReports() == 2, "two open reports about one advert");

            var response = Decide(staff, "Remove", reportId, "/pl/admin/reports");
            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a takedown redirects, got {(int)response.StatusCode}");

            that(OpenReports() == 0,
                "one decision answers every open report about the same advert — a queue must not regrow with work already done");

            using (var db = Db())
            {
                var listing = db.LftListings.AsNoTracking().Single(l => l.Id == listingId);
                that(listing.ClosedAt is not null, "ClosedAt is what every public read already filters on");
                that(listing.ModeratedAt is not null, "ModeratedAt is what stops the author undoing it");
            }

            using (var browser = factory.NewClient())
            {
                var board = browser.GetStringAsync("/pl/lft?view=players").GetAwaiter().GetResult();
                that(!board.Contains($"Szukam składu {stamp}", StringComparison.Ordinal),
                    "the advert must be gone from the board without any query having changed");
            }

            // THE ONE THAT MATTERS. A listing is one row per account and game,
            // reused on republication — so without ModeratedAt this click puts
            // the moderated advert straight back on the board.
            var republish = Publish();
            that(republish.StatusCode == HttpStatusCode.OK,
                $"republishing a moderated advert must be refused on the form, got {(int)republish.StatusCode}");

            using (var db = Db())
            {
                var listing = db.LftListings.AsNoTracking().Single(l => l.Id == listingId);
                that(listing.ClosedAt is not null,
                    "A TAKEDOWN THE AUTHOR CAN UNDO IN ONE CLICK IS THEATRE — the advert must stay down");
            }

            using var log = Db();
            that(log.AuditEntries.Any(a =>
                    a.ActorAccountId == staffId && a.Action == AuditAction.ModerateAdvert &&
                    a.Outcome == AuditOutcome.Allowed && a.ResourceId == listingId),
                "and the takedown is answerable later");
        });

        using (var db = Db())
        {
            db.LftListings.Where(l => l.Id == listingId).ExecuteDelete();
        }
    }
}
