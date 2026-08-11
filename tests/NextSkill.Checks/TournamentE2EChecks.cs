using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Checks;

/// <summary>
/// Tournaments through the real pipeline (decisions 036, 046).
///
/// The risk this slice introduced is the one worth testing hardest: tournaments
/// are <b>tenant-scoped</b>, but the public list and page have no organiser in
/// scope. Every read therefore goes through <c>PublicReads</c>, and a mistake
/// there does not throw — it quietly shows one organiser's private drafts to
/// everybody.
/// </summary>
public static class TournamentE2EChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = new WebApplicationFactory<NextSkillDb>()
            .WithWebHostBuilder(b => b
                .UseEnvironment("Development")
                .UseContentRoot(FindApiContentRoot()));

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var orgId = Guid.NewGuid();
        var rivalOrgId = Guid.NewGuid();
        const string password = "a-long-enough-passphrase";

        var organiserNick = $"Org{stamp}";
        var captainNick = $"Cpt{stamp}";
        var strangerNick = $"Str{stamp}";

        HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        async Task<HttpResponseMessage> PostAsync(
            HttpClient http, string path, Dictionary<string, string> fields, string? tokenPage = null)
        {
            var page = await http.GetAsync(tokenPage ?? path);
            fields["__RequestVerificationToken"] = TokenFrom(await page.Content.ReadAsStringAsync());
            return await http.PostAsync(path, new FormUrlEncodedContent(fields));
        }

        HttpClient SignedInAs(string nickname)
        {
            var http = Client();
            PostAsync(http, "/pl/account/register", new()
            {
                ["Input.Nickname"] = nickname,
                ["Input.Email"] = $"{nickname.ToLowerInvariant()}@nextskill.test",
                ["Input.Password"] = password,
            }).GetAwaiter().GetResult();
            return http;
        }

        using var asOrganiser = SignedInAs(organiserNick);
        using var asCaptain = SignedInAs(captainNick);
        using var asStranger = SignedInAs(strangerNick);

        Guid organiserAccount, captainAccount, teamId, thinTeamId;
        var teamTag = $"TT{stamp % 100000}";
        var thinTag = $"TH{stamp % 100000}";

        using (var db = Db())
        {
            organiserAccount = db.Accounts.AsNoTracking().Single(a => a.Nickname == organiserNick).Id;
            captainAccount = db.Accounts.AsNoTracking().Single(a => a.Nickname == captainNick).Id;

            db.Organisers.Add(new Organiser { Id = orgId, Name = $"Org {stamp}", Slug = $"org-{stamp}", CreatedAt = DateTimeOffset.UtcNow });
            db.Organisers.Add(new Organiser { Id = rivalOrgId, Name = $"Rival {stamp}", Slug = $"rival-{stamp}", CreatedAt = DateTimeOffset.UtcNow });
            db.SaveChanges();
        }

        using (var db = Db(orgId))
        {
            db.OrganiserStaff.Add(new OrganiserStaff
            {
                Id = Guid.NewGuid(), OrganiserId = orgId, AccountId = organiserAccount,
                Role = OrganiserRole.Owner, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        // A full 5-player team, and a team with only three — the difference the
        // registration rule exists to catch.
        using (var db = Db())
        {
            teamId = Guid.NewGuid();
            thinTeamId = Guid.NewGuid();
            db.Teams.Add(new Team { Id = teamId, Name = $"Full {stamp}", Tag = teamTag, CreatedAt = DateTimeOffset.UtcNow });
            db.Teams.Add(new Team { Id = thinTeamId, Name = $"Thin {stamp}", Tag = thinTag, CreatedAt = DateTimeOffset.UtcNow });

            void Add(Guid team, Guid account, TeamRole role, RosterSlot slot) =>
                db.TeamMemberships.Add(new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = team, AccountId = account, Role = role, Slot = slot,
                    JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });

            Add(teamId, captainAccount, TeamRole.Captain, RosterSlot.Player);
            for (var i = 0; i < 4; i++)
            {
                var filler = new Account
                {
                    Id = Guid.NewGuid(), Email = $"f{i}-{stamp}@nextskill.test", Nickname = $"Fl{stamp}n{i}",
                    PasswordHash = "x", CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Accounts.Add(filler);
                Add(teamId, filler.Id, TeamRole.Member, RosterSlot.Player);
            }

            // Thin team: captain + one player + TWO COACHES. Four people, two
            // fieldable — the case a naive member count gets wrong.
            Add(thinTeamId, captainAccount, TeamRole.Captain, RosterSlot.Player);
            for (var i = 0; i < 3; i++)
            {
                var filler = new Account
                {
                    Id = Guid.NewGuid(), Email = $"c{i}-{stamp}@nextskill.test", Nickname = $"Cf{stamp}n{i}",
                    PasswordHash = "x", CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Accounts.Add(filler);
                Add(thinTeamId, filler.Id, TeamRole.Member, i == 0 ? RosterSlot.Player : RosterSlot.Coach);
            }

            db.SaveChanges();
        }

        Guid tournamentId = default;

        check("tournaments: creating one requires an organiser role", () =>
        {
            var res = asStranger.GetAsync("/pl/tournaments/create").GetAwaiter().GetResult();
            that((int)res.StatusCode == 403,
                $"somebody with no organiser role must not reach the create page, got {(int)res.StatusCode}");
        });

        check("tournaments: a new tournament starts as a PRIVATE draft", () =>
        {
            var res = PostAsync(asOrganiser, "/pl/tournaments/create", new()
            {
                ["Input.Name"] = $"Puchar {stamp}",
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.SingleElimination.ToString(),
                ["Input.MaxTeams"] = "8",
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db(orgId);
            var tournament = db.Tournaments.AsNoTracking().Single(t => t.Name == $"Puchar {stamp}");
            tournamentId = tournament.Id;

            that(tournament.Status == TournamentStatus.Draft,
                "an organiser has to be able to read their own page back before the world does (046)");
        });

        // ── the tenancy boundary: the thing most likely to leak ──────────
        check("tournaments: A DRAFT IS INVISIBLE to the public list and to strangers", () =>
        {
            using var anonymous = Client();

            var list = anonymous.GetAsync("/pl/tournaments").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!list.Contains($"Puchar {stamp}"),
                "a draft must not appear in the public list — a leaked draft costs an organiser's trust (037)");

            var page = anonymous.GetAsync($"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.NotFound,
                $"a stranger asking for a draft by id must get 404, got {(int)page.StatusCode}");

            var loggedIn = asStranger.GetAsync($"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(loggedIn.StatusCode == HttpStatusCode.NotFound,
                "being logged in is not the same as being allowed");

            // The organiser sees it, or the draft would be unusable.
            var owner = asOrganiser.GetAsync($"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(owner.StatusCode == HttpStatusCode.OK,
                $"the organiser must see their own draft, got {(int)owner.StatusCode}");
        });

        check("tournaments: a stranger cannot drive another organiser's tournament", () =>
        {
            var res = PostAsync(asStranger, $"/pl/t/{tournamentId}?handler=Status&to={TournamentStatus.RegistrationOpen}",
                new(), tokenPage: "/pl/tournaments").GetAwaiter().GetResult();

            that((int)res.StatusCode is 403 or 404,
                $"expected a refusal, got {(int)res.StatusCode}");

            using var db = Db(orgId);
            that(db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId).Status == TournamentStatus.Draft,
                "the tournament must be untouched");
        });

        check("tournaments: an ILLEGAL transition is refused even when typed by hand", () =>
        {
            // Draft → Running would build a bracket from a field that was never
            // assembled. The buttons never offer it; the state machine is what
            // actually enforces it (046).
            var res = PostAsync(asOrganiser, $"/pl/t/{tournamentId}?handler=Status&to={TournamentStatus.Running}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "must re-render with an explanation, not perform the jump");

            using var db = Db(orgId);
            that(db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId).Status == TournamentStatus.Draft,
                "still a draft");
        });

        check("tournaments: opening registration makes it public", () =>
        {
            var res = PostAsync(asOrganiser, $"/pl/t/{tournamentId}?handler=Status&to={TournamentStatus.RegistrationOpen}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var anonymous = Client();
            var list = anonymous.GetAsync("/pl/tournaments").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(list.Contains($"Puchar {stamp}"), "an open tournament belongs in the public list");

            var page = anonymous.GetAsync($"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.OK, "and its page must load for anyone");
        });

        // ── the rule that connects rosters to tournaments ────────────────
        check("tournaments: a team WITHOUT ENOUGH FIELDABLE PLAYERS cannot enter", () =>
        {
            // The thin team has four members but two of them are coaches, so a
            // member count would say "four" and be wrong. A 5v5 entered by two
            // players is a forfeit that has not happened yet (045).
            var res = PostAsync(asCaptain, $"/pl/t/{tournamentId}?handler=Enter&teamId={thinTeamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "must be refused with an explanation, not a redirect");

            var body = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(body.Contains("zawodników"), "the refusal must say what is missing");

            using var db = Db(orgId);
            that(!db.Registrations.Any(r => r.TournamentId == tournamentId && r.TeamId == thinTeamId),
                "no registration row may be created");
        });

        check("tournaments: a full team enters, and only its CAPTAIN may do it", () =>
        {
            var refused = PostAsync(asStranger, $"/pl/t/{tournamentId}?handler=Enter&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that((int)refused.StatusCode is 403 or 404,
                $"entering commits a team's time — a stranger must not, got {(int)refused.StatusCode}");

            var res = PostAsync(asCaptain, $"/pl/t/{tournamentId}?handler=Enter&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db(orgId);
            var entry = db.Registrations.AsNoTracking()
                .SingleOrDefault(r => r.TournamentId == tournamentId && r.TeamId == teamId);
            that(entry is not null, "the registration row must exist");
            that(entry!.Seed is null, "seeds do not exist until the field does (046)");
        });

        check("tournaments: entering twice is refused by the DATABASE", () =>
        {
            var res = PostAsync(asCaptain, $"/pl/t/{tournamentId}?handler=Enter&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.OK, "a double submit must not create a second entry");

            using var db = Db(orgId);
            that(db.Registrations.Count(r => r.TournamentId == tournamentId && r.WithdrawnAt == null) == 1,
                "still exactly one entry");
        });

        check("tournaments: closing registration SEEDS the field", () =>
        {
            var res = PostAsync(asOrganiser, $"/pl/t/{tournamentId}?handler=Status&to={TournamentStatus.RegistrationClosed}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db(orgId);
            var seeds = db.Registrations.AsNoTracking()
                .Where(r => r.TournamentId == tournamentId && r.WithdrawnAt == null)
                .Select(r => r.Seed).ToList();

            that(seeds.All(s => s is not null), "every entrant must have a seed once registration closes");
            that(seeds.Distinct().Count() == seeds.Count, "two teams sharing a seed means one disappears from the bracket (023)");
        });

        check("tournaments: a CLOSED tournament takes no more entries", () =>
        {
            var res = PostAsync(asCaptain, $"/pl/t/{tournamentId}?handler=Enter&teamId={thinTeamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.OK, "registration is closed, so this must be refused");

            using var db = Db(orgId);
            that(db.Registrations.Count(r => r.TournamentId == tournamentId && r.WithdrawnAt == null) == 1,
                "the field must not have grown");
        });

        check("tournaments: withdrawing clears the SEEDING, because the field changed", () =>
        {
            // Seeds are positions in a field. Keeping stale ones would show an
            // organiser a bracket shape that no longer holds.
            var res = PostAsync(asCaptain, $"/pl/t/{tournamentId}?handler=Withdraw&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db(orgId);
            var entry = db.Registrations.AsNoTracking()
                .Single(r => r.TournamentId == tournamentId && r.TeamId == teamId);

            that(entry.WithdrawnAt is not null, "withdrawal is soft — the event's history keeps it");
            that(entry.Seed is null, "a stale seed would describe a field that no longer exists");
        });

        check("tournaments: a RUNNING tournament freezes its participants", () =>
        {
            using (var db = Db(orgId))
            {
                // Put a team back in and drive the tournament to Running.
                db.Registrations.Add(new TournamentRegistration
                {
                    Id = Guid.NewGuid(), OrganiserId = orgId, TournamentId = tournamentId,
                    TeamId = teamId, Seed = 1, RegisteredAt = DateTimeOffset.UtcNow,
                });
                db.Tournaments.Single(t => t.Id == tournamentId).Status = TournamentStatus.Running;
                db.SaveChanges();
            }

            var res = PostAsync(asCaptain, $"/pl/t/{tournamentId}?handler=Withdraw&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "withdrawing from a running event must be refused here");

            using var after = Db(orgId);
            that(after.Registrations.AsNoTracking()
                    .Single(r => r.TournamentId == tournamentId && r.TeamId == teamId && r.WithdrawnAt == null) is not null,
                "a team vanishing mid-event would leave matches pointing at a team not in the tournament (023)");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
            db.Database.ExecuteSqlRaw(
                "DELETE FROM registrations WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM tournaments WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM organiser_staff WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM organisers WHERE id IN ({0}, {1}); " +
                "DELETE FROM team_memberships WHERE team_id IN ({2}, {3}); " +
                "DELETE FROM teams WHERE id IN ({2}, {3}); " +
                "DELETE FROM sessions WHERE account_id IN (SELECT id FROM accounts WHERE email LIKE {4}); " +
                "DELETE FROM accounts WHERE email LIKE {4};",
                orgId, rivalOrgId, teamId, thinTeamId, $"%{stamp}@nextskill.test");
    }

    private static string TokenFrom(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\"";
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new Exception("No antiforgery token in the page — Razor Pages should always emit one.");

        var valueAt = html.IndexOf("value=\"", at, StringComparison.Ordinal) + 7;
        return html[valueAt..html.IndexOf('"', valueAt)];
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NextSkill.Api");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate src/NextSkill.Api from " + AppContext.BaseDirectory);
    }
}
