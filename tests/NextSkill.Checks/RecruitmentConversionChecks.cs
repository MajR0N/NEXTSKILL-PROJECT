using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;
using NextSkill.Domain.Recruitment;
using NextSkill.Domain.Teams;

namespace NextSkill.Checks;

/// <summary>
/// Turning a mutual contact into a roster invitation (decision 056).
///
/// <b>This is the board's only real success metric.</b> 056 is explicit that
/// mutual contact is a conversation start and only an accepted, slot-specific
/// roster invitation counts — so the check that matters is the one asserting a
/// captain <i>cannot</i> mark their own success: converting sends an invitation
/// and leaves the vacancy open, and the vacancy is filled by the player
/// agreeing to fill it.
/// </summary>
public static class RecruitmentConversionChecks
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

        var captainNick = $"CvCa{stamp}";
        var playerNick = $"CvPl{stamp}";
        var waitingNick = $"CvWa{stamp}";
        var strangerNick = $"CvSt{stamp}";

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
        using var player = SignedInAs(playerNick);
        using var stranger = SignedInAs(strangerNick);
        SignedInAs(waitingNick).Dispose();

        Guid captainId, playerId, waitingId, strangerId;
        var teamId = Guid.NewGuid();
        var openingId = Guid.NewGuid();

        using (var db = Db())
        {
            Guid IdOf(string nickname) => db.Accounts.Single(a => a.Nickname == nickname).Id;
            captainId = IdOf(captainNick);
            playerId = IdOf(playerNick);
            waitingId = IdOf(waitingNick);
            strangerId = IdOf(strangerNick);

            db.Teams.Add(new Team
            {
                Id = teamId, Name = $"Konwersja {stamp}", Tag = $"KV{stamp % 100000}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.TeamMemberships.Add(new TeamMembership
            {
                Id = Guid.NewGuid(), TeamId = teamId, AccountId = captainId,
                Role = TeamRole.Captain, Slot = RosterSlot.Player,
                JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });

            var now = DateTimeOffset.UtcNow;
            db.TeamOpenings.Add(new TeamOpening
            {
                Id = openingId, TeamId = teamId, Game = Game.Cs2, Slot = RecruitmentSlot.Player,
                ContactCaptainAccountId = captainId,
                PublishedAt = now, ExpiresAt = now + RecruitmentRules.AdvertLifetime, CreatedAt = now,
            });

            // The state a mutual acceptance leaves behind: an accepted request
            // and a live grant.
            db.OpeningApplications.Add(new OpeningApplication
            {
                Id = Guid.NewGuid(), TeamOpeningId = openingId, AccountId = playerId,
                Cycle = 1, State = RecruitmentState.Accepted,
                CreatedAt = now, ExpiresAt = now + RecruitmentRules.RequestLifetime,
            });
            db.ContactGrants.Add(new ContactGrant
            {
                Id = Guid.NewGuid(), TeamOpeningId = openingId, AccountId = playerId,
                CaptainAccountId = captainId,
                CreatedAt = now, ExpiresAt = now + RecruitmentRules.ContactGrantLifetime,
            });

            // Somebody else still waiting on the same vacancy.
            db.OpeningApplications.Add(new OpeningApplication
            {
                Id = Guid.NewGuid(), TeamOpeningId = openingId, AccountId = waitingId,
                Cycle = 1, State = RecruitmentState.Pending,
                CreatedAt = now, ExpiresAt = now + RecruitmentRules.RequestLifetime,
            });

            db.SaveChanges();
        }

        HttpResponseMessage Convert(HttpClient http, string tokenPage) =>
            http.PostFormAsync($"/pl/me/recruitment?handler=Convert&openingId={openingId}",
                [], tokenPage).GetAwaiter().GetResult();

        check("conversion: somebody who is not a captain cannot send the invitation", () =>
        {
            var response = Convert(stranger, "/pl/me");
            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"a contact grant is permission to talk, not authority over a roster; got {(int)response.StatusCode}");

            using var db = Db();
            that(!db.TeamMemberships.Any(m => m.TeamId == teamId && m.AccountId == playerId),
                "and nothing may have been offered");
        });

        check("conversion: THE SUCCESS METRIC — it invites, and does NOT fill the vacancy", () =>
        {
            var response = Convert(captain, "/pl/me/recruitment");
            that(response.StatusCode == HttpStatusCode.Redirect,
                $"converting should redirect back to the private surface, got {(int)response.StatusCode}");

            using var db = Db();
            var invite = db.TeamMemberships.AsNoTracking()
                .Single(m => m.TeamId == teamId && m.AccountId == playerId);

            that(invite.JoinedAt is null,
                "it must be a PENDING invitation — the player still has to accept (056, 045)");
            that(invite.Slot == RosterSlot.Player, "in the slot the vacancy was published for");
            that(invite.FromTeamOpeningId == openingId,
                "carrying the opening it came from, or 'a player joined' and 'the board filled a " +
                "vacancy' are the same row and the claim cannot be made honestly");

            that(db.TeamOpenings.AsNoTracking().Single(o => o.Id == openingId).ClosedAt is null,
                "AND THE VACANCY IS STILL OPEN: a recruiter must not be able to mark their own success");

            that(db.OpeningApplications.AsNoTracking()
                    .Single(a => a.TeamOpeningId == openingId && a.AccountId == playerId)
                    .State == RecruitmentState.Converted,
                "the conversation that went somewhere is recorded as Converted");

            that(db.Notifications.Any(n => n.AccountId == playerId && n.Type == NotificationType.TeamInvite),
                "and the player is told, since an invitation nobody sees is not an offer");
        });

        check("conversion: converting twice offers nothing twice", () =>
        {
            var response = Convert(captain, "/pl/me/recruitment");
            that(response.StatusCode == HttpStatusCode.OK,
                $"the second attempt is refused on the page, not thrown; got {(int)response.StatusCode}");

            using var db = Db();
            that(db.TeamMemberships.Count(m => m.TeamId == teamId && m.AccountId == playerId) == 1,
                "the partial unique index is what makes a double click harmless");
        });

        check("conversion: ACCEPTANCE is what fills the opening, and closes it for everyone else", () =>
        {
            var response = player.PostFormAsync($"/pl/teams/{teamId}?handler=Respond&accept=true",
                [], $"/pl/teams/{teamId}").GetAwaiter().GetResult();

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"accepting should redirect to the team, got {(int)response.StatusCode}");

            using var db = Db();
            that(db.TeamMemberships.AsNoTracking()
                    .Single(m => m.TeamId == teamId && m.AccountId == playerId).JoinedAt is not null,
                "the player is now on the roster");

            that(db.TeamOpenings.AsNoTracking().Single(o => o.Id == openingId).ClosedAt is not null,
                "and ONLY NOW is the vacancy filled — this is the event 056 lets marketing claim");

            that(db.OpeningApplications.AsNoTracking()
                    .Single(a => a.TeamOpeningId == openingId && a.AccountId == waitingId)
                    .State == RecruitmentState.OpeningClosed,
                "somebody still waiting on a vacancy that no longer exists is closed NEUTRALLY — " +
                "it is not a rejection of them (056)");
        });

        check("conversion: a closed opening cannot be converted again", () =>
        {
            using (var db = Db())
            {
                // Put a second grant behind the now-closed vacancy.
                db.ContactGrants.Add(new ContactGrant
                {
                    Id = Guid.NewGuid(), TeamOpeningId = openingId, AccountId = strangerId,
                    CaptainAccountId = captainId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow + RecruitmentRules.ContactGrantLifetime,
                });
                db.SaveChanges();
            }

            var response = Convert(captain, "/pl/me/recruitment");
            that(response.StatusCode == HttpStatusCode.OK, "refused on the form");

            using var after = Db();
            that(!after.TeamMemberships.Any(m => m.TeamId == teamId && m.AccountId == strangerId),
                "a filled vacancy is not a standing offer to everybody who ever talked to the captain");
        });

        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM contact_grants WHERE team_opening_id = {0}; " +
                "DELETE FROM opening_applications WHERE team_opening_id = {0}; " +
                "DELETE FROM team_interests WHERE team_opening_id = {0}; " +
                "UPDATE team_memberships SET from_team_opening_id = NULL WHERE team_id = {1}; " +
                "DELETE FROM team_openings WHERE id = {0}; " +
                "DELETE FROM team_memberships WHERE team_id = {1}; " +
                "DELETE FROM notifications WHERE account_id IN ({2},{3},{4},{5}); " +
                "DELETE FROM sessions WHERE account_id IN ({2},{3},{4},{5}); " +
                "DELETE FROM teams WHERE id = {1}; " +
                "DELETE FROM accounts WHERE id IN ({2},{3},{4},{5});",
                openingId, teamId, captainId, playerId, waitingId, strangerId);
        }
    }
}
