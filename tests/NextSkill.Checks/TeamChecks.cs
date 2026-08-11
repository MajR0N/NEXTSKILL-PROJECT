using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Teams;

namespace NextSkill.Checks;

/// <summary>
/// Teams, invites and rosters (decisions 021, 036, 008).
///
/// The failures worth catching here are the quiet ones: a pending invite that
/// counts as a player, a non-captain who can change the roster anyway, a team
/// left with no captain and therefore no way to confirm a result.
/// </summary>
public static class TeamChecks
{
    private sealed class NoTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => null;
        public Guid RequireOrganiserId() => throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        using var factory = new WebApplicationFactory<NextSkillDb>()
            .WithWebHostBuilder(b => b
                .UseEnvironment("Development")
                .UseContentRoot(FindApiContentRoot()));

        NextSkillDb Db() => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new NoTenant());

        var stamp = DateTimeOffset.UtcNow.Ticks;
        var captain = $"Cap{stamp}";
        var player = $"Ply{stamp}";
        var outsider = $"Out{stamp}";
        const string password = "a-long-enough-passphrase";

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

        using var asCaptain = SignedInAs(captain);
        using var asPlayer = SignedInAs(player);
        using var asOutsider = SignedInAs(outsider);

        Guid teamId = default;
        var tag = $"T{stamp % 1000000}";   // reassigned when the team is re-tagged

        // Resolved once, as plain locals: EF cannot translate a call to a local
        // function inside a query expression.
        Guid captainId, playerId, outsiderId;
        using (var db = Db())
        {
            captainId = db.Accounts.AsNoTracking().Single(a => a.Nickname == captain).Id;
            playerId = db.Accounts.AsNoTracking().Single(a => a.Nickname == player).Id;
            outsiderId = db.Accounts.AsNoTracking().Single(a => a.Nickname == outsider).Id;
        }

        check("teams: the creator becomes CAPTAIN in the same transaction", () =>
        {
            var res = PostAsync(asCaptain, "/pl/teams/create", new()
            {
                ["Input.Name"] = $"Test {stamp}",
                ["Input.Tag"] = tag,
                ["Input.City"] = "Kraków",
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db();
            var team = db.Teams.AsNoTracking().SingleOrDefault(t => t.Tag == tag);
            that(team is not null, "the team row must exist");
            teamId = team!.Id;

            var membership = db.TeamMemberships.AsNoTracking()
                .SingleOrDefault(m => m.TeamId == teamId && m.AccountId == captainId);

            that(membership is not null, "the creator must be on the roster");
            that(membership!.Role == TeamRole.Captain,
                "a team with no captain cannot invite, confirm or dispute — it is stuck and needs staff to fix");
            that(membership.JoinedAt is not null, "the founder does not invite themselves");
        });

        check("teams: creating a team requires a login", () =>
        {
            using var anonymous = Client();
            var res = anonymous.GetAsync("/pl/teams/create").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect to login, got {(int)res.StatusCode}");
            that(res.Headers.Location?.OriginalString.Contains("/account/login") == true,
                $"should redirect to the login page, went to {res.Headers.Location}");
        });

        check("teams: a duplicate TAG is refused", () =>
        {
            var res = PostAsync(asPlayer, "/pl/teams/create", new()
            {
                ["Input.Name"] = "Kopia",
                ["Input.Tag"] = tag.ToUpperInvariant(),
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "must re-render the form rather than create a second team");

            using var db = Db();
            that(db.Teams.Count(t => t.Tag.ToLower() == tag.ToLower()) == 1, "still exactly one team with this tag");
        });

        // ── the one most likely to be missing ────────────────────────────
        check("teams: a PENDING invite is not a roster member", () =>
        {
            var res = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Invite", new()
            {
                ["InviteNickname"] = player,
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"invite should redirect, got {(int)res.StatusCode}");

            using var db = Db();
            var invite = db.TeamMemberships.AsNoTracking()
                .SingleOrDefault(m => m.TeamId == teamId && m.AccountId == playerId);

            that(invite is not null, "an invite is a pending membership row");
            that(invite!.JoinedAt is null,
                "JoinedAt must stay null — an invited player is NOT on the roster and must not be fieldable");

            // And it must confer no powers at all.
            var canManage = AccessPolicy.Can(
                new Actor(playerId), Operation.ManageRoster, new ResourceRef(TeamId: teamId));
            that(!canManage, "a pending invite must grant no roster rights");

            that(db.Notifications.Any(n => n.AccountId == playerId && n.Type == NotificationType.TeamInvite),
                "the invited player must be notified, or the invite is invisible (043)");
        });

        check("teams: inviting the same player twice is refused by the DATABASE", () =>
        {
            var res = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Invite", new()
            {
                ["InviteNickname"] = player,
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "a duplicate invite must re-render, not redirect");

            using var db = Db();
            that(db.TeamMemberships.Count(m => m.TeamId == teamId && m.AccountId == playerId && m.LeftAt == null) == 1,
                "the partial unique index is what makes two captains clicking at once safe (008)");
        });

        check("teams: a NON-CAPTAIN cannot change the roster", () =>
        {
            var res = PostAsync(asOutsider, $"/pl/teams/{teamId}?handler=Invite", new()
            {
                ["InviteNickname"] = outsider,
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that((int)res.StatusCode is 403 or 302,
                $"a stranger must not be able to invite, got {(int)res.StatusCode}");

            using var db = Db();
            that(!db.TeamMemberships.Any(m => m.TeamId == teamId && m.AccountId == outsiderId),
                "no membership row may be created by someone without ManageRoster");
        });

        check("teams: accepting an invite sets JoinedAt and notifies the captain", () =>
        {
            var res = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Respond&accept=true", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db();
            var membership = db.TeamMemberships.AsNoTracking()
                .Single(m => m.TeamId == teamId && m.AccountId == playerId);

            that(membership.JoinedAt is not null, "accepting must set JoinedAt");
            that(db.Notifications.Any(n => n.AccountId == captainId && n.Type == NotificationType.TeamInviteAccepted),
                "the captain should learn their invite was accepted");
        });

        check("teams: a player cannot accept an invite that was never sent to them", () =>
        {
            var res = PostAsync(asOutsider, $"/pl/teams/{teamId}?handler=Respond&accept=true", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that((int)res.StatusCode == 404, $"there is no invite to answer, got {(int)res.StatusCode}");

            using var db = Db();
            that(!db.TeamMemberships.Any(m => m.TeamId == teamId && m.AccountId == outsiderId),
                "answering a non-existent invite must not join the team");
        });

        check("teams: THE LAST CAPTAIN CANNOT LEAVE", () =>
        {
            // A captainless team cannot invite, confirm a result or dispute one.
            // Refusing here is cheaper than the support ticket that follows.
            var res = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Leave", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.OK, "must re-render with an explanation, not silently succeed");

            using var db = Db();
            that(db.TeamMemberships.AsNoTracking()
                    .Single(m => m.TeamId == teamId && m.AccountId == captainId).LeftAt is null,
                "the last captain must still be on the roster");
        });

        check("teams: leaving sets LeftAt, and the same player can be re-invited", () =>
        {
            var left = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Leave", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(left.StatusCode == HttpStatusCode.Redirect, $"leaving should redirect, got {(int)left.StatusCode}");

            using (var db = Db())
            {
                var membership = db.TeamMemberships.AsNoTracking()
                    .Single(m => m.TeamId == teamId && m.AccountId == playerId);
                that(membership.LeftAt is not null, "leaving records LeftAt rather than deleting the row");
                that(membership.JoinedAt is not null,
                    "history must keep saying they played — deleting the row would rewrite match history (008)");
            }

            // The partial unique index ignores departed rows on purpose.
            var reinvite = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Invite", new()
            {
                ["InviteNickname"] = player,
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that(reinvite.StatusCode == HttpStatusCode.Redirect,
                $"a player who left must be re-invitable, got {(int)reinvite.StatusCode}");

            using (var db = Db())
                that(db.TeamMemberships.Count(m => m.TeamId == teamId && m.AccountId == playerId) == 2,
                    "the old row stays as history and a new pending row is created");
        });

        check("teams: the public team page shows the roster and NOT the pending invites as players", () =>
        {
            using var anonymous = Client();
            var html = anonymous.GetAsync($"/pl/teams/{teamId}").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(html.Contains(captain), "the captain should be listed");
            that(html.Contains("Kapitan"), "roles should be visible");
            that(!html.Contains("Zaproś gracza"), "a stranger must not see the invite form");
            that(!html.Contains("Usuń"), "a stranger must not see remove buttons");
        });

        check("teams: an unknown team id is 404, not an error that confirms anything", () =>
        {
            using var anonymous = Client();
            var res = anonymous.GetAsync($"/pl/teams/{Guid.NewGuid()}").GetAwaiter().GetResult();
            that(res.StatusCode == HttpStatusCode.NotFound, $"expected 404, got {(int)res.StatusCode}");
        });

        // ── the trap the previous check only half-closed ─────────────────
        check("teams: the last captain has a WAY OUT — promote, then leave", () =>
        {
            // "The last captain cannot leave" is only correct if handing the
            // captaincy over is actually possible. Without a promote handler
            // that rule is not a safeguard, it is a permanent trap, and the
            // error message telling them to hand over points at nothing.

            // A pending invite must not be promotable: an invite nobody accepted
            // could otherwise be handed control of the team.
            var tooEarly = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Promote&accountId={playerId}", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that((int)tooEarly.StatusCode == 404,
                $"a pending invite must not be promotable, got {(int)tooEarly.StatusCode}");

            PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Respond&accept=true", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            var promoted = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Promote&accountId={playerId}", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(promoted.StatusCode == HttpStatusCode.Redirect,
                $"promoting an accepted member should succeed, got {(int)promoted.StatusCode}");

            using (var db = Db())
                that(db.TeamMemberships.AsNoTracking()
                        .Single(m => m.TeamId == teamId && m.AccountId == playerId && m.LeftAt == null)
                        .Role == TeamRole.Captain,
                    "the promoted member must actually be a captain");

            // And NOW the founder can go.
            var left = PostAsync(asCaptain, $"/pl/teams/{teamId}?handler=Leave", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(left.StatusCode == HttpStatusCode.Redirect,
                $"with a second captain in place the founder may leave, got {(int)left.StatusCode}");

            using (var db = Db())
            {
                that(!db.TeamMemberships.Any(m => m.TeamId == teamId && m.AccountId == captainId && m.LeftAt == null),
                    "the founder should no longer have an active membership");

                that(db.TeamMemberships.Any(m =>
                        m.TeamId == teamId && m.Role == TeamRole.Captain && m.LeftAt == null && m.JoinedAt != null),
                    "THE TEAM MUST NEVER END UP WITH NO CAPTAIN");
            }
        });

        // ── roster cap (045) ─────────────────────────────────────────────
        check("teams: each slot caps separately, and PENDING INVITES occupy a place", () =>
        {
            // 5 players + 3 substitutes + 2 coaches. The caps are per slot, so
            // "the roster is not full" is not the same question as "there is
            // room for another starter".
            var filler = new List<HttpClient>();
            var n = 0;

            HttpResponseMessage Invite(RosterSlot slot)
            {
                var nickname = $"Fil{stamp}x{n++}";
                filler.Add(SignedInAs(nickname));
                return PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Invite", new()
                {
                    ["InviteNickname"] = nickname,
                    ["InviteSlot"] = slot.ToString(),
                }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            }

            try
            {
                foreach (var slot in Enum.GetValues<RosterSlot>())
                {
                    int occupied;
                    using (var db = Db())
                        occupied = db.TeamMemberships.Count(m => m.TeamId == teamId && m.LeftAt == null && m.Slot == slot);

                    var free = RosterRules.Capacity(slot) - occupied;
                    that(free >= 0, $"{slot} was already over capacity: {occupied}/{RosterRules.Capacity(slot)}");

                    for (var i = 0; i < free; i++)
                        that(Invite(slot).StatusCode == HttpStatusCode.Redirect,
                            $"{slot}: invite {i + 1} of {free} should be accepted");

                    // One past the cap. Every place here is held by an invite
                    // nobody has answered — if the cap counted accepted members
                    // only, this would sail through, and a full roster could
                    // spam two hundred people (040).
                    var overflow = Invite(slot);
                    that(overflow.StatusCode == HttpStatusCode.OK,
                        $"{slot}: place {RosterRules.Capacity(slot) + 1} must be refused, got {(int)overflow.StatusCode}");

                    using var after = Db();
                    var count = after.TeamMemberships.Count(m => m.TeamId == teamId && m.LeftAt == null && m.Slot == slot);
                    that(count == RosterRules.Capacity(slot),
                        $"{slot}: expected exactly {RosterRules.Capacity(slot)} places filled, found {count}");
                }

                using var db2 = Db();
                var total = db2.TeamMemberships.Count(m => m.TeamId == teamId && m.LeftAt == null);
                that(total == RosterRules.MaxTotal,
                    $"a full team is {RosterRules.MaxTotal} places (5 + 3 + 2), found {total}");
            }
            finally
            {
                foreach (var c in filler) c.Dispose();
            }
        });

        check("teams: a member can be MOVED between slots, and the destination cap still applies", () =>
        {
            // Benching a starter and fielding a substitute is the whole point of
            // having a bench. Without it a one-week absence would have to be a
            // removal, which loses the roster history (045).
            Guid starter, substitute;
            using (var db = Db())
            {
                starter = db.TeamMemberships
                    .First(m => m.TeamId == teamId && m.LeftAt == null && m.Slot == RosterSlot.Player && m.AccountId != playerId).AccountId;
                substitute = db.TeamMemberships
                    .First(m => m.TeamId == teamId && m.LeftAt == null && m.Slot == RosterSlot.Substitute).AccountId;
            }

            // Players are full, so promoting a substitute must be refused until
            // a place is freed. Six starters arrived at by promotion is still
            // six starters.
            var blocked = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Slot&accountId={substitute}&slot={RosterSlot.Player}",
                new(), tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(blocked.StatusCode == HttpStatusCode.OK,
                $"the destination cap must apply to a move, got {(int)blocked.StatusCode}");

            // Bench the starter, then field the substitute.
            var benched = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Slot&accountId={starter}&slot={RosterSlot.Substitute}",
                new(), tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(benched.StatusCode == HttpStatusCode.OK,
                "substitutes are full too, so benching must also be refused while there is no room");

            using (var db = Db())
            {
                // Free one substitute place by removing the person we intend to field.
                that(db.TeamMemberships.Count(m => m.TeamId == teamId && m.LeftAt == null && m.Slot == RosterSlot.Substitute)
                     == RosterRules.MaxSubstitutes, "substitutes should be full at this point");
            }

            PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Remove&accountId={substitute}", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            var moved = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Slot&accountId={starter}&slot={RosterSlot.Substitute}",
                new(), tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(moved.StatusCode == HttpStatusCode.Redirect,
                $"with a place free, benching a starter must work, got {(int)moved.StatusCode}");

            using var after = Db();
            that(after.TeamMemberships.Single(m => m.TeamId == teamId && m.AccountId == starter && m.LeftAt == null)
                    .Slot == RosterSlot.Substitute,
                "the moved member must actually be on the bench");
            that(after.Notifications.Any(nn => nn.AccountId == starter && nn.Type == NotificationType.RosterChanged),
                "being benched is news — the player should be told (043)");
        });

        check("teams: a COACH is never fieldable, and the page says so", () =>
        {
            // The rule that makes a coach a real thing rather than a label. A
            // lineup that could include the coach would be a 5v5 played 4v5,
            // discovered at map start (011, 035).
            that(!RosterRules.IsFieldable(RosterSlot.Coach), "a coach must never take a place in a match");
            that(RosterRules.IsFieldable(RosterSlot.Player), "starters play");
            that(RosterRules.IsFieldable(RosterSlot.Substitute), "a bench that cannot be fielded is not a bench");

            // Every staff place is currently held by an unanswered invite, so
            // free one and put a real coach on the roster.
            Guid pendingCoach;
            using (var db = Db())
                pendingCoach = db.TeamMemberships
                    .First(m => m.TeamId == teamId && m.LeftAt == null && m.Slot == RosterSlot.Coach).AccountId;

            PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Remove&accountId={pendingCoach}", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            var coachNick = $"Coach{stamp}";
            using var asCoach = SignedInAs(coachNick);

            var invited = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Invite", new()
            {
                ["InviteNickname"] = coachNick,
                ["InviteSlot"] = RosterSlot.Coach.ToString(),
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(invited.StatusCode == HttpStatusCode.Redirect,
                $"a freed staff place should accept an invite, got {(int)invited.StatusCode}");

            PostAsync(asCoach, $"/pl/teams/{teamId}?handler=Respond&accept=true", new(),
                tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            using (var db = Db())
            {
                var coachId = db.Accounts.AsNoTracking().Single(a => a.Nickname == coachNick).Id;
                that(db.TeamMemberships.Single(m => m.TeamId == teamId && m.AccountId == coachId && m.LeftAt == null)
                        .Slot == RosterSlot.Coach,
                    "accepting must not quietly change the slot the captain chose");
            }

            using var anonymous = Client();
            var html = anonymous.GetAsync($"/pl/teams/{teamId}").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();

            that(html.Contains(coachNick), "the coach should be listed on the team page");
            that(html.Contains("nie gra w meczach"),
                "the page must say plainly that the staff section does not play — otherwise a coach reads as a sixth player");
        });

        // ── edit and disband (045) ───────────────────────────────────────
        check("teams: a stranger cannot open or submit team settings", () =>
        {
            var get = asOutsider.GetAsync($"/pl/teams/{teamId}/edit").GetAwaiter().GetResult();
            that((int)get.StatusCode == 403, $"expected 403, got {(int)get.StatusCode}");

            using var db = Db();
            var before = db.Teams.AsNoTracking().Single(t => t.Id == teamId).Name;

            var post = PostAsync(asOutsider, $"/pl/teams/{teamId}/edit", new()
            {
                ["Input.Name"] = "Przejęte",
                ["Input.Tag"] = $"X{stamp % 100000}",
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that((int)post.StatusCode is 403 or 400, $"expected a refusal, got {(int)post.StatusCode}");

            using var after = Db();
            that(after.Teams.AsNoTracking().Single(t => t.Id == teamId).Name == before,
                "the team must be unchanged");
        });

        check("teams: a captain can rename and re-tag, and history is unaffected", () =>
        {
            var newTag = $"R{stamp % 1000000}";
            var res = PostAsync(asPlayer, $"/pl/teams/{teamId}/edit", new()
            {
                ["Input.Name"] = "Nowa Nazwa",
                ["Input.Tag"] = newTag,
                ["Input.City"] = "Gdańsk",
            }).GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var db = Db();
            var team = db.Teams.AsNoTracking().Single(t => t.Id == teamId);
            that(team.Name == "Nowa Nazwa" && team.Tag == newTag && team.City == "Gdańsk", "the edit must be saved");

            // The id is what results reference, and it must never move.
            that(team.Id == teamId, "renaming must not create a new team");
            tag = newTag;
        });

        check("teams: DISBAND requires typing the tag, and does NOT delete the row", () =>
        {
            using (var db = Db())
            {
                var wrong = PostAsync(asPlayer, $"/pl/teams/{teamId}/edit?handler=Disband", new()
                {
                    ["ConfirmTag"] = "cokolwiek",
                }, tokenPage: $"/pl/teams/{teamId}/edit").GetAwaiter().GetResult();

                that(wrong.StatusCode == HttpStatusCode.OK, "a wrong confirmation must not disband anything");
                that(db.Teams.AsNoTracking().Single(t => t.Id == teamId).DisbandedAt is null, "still active");
            }

            var res = PostAsync(asPlayer, $"/pl/teams/{teamId}/edit?handler=Disband", new()
            {
                ["ConfirmTag"] = tag,
            }, tokenPage: $"/pl/teams/{teamId}/edit").GetAwaiter().GetResult();

            that(res.StatusCode == HttpStatusCode.Redirect, $"expected a redirect, got {(int)res.StatusCode}");

            using var after = Db();
            var team = after.Teams.AsNoTracking().SingleOrDefault(t => t.Id == teamId);

            // Matches and ratings point at this id. Deleting the row would leave
            // finished tournaments referencing nothing — history rewritten by a
            // captain clicking a button months later (045).
            that(team is not null, "THE ROW MUST SURVIVE — deleting it would corrupt every match that referenced this team");
            that(team!.DisbandedAt is not null, "the team must be marked disbanded");

            that(!after.TeamMemberships.Any(m => m.TeamId == teamId && m.LeftAt == null),
                "everyone leaves at the same moment, so nobody holds captain rights over a dissolved team");
        });

        check("teams: a disbanded team leaves the directory but keeps its page and its tag", () =>
        {
            using var anonymous = Client();

            var directory = anonymous.GetAsync("/pl/teams").GetAwaiter().GetResult()
                .Content.ReadAsStringAsync().GetAwaiter().GetResult();
            that(!directory.Contains(tag), "a disbanded team must not be listed");

            // The link still resolves: it may be sitting in a Discord message
            // from last season, and old brackets still name this team (018).
            var page = anonymous.GetAsync($"/pl/teams/{teamId}").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.OK, $"the page must still load, got {(int)page.StatusCode}");

            // And the tag stays reserved, or an old bracket would read as if it
            // belonged to whoever claimed the tag afterwards.
            using var fresh = SignedInAs($"Nw{stamp}");
            var stolen = PostAsync(fresh, "/pl/teams/create", new()
            {
                ["Input.Name"] = "Podszywacz",
                ["Input.Tag"] = tag,
            }).GetAwaiter().GetResult();

            that(stolen.StatusCode == HttpStatusCode.OK, "a disbanded team's tag must stay reserved");
        });

        check("teams: a disbanded team accepts no more invites", () =>
        {
            var res = PostAsync(asPlayer, $"/pl/teams/{teamId}?handler=Invite", new()
            {
                ["InviteNickname"] = outsider,
            }, tokenPage: $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that((int)res.StatusCode is 403 or 404,
                $"a dissolved team must not be able to recruit, got {(int)res.StatusCode}");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db())
            db.Database.ExecuteSqlRaw(
                "DELETE FROM notifications WHERE account_id IN (SELECT id FROM accounts WHERE email LIKE {0}); " +
                "DELETE FROM team_memberships WHERE account_id IN (SELECT id FROM accounts WHERE email LIKE {0}); " +
                "DELETE FROM teams WHERE tag = {1} OR name = 'Nowa Nazwa'; " +
                "DELETE FROM sessions WHERE account_id IN (SELECT id FROM accounts WHERE email LIKE {0}); " +
                "DELETE FROM accounts WHERE email LIKE {0};",
                $"%{stamp}@nextskill.test", tag);
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
