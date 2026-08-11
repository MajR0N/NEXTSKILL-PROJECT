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
        using var factory = TestHost.Create();

        NextSkillDb Db(Guid? tenant = null) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var orgId = Guid.NewGuid();
        var rivalOrgId = Guid.NewGuid();
        const string password = "a-long-enough-passphrase";

        var organiserNick = $"Org{stamp}";
        var captainNick = $"Cpt{stamp}";
        var otherCaptainNick = $"CptB{stamp}";
        var strangerNick = $"Str{stamp}";

        HttpClient Client() => factory.NewClient();

        Task<HttpResponseMessage> PostAsync(
            HttpClient http, string path, Dictionary<string, string> fields, string? tokenPage = null) =>
            http.PostFormAsync(path, fields, tokenPage);

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
        using var asOtherCaptain = SignedInAs(otherCaptainNick);
        using var asStranger = SignedInAs(strangerNick);

        Guid organiserAccount, captainAccount, otherCaptainAccount, teamId, thinTeamId;
        var teamTag = $"TT{stamp % 100000}";
        var thinTag = $"TH{stamp % 100000}";
        var openSeasonId = Guid.NewGuid();
        var alternateSeasonId = Guid.NewGuid();
        var closedSeasonId = Guid.NewGuid();
        var openSeasonName = $"Open {stamp}";
        var alternateSeasonName = $"Alternate {stamp}";

        using (var db = Db())
        {
            organiserAccount = db.Accounts.AsNoTracking().Single(a => a.Nickname == organiserNick).Id;
            captainAccount = db.Accounts.AsNoTracking().Single(a => a.Nickname == captainNick).Id;
            otherCaptainAccount = db.Accounts.AsNoTracking().Single(a => a.Nickname == otherCaptainNick).Id;

            db.Organisers.Add(new Organiser { Id = orgId, Name = $"Org {stamp}", Slug = $"org-{stamp}", CreatedAt = DateTimeOffset.UtcNow });
            db.Organisers.Add(new Organiser { Id = rivalOrgId, Name = $"Rival {stamp}", Slug = $"rival-{stamp}", CreatedAt = DateTimeOffset.UtcNow });
            var nextSeasonNumber = (db.Seasons.Max(s => (int?)s.Number) ?? 0) + 1;
            db.Seasons.AddRange(
                new Season
                {
                    Id = openSeasonId, Number = nextSeasonNumber, Name = openSeasonName,
                    StartsAt = new DateTimeOffset(2029, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    EndsAt = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero), Closed = false,
                },
                new Season
                {
                    Id = alternateSeasonId, Number = nextSeasonNumber + 1, Name = alternateSeasonName,
                    StartsAt = new DateTimeOffset(2029, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    EndsAt = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero), Closed = false,
                },
                new Season
                {
                    Id = closedSeasonId, Number = nextSeasonNumber + 2, Name = $"Closed {stamp}",
                    StartsAt = new DateTimeOffset(2029, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    EndsAt = new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero), Closed = true,
                });
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
        Guid clonedTournamentId = default;
        Guid templateId = default;
        var tournamentName = $"Puchar {stamp}";
        var editedTournamentName = $"Puchar edytowany {stamp}";
        var publishedTournamentName = $"Puchar otwarty {stamp}";
        var templateName = $"Szablon {stamp}";

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
                ["Input.Name"] = tournamentName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.SingleElimination.ToString(),
                ["Input.MaxTeams"] = "8",
                ["Input.SeasonId"] = openSeasonId.ToString(),
                ["Input.StartsAt"] = "2030-09-01",
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db(orgId);
            var tournament = db.Tournaments.AsNoTracking().Single(t => t.Name == tournamentName);
            tournamentId = tournament.Id;

            that(tournament.Status == TournamentStatus.Draft,
                "an organiser has to be able to read their own page back before the world does (046)");
            that(tournament.SeasonId == openSeasonId,
                "the selected open season must be persisted on the new draft");
        });

        check("tournaments: season assignment rejects closed, unknown and out-of-window seasons", () =>
        {
            HttpResponseMessage TryCreate(string name, Guid seasonId, string startsAt) =>
                PostAsync(asOrganiser, "/pl/tournaments/create", new()
                {
                    ["Input.Name"] = name,
                    ["Input.OrganiserId"] = orgId.ToString(),
                    ["Input.Game"] = Game.Cs2.ToString(),
                    ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                    ["Input.Format"] = TournamentFormat.SingleElimination.ToString(),
                    ["Input.MaxTeams"] = "8",
                    ["Input.SeasonId"] = seasonId.ToString(),
                    ["Input.StartsAt"] = startsAt,
                }).GetAwaiter().GetResult();

            var closedName = $"Closed reject {stamp}";
            var unknownName = $"Unknown reject {stamp}";
            var outsideName = $"Outside reject {stamp}";

            that(TryCreate(closedName, closedSeasonId, "2030-06-01").StatusCode == HttpStatusCode.OK,
                "a closed season must re-render with an explanation");
            that(TryCreate(unknownName, Guid.NewGuid(), "2030-06-01").StatusCode == HttpStatusCode.OK,
                "an id omitted from the select must still be validated server-side");
            that(TryCreate(outsideName, openSeasonId, "2040-06-01").StatusCode == HttpStatusCode.OK,
                "a tournament date outside the selected season must be refused");

            using var db = Db(orgId);
            that(!db.Tournaments.Any(t =>
                    t.Name == closedName || t.Name == unknownName || t.Name == outsideName),
                "none of the invalid assignments may create a draft");
        });

        check("tournaments: a dangling season id is refused by the DATABASE", () =>
        {
            var rogueId = Guid.NewGuid();
            var threw = false;
            using (var db = Db(orgId))
            {
                db.Tournaments.Add(new Tournament
                {
                    Id = rogueId, OrganiserId = orgId, Name = $"Rogue season {stamp}",
                    Game = Game.Cs2, TeamSize = TeamSize.Squad,
                    Format = TournamentFormat.SingleElimination, Status = TournamentStatus.Draft,
                    MaxTeams = 8, SeasonId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
                });

                try { db.SaveChanges(); }
                catch (DbUpdateException) { threw = true; }
            }

            that(threw, "season assignment must be referential integrity, not an unchecked Guid");
            using var after = Db(orgId);
            that(!after.Tournaments.Any(t => t.Id == rogueId),
                "the failed foreign-key write must leave no tournament behind");
        });

        // ── the tenancy boundary: the thing most likely to leak ──────────
        check("tournaments: A DRAFT IS INVISIBLE to the public list and to strangers", () =>
        {
            using var anonymous = Client();

            var list = anonymous.GetAsync("/pl/tournaments").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!list.Contains(tournamentName),
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

        check("tournaments: a stranger cannot edit another organiser's setup", () =>
        {
            var page = asStranger.GetAsync($"/pl/t/{tournamentId}/edit").GetAwaiter().GetResult();
            that((int)page.StatusCode is 403 or 404,
                $"a private editor must refuse outsiders, got {(int)page.StatusCode}");

            var post = PostAsync(asStranger, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = "stolen",
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs16.ToString(),
                ["Input.TeamSize"] = TeamSize.Duo.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "32",
            }, tokenPage: "/pl/tournaments").GetAwaiter().GetResult();

            that((int)post.StatusCode is 403 or 404,
                $"a hand-written edit must also be refused, got {(int)post.StatusCode}");

            using var db = Db(orgId);
            that(db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId).Name == tournamentName,
                "the refused edit must change nothing");
        });

        check("tournaments: a DRAFT can edit all executable setup without changing its URL", () =>
        {
            var response = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = editedTournamentName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "10",
                ["Input.Description"] = $"Regulamin {stamp}",
                ["Input.StartsAt"] = "2030-09-01",
                ["Input.SeasonId"] = openSeasonId.ToString(),
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a valid edit should redirect, got {(int)response.StatusCode}");
            that(response.Headers.Location?.OriginalString.Contains(tournamentId.ToString()) == true,
                "editing must preserve the permanent tournament URL");

            using var db = Db(orgId);
            var tournament = db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId);
            that(tournament.Name == editedTournamentName, "the name should be updated");
            that(tournament.Format == TournamentFormat.DoubleElimination, "draft structure should be editable");
            that(tournament.MaxTeams == 10 && tournament.Description == $"Regulamin {stamp}",
                "the repeatable setup should be stored");
            that(tournament.Status == TournamentStatus.Draft, "editing must not publish a draft");
        });

        check("tournaments: editing cannot move a draft into a CLOSED season", () =>
        {
            var response = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = editedTournamentName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "10",
                ["Input.Description"] = $"Regulamin {stamp}",
                ["Input.StartsAt"] = "2030-09-01",
                ["Input.SeasonId"] = closedSeasonId.ToString(),
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                "a closed-season assignment should re-render with an explanation");
            using var db = Db(orgId);
            that(db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId).SeasonId == openSeasonId,
                "the rejected edit must preserve the previous open-season assignment");
        });

        check("tournaments: saving a named template is case-insensitively unique", () =>
        {
            var saved = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit?handler=SaveTemplate", new()
            {
                ["TemplateName"] = templateName,
            }, tokenPage: $"/pl/t/{tournamentId}/edit").GetAwaiter().GetResult();

            that(saved.StatusCode == HttpStatusCode.Redirect,
                $"saving a template should redirect, got {(int)saved.StatusCode}");

            using (var db = Db(orgId))
            {
                var template = db.TournamentTemplates.AsNoTracking().Single(t => t.Name == templateName);
                templateId = template.Id;
                that(template.Format == TournamentFormat.DoubleElimination && template.MaxTeams == 10,
                    "a template must snapshot the source setup");
                that(template.Description == $"Regulamin {stamp}", "repeatable content should be copied");
            }

            var duplicate = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit?handler=SaveTemplate", new()
            {
                ["TemplateName"] = templateName.ToUpperInvariant(),
            }, tokenPage: $"/pl/t/{tournamentId}/edit").GetAwaiter().GetResult();

            that(duplicate.StatusCode == HttpStatusCode.OK,
                "a duplicate template name should re-render with an explanation");
            using var after = Db(orgId);
            that(after.TournamentTemplates.Count(t => t.NameKey == templateName.ToLowerInvariant()) == 1,
                "case changes must not create indistinguishable template names");
        });

        check("tournaments: a template prefills an independent new draft", () =>
        {
            var picker = asOrganiser.GetAsync($"/pl/tournaments/create?templateId={templateId}")
                .GetAwaiter().GetResult();
            var body = picker.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(picker.StatusCode == HttpStatusCode.OK, "an organiser should be able to open their template");
            that(body.Contains(templateName) && body.Contains($"Regulamin {stamp}"),
                "the selected template should prefill its repeatable setup");
            that(body.Contains("Skopiowano ustawienia"),
                "the page must explain that this is a copy, not a live link");

            var cloneName = $"Kopia {stamp}";
            var created = PostAsync(asOrganiser, "/pl/tournaments/create", new()
            {
                ["Input.Name"] = cloneName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "10",
                ["Input.Description"] = $"Regulamin {stamp}",
            }).GetAwaiter().GetResult();
            that(created.StatusCode == HttpStatusCode.Redirect, "the prefilled setup should create a draft");

            using (var db = Db(orgId))
            {
                var clone = db.Tournaments.AsNoTracking().Single(t => t.Name == cloneName);
                clonedTournamentId = clone.Id;
                that(clone.Status == TournamentStatus.Draft && clone.Format == TournamentFormat.DoubleElimination,
                    "cloning creates an ordinary private draft with the copied values");
                that(clone.StartsAt is null && clone.SeasonId is null,
                    "an absolute event date or season must not leak through a reusable template");
            }

            var changedClone = PostAsync(asOrganiser, $"/pl/t/{clonedTournamentId}/edit", new()
            {
                ["Input.Name"] = cloneName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "6",
                ["Input.Description"] = "Tylko kopia",
            }).GetAwaiter().GetResult();
            that(changedClone.StatusCode == HttpStatusCode.Redirect, "the cloned draft should be editable normally");

            using var after = Db(orgId);
            that(after.TournamentTemplates.AsNoTracking().Single(t => t.Id == templateId).MaxTeams == 10,
                "editing a clone must not mutate its template");
            that(after.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId).MaxTeams == 10,
                "editing a clone must not mutate the tournament that produced the template");
        });

        check("tournaments: only the owning organiser can delete a template", () =>
        {
            var refused = PostAsync(asStranger,
                $"/pl/tournaments/create?handler=DeleteTemplate&templateId={templateId}",
                new(), tokenPage: "/pl/tournaments").GetAwaiter().GetResult();
            that((int)refused.StatusCode is 403 or 404,
                $"an outsider must not delete the template, got {(int)refused.StatusCode}");

            using (var db = Db(orgId))
                that(db.TournamentTemplates.Any(t => t.Id == templateId), "the template must survive the refusal");

            var deleted = PostAsync(asOrganiser,
                $"/pl/tournaments/create?handler=DeleteTemplate&templateId={templateId}",
                new(), tokenPage: "/pl/tournaments/create").GetAwaiter().GetResult();
            that(deleted.StatusCode == HttpStatusCode.Redirect, "the owning organiser may delete it");

            using var after = Db(orgId);
            that(!after.TournamentTemplates.Any(t => t.Id == templateId), "the deleted template must be gone");
        });

        check("tournaments: opening registration makes it public", () =>
        {
            var res = PostAsync(asOrganiser, $"/pl/t/{tournamentId}?handler=Status&to={TournamentStatus.RegistrationOpen}",
                new(), tokenPage: $"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var anonymous = Client();
            var list = anonymous.GetAsync("/pl/tournaments").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(list.Contains(editedTournamentName), "an open tournament belongs in the public list");

            var page = anonymous.GetAsync($"/pl/t/{tournamentId}").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.OK, "and its page must load for anyone");
            var body = page.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(body.Contains(openSeasonName),
                "the public tournament page must show the season the organiser assigned");
        });

        // ── the rule that connects rosters to tournaments ────────────────
        check("tournaments: publishing freezes structure but not descriptive setup", () =>
        {
            var tampered = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = "must not land",
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs16.ToString(),
                ["Input.TeamSize"] = TeamSize.Duo.ToString(),
                ["Input.Format"] = TournamentFormat.SingleElimination.ToString(),
                ["Input.MaxTeams"] = "9",
                ["Input.Description"] = "must not land",
            }).GetAwaiter().GetResult();

            that(tampered.StatusCode == HttpStatusCode.OK,
                "a hand-written structural change should re-render with an explanation");

            using (var db = Db(orgId))
            {
                var unchanged = db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId);
                that(unchanged.Name == editedTournamentName, "an invalid form must not partially save details");
                that(unchanged.Game == Game.Cs2 && unchanged.TeamSize == TeamSize.Squad &&
                     unchanged.Format == TournamentFormat.DoubleElimination,
                    "published competitive structure must remain unchanged");
            }

            var details = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = publishedTournamentName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "9",
                ["Input.Description"] = $"Opis publiczny {stamp}",
                ["Input.StartsAt"] = "2030-10-02",
                ["Input.SeasonId"] = alternateSeasonId.ToString(),
            }).GetAwaiter().GetResult();

            that(details.StatusCode == HttpStatusCode.Redirect,
                "name, schedule, capacity and description should remain editable before start");

            using var after = Db(orgId);
            var updated = after.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId);
            that(updated.Name == publishedTournamentName && updated.MaxTeams == 9,
                "descriptive setup and capacity should be updated");
            that(updated.SeasonId == alternateSeasonId,
                "an open season assignment may change until the tournament starts");
            that(updated.Format == TournamentFormat.DoubleElimination,
                "editing details must not disturb frozen structure");
        });

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

        check("tournaments: capacity cannot be reduced below the active field", () =>
        {
            var response = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = publishedTournamentName,
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs2.ToString(),
                ["Input.TeamSize"] = TeamSize.Squad.ToString(),
                ["Input.Format"] = TournamentFormat.DoubleElimination.ToString(),
                ["Input.MaxTeams"] = "0",
                ["Input.Description"] = $"Opis publiczny {stamp}",
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                "shrinking below the field should re-render rather than discard entrants");

            using var db = Db(orgId);
            var tournament = db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId);
            that(tournament.MaxTeams == 9, "the invalid capacity must not be stored");
            that(db.Registrations.Count(r => r.TournamentId == tournamentId && r.WithdrawnAt == null) == 1,
                "editing capacity must never remove an existing registration");
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

        check("tournaments: a RUNNING tournament's published setup is immutable", () =>
        {
            var response = PostAsync(asOrganiser, $"/pl/t/{tournamentId}/edit", new()
            {
                ["Input.Name"] = "rewritten history",
                ["Input.OrganiserId"] = orgId.ToString(),
                ["Input.Game"] = Game.Cs16.ToString(),
                ["Input.TeamSize"] = TeamSize.Duo.ToString(),
                ["Input.Format"] = TournamentFormat.SingleElimination.ToString(),
                ["Input.MaxTeams"] = "128",
            }).GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.OK,
                "the editor should explain the freeze without mutating the event");

            using var db = Db(orgId);
            var tournament = db.Tournaments.AsNoTracking().Single(t => t.Id == tournamentId);
            that(tournament.Name == publishedTournamentName && tournament.MaxTeams == 9,
                "running setup is event history and must remain unchanged");
            that(tournament.Game == Game.Cs2 && tournament.Format == TournamentFormat.DoubleElimination,
                "competitive structure must remain unchanged too");
        });

        Guid checkInTournamentId = default;
        Guid checkInMatchId = default;
        Guid removableMembershipId = default;

        check("tournaments: start waits for every captain's fresh CHECK-IN", () =>
        {
            using (var db = Db())
            {
                // Give the second team its own captain now that the earlier
                // registration-size checks have exercised the thin roster.
                var formerCaptain = db.TeamMemberships.Single(m =>
                    m.TeamId == thinTeamId && m.AccountId == captainAccount && m.LeftAt == null);
                formerCaptain.Role = TeamRole.Member;

                void Add(Guid accountId) => db.TeamMemberships.Add(new TeamMembership
                {
                    Id = Guid.NewGuid(), TeamId = thinTeamId, AccountId = accountId,
                    Role = accountId == otherCaptainAccount ? TeamRole.Captain : TeamRole.Member,
                    Slot = RosterSlot.Player, JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });

                Add(otherCaptainAccount);
                for (var i = 0; i < 2; i++)
                {
                    var filler = new Account
                    {
                        Id = Guid.NewGuid(), Email = $"ready{i}-{stamp}@nextskill.test",
                        Nickname = $"Ready{stamp}n{i}", PasswordHash = "x", CreatedAt = DateTimeOffset.UtcNow,
                    };
                    db.Accounts.Add(filler);
                    Add(filler.Id);
                    if (i == 0)
                        removableMembershipId = db.TeamMemberships.Local.Last().Id;
                }

                checkInTournamentId = Guid.NewGuid();
                db.Tournaments.Add(new Tournament
                {
                    Id = checkInTournamentId, OrganiserId = orgId, Name = $"Check-in {stamp}",
                    Game = Game.Cs2, Format = TournamentFormat.SingleElimination,
                    TeamSize = TeamSize.Squad, MaxTeams = 2,
                    Status = TournamentStatus.RegistrationClosed, SeasonId = openSeasonId,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                db.Registrations.AddRange(
                    new TournamentRegistration
                    {
                        Id = Guid.NewGuid(), OrganiserId = orgId, TournamentId = checkInTournamentId,
                        TeamId = teamId, Seed = 1, RegisteredAt = DateTimeOffset.UtcNow,
                    },
                    new TournamentRegistration
                    {
                        Id = Guid.NewGuid(), OrganiserId = orgId, TournamentId = checkInTournamentId,
                        TeamId = thinTeamId, Seed = 2, RegisteredAt = DateTimeOffset.UtcNow,
                    });
                db.SaveChanges();
            }

            var blocked = PostAsync(asOrganiser,
                $"/pl/t/{checkInTournamentId}?handler=Status&to={TournamentStatus.Running}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();
            that(blocked.StatusCode == HttpStatusCode.OK,
                "an unchecked field should re-render with an explanation");

            using var after = Db(orgId);
            that(after.Tournaments.AsNoTracking().Single(t => t.Id == checkInTournamentId).Status ==
                 TournamentStatus.RegistrationClosed,
                "the lifecycle must remain closed while confirmations are missing");
            that(!after.Matches.Any(m => m.TournamentId == checkInTournamentId),
                "the refused start must not leave a partial bracket");
        });

        check("tournaments: only each team's CAPTAIN may check it in", () =>
        {
            var refused = PostAsync(asStranger,
                $"/pl/t/{checkInTournamentId}?handler=CheckIn&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();
            that((int)refused.StatusCode is 403 or 404,
                $"an outsider must not confirm a team's commitment, got {(int)refused.StatusCode}");

            var full = PostAsync(asCaptain,
                $"/pl/t/{checkInTournamentId}?handler=CheckIn&teamId={teamId}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();
            var thin = PostAsync(asOtherCaptain,
                $"/pl/t/{checkInTournamentId}?handler=CheckIn&teamId={thinTeamId}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();

            that(full.StatusCode == HttpStatusCode.Redirect && thin.StatusCode == HttpStatusCode.Redirect,
                "both captains should be able to confirm their own fieldable teams");

            using var db = Db(orgId);
            that(db.Registrations.AsNoTracking()
                    .Where(r => r.TournamentId == checkInTournamentId)
                    .All(r => r.CheckedInAt != null),
                "check-in timestamps are persisted for the exact entrants");
        });

        check("tournaments: a CLOSED season cannot receive a newly started tournament", () =>
        {
            using (var db = Db())
            {
                db.Seasons.Single(s => s.Id == openSeasonId).Closed = true;
                db.SaveChanges();
            }

            var blocked = PostAsync(asOrganiser,
                $"/pl/t/{checkInTournamentId}?handler=Status&to={TournamentStatus.Running}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();
            that(blocked.StatusCode == HttpStatusCode.OK,
                "starting inside frozen archive history must be refused with an explanation");

            using (var db = Db(orgId))
            {
                that(db.Tournaments.AsNoTracking().Single(t => t.Id == checkInTournamentId).Status ==
                     TournamentStatus.RegistrationClosed,
                    "the closed season must not gain a running tournament");
                that(!db.Matches.Any(m => m.TournamentId == checkInTournamentId),
                    "the refused start must not create a bracket");
            }

            // The season-management slice will enforce closure against live
            // tournaments. Reopen this test fixture so the roster-start checks
            // can continue exercising the same event.
            using (var db = Db())
            {
                db.Seasons.Single(s => s.Id == openSeasonId).Closed = false;
                db.SaveChanges();
            }
        });

        check("tournaments: start REVALIDATES rosters and builds the bracket atomically", () =>
        {
            using (var db = Db())
            {
                db.TeamMemberships.Single(m => m.Id == removableMembershipId).LeftAt = DateTimeOffset.UtcNow;
                db.SaveChanges();
            }

            var blocked = PostAsync(asOrganiser,
                $"/pl/t/{checkInTournamentId}?handler=Status&to={TournamentStatus.Running}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();
            that(blocked.StatusCode == HttpStatusCode.OK,
                "a roster that shrank after check-in must block start");

            using (var db = Db(orgId))
            {
                that(db.Tournaments.AsNoTracking().Single(t => t.Id == checkInTournamentId).Status ==
                     TournamentStatus.RegistrationClosed, "the failed revalidation must not move lifecycle state");
                that(!db.Matches.Any(m => m.TournamentId == checkInTournamentId),
                    "the failed revalidation must not write any bracket rows");
            }

            using (var db = Db())
            {
                db.TeamMemberships.Single(m => m.Id == removableMembershipId).LeftAt = null;
                db.SaveChanges();
            }

            var started = PostAsync(asOrganiser,
                $"/pl/t/{checkInTournamentId}?handler=Status&to={TournamentStatus.Running}",
                new(), tokenPage: $"/pl/t/{checkInTournamentId}").GetAwaiter().GetResult();
            that(started.StatusCode == HttpStatusCode.Redirect,
                "a fully checked-in, fieldable tournament should start");

            using var after = Db(orgId);
            that(after.Tournaments.AsNoTracking().Single(t => t.Id == checkInTournamentId).Status ==
                 TournamentStatus.Running, "start and bracket creation should land together");
            var match = after.Matches.AsNoTracking().Single(m => m.TournamentId == checkInTournamentId);
            checkInMatchId = match.Id;
            that(match.TeamAId is not null && match.TeamBId is not null &&
                 match.SlotAResolved && match.SlotBResolved,
                "the first match should be immediately actionable");
        });

        check("tournaments: a captain cannot FORFEIT the opponent by tampering with the form", () =>
        {
            var path = $"/pl/t/{checkInTournamentId}/matches/{checkInMatchId}/forfeit";
            var refused = PostAsync(asCaptain, path, new()
            {
                ["LosingTeamId"] = thinTeamId.ToString(),
                ["Reason"] = "Tampered request",
                ["ConfirmTag"] = thinTag,
            }, tokenPage: path).GetAwaiter().GetResult();

            that((int)refused.StatusCode is 403 or 404,
                $"a captain may exit only their own team, got {(int)refused.StatusCode}");
            using var db = Db(orgId);
            that(db.Matches.AsNoTracking().Single(m => m.Id == checkInMatchId).WinnerTeamId is null,
                "the opponent-forfeit attempt must change nothing");
        });

        check("tournaments: FORFEIT is audited, scoreless and removes the team from the event", () =>
        {
            var path = $"/pl/t/{checkInTournamentId}/matches/{checkInMatchId}/forfeit";
            var wrongTag = PostAsync(asOtherCaptain, path, new()
            {
                ["LosingTeamId"] = thinTeamId.ToString(),
                ["Reason"] = "Roster unavailable",
                ["ConfirmTag"] = "WRONG",
            }, tokenPage: path).GetAwaiter().GetResult();
            that(wrongTag.StatusCode == HttpStatusCode.OK,
                "the destructive action requires the team's exact tag");

            int ratingCountBefore;
            using (var before = Db())
                ratingCountBefore = before.Ratings.Count(r => r.TeamId == teamId || r.TeamId == thinTeamId);

            var accepted = PostAsync(asOtherCaptain, path, new()
            {
                ["LosingTeamId"] = thinTeamId.ToString(),
                ["Reason"] = "Roster unavailable",
                ["ConfirmTag"] = thinTag,
            }, tokenPage: path).GetAwaiter().GetResult();
            that(accepted.StatusCode == HttpStatusCode.Redirect,
                $"the team's captain should be able to record the exit, got {(int)accepted.StatusCode}");

            using var db = Db(orgId);
            var match = db.Matches.AsNoTracking().Single(m => m.Id == checkInMatchId);
            var entry = db.Registrations.AsNoTracking().Single(r =>
                r.TournamentId == checkInTournamentId && r.TeamId == thinTeamId);

            that(match.Status == MatchStatus.Forfeited && match.ForfeitedTeamId == thinTeamId &&
                 match.WinnerTeamId == teamId,
                "the opponent advances and the administrative result is explicit");
            that(match.ScoreA is null && match.ScoreB is null,
                "a forfeit must not invent a competitive score");
            that(match.ForfeitedByAccountId == otherCaptainAccount &&
                 match.ForfeitReason == "Roster unavailable",
                "the match records who made the decision and why");
            that(entry.ForfeitedAt is not null && entry.ForfeitedByAccountId == otherCaptainAccount,
                "the registration records that the team left the whole event");
            that(db.Ratings.Count(r => r.TeamId == teamId || r.TeamId == thinTeamId) == ratingCountBefore,
                "an administrative win must never move either team's rating");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
            db.Database.ExecuteSqlRaw(
                "UPDATE matches SET winner_to_match_id = NULL, loser_to_match_id = NULL WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM matches WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM tournament_templates WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM registrations WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM tournaments WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM seasons WHERE id IN ({5}, {6}, {7}); " +
                "DELETE FROM organiser_staff WHERE organiser_id IN ({0}, {1}); " +
                "DELETE FROM organisers WHERE id IN ({0}, {1}); " +
                "DELETE FROM team_memberships WHERE team_id IN ({2}, {3}); " +
                "DELETE FROM teams WHERE id IN ({2}, {3}); " +
                "DELETE FROM sessions WHERE account_id IN (SELECT id FROM accounts WHERE email LIKE {4}); " +
                "DELETE FROM accounts WHERE email LIKE {4};",
                orgId, rivalOrgId, teamId, thinTeamId, $"%{stamp}@nextskill.test",
                openSeasonId, alternateSeasonId, closedSeasonId);
    }


}
