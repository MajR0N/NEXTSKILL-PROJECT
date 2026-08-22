using System.Net;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Access;

namespace NextSkill.Checks;

/// <summary>
/// The organiser staff panel (decisions 015, 036).
///
/// <b>The gap this closes had teeth.</b> Every organiser-scoped permission in
/// the product reads <c>organiser_staff</c>, and until now nothing could write
/// it — so a referee, an admin and an owner all existed only if somebody ran
/// SQL by hand. The checks below are aimed at the two ways a role page goes
/// wrong: it lets the wrong person grant, or it lets an organiser end up with
/// nobody who can grant at all.
/// </summary>
public static class OrganiserStaffChecks
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
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        const string password = "a-long-enough-passphrase";

        var ownerNick = $"SOwn{stamp}";
        var otherOwnerNick = $"SOth{stamp}";
        var refNick = $"SRef{stamp}";
        var playerNick = $"SPly{stamp}";

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

        using var owner = SignedInAs(ownerNick);
        using var otherOwner = SignedInAs(otherOwnerNick);
        using var referee = SignedInAs(refNick);
        SignedInAs(playerNick).Dispose();

        Guid ownerId, otherOwnerId, refereeId, playerId;

        using (var db = Db())
        {
            ownerId = db.Accounts.Single(a => a.Nickname == ownerNick).Id;
            otherOwnerId = db.Accounts.Single(a => a.Nickname == otherOwnerNick).Id;
            refereeId = db.Accounts.Single(a => a.Nickname == refNick).Id;
            playerId = db.Accounts.Single(a => a.Nickname == playerNick).Id;

            foreach (var (id, name) in new[] { (orgA, "A"), (orgB, "B") })
                db.Organisers.Add(new Organiser
                {
                    Id = id,
                    Name = $"Staff Panel {name} {stamp}",
                    Slug = $"staff-panel-{name.ToLowerInvariant()}-{stamp}",
                    CreatedAt = DateTimeOffset.UtcNow,
                });

            // The only hand-written staff rows in this file: the bootstrap owner
            // of each organiser. Everything after this is granted THROUGH the
            // page, which is the point.
            db.OrganiserStaff.AddRange(
                new OrganiserStaff { Id = Guid.NewGuid(), OrganiserId = orgA, AccountId = ownerId, Role = OrganiserRole.Owner, CreatedAt = DateTimeOffset.UtcNow },
                new OrganiserStaff { Id = Guid.NewGuid(), OrganiserId = orgB, AccountId = otherOwnerId, Role = OrganiserRole.Owner, CreatedAt = DateTimeOffset.UtcNow },
                new OrganiserStaff { Id = Guid.NewGuid(), OrganiserId = orgA, AccountId = refereeId, Role = OrganiserRole.Referee, CreatedAt = DateTimeOffset.UtcNow });

            db.SaveChanges();
        }

        OrganiserStaff? StaffRow(Guid organiserId, Guid accountId)
        {
            using var db = Db();
            return db.OrganiserStaff.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefault(s => s.OrganiserId == organiserId && s.AccountId == accountId);
        }

        int AuditCount(Guid actorId, AuditAction action, AuditOutcome outcome)
        {
            using var db = Db();
            return db.AuditEntries.AsNoTracking().Count(a =>
                a.ActorAccountId == actorId && a.Action == action && a.Outcome == outcome);
        }

        HttpResponseMessage Post(HttpClient http, string handler, Dictionary<string, string> fields, string tokenPage) =>
            http.PostFormAsync($"/pl/org/staff?handler={handler}", fields, tokenPage).GetAwaiter().GetResult();

        check("org staff: a referee cannot see the page, and a grant attempt is refused AND recorded", () =>
        {
            var page = referee.GetAsync("/pl/org/staff").GetAwaiter().GetResult();
            that(page.StatusCode == HttpStatusCode.Forbidden,
                $"a referee who can appoint referees is not a referee (036); got {(int)page.StatusCode}");

            var response = Post(referee, "Grant", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.Nickname"] = playerNick,
                ["Input.Role"] = nameof(OrganiserRole.Owner),
            }, "/pl/me");

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"and the hand-written POST must be refused too, got {(int)response.StatusCode}");
            that(StaffRow(orgA, playerId) is null, "nothing may have been granted");
            that(AuditCount(refereeId, AuditAction.GrantRole, AuditOutcome.Denied) == 1,
                "a refused escalation writes no row anywhere else — only the log can hold it");
        });

        check("org staff: the owner appoints by nickname, and the row is audited", () =>
        {
            var response = Post(owner, "Grant", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.Nickname"] = playerNick.ToUpperInvariant(),   // nickname match is case-insensitive
                ["Input.Role"] = nameof(OrganiserRole.Referee),
            }, "/pl/org/staff");

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"a successful grant redirects, got {(int)response.StatusCode}");
            that(StaffRow(orgA, playerId)?.Role == OrganiserRole.Referee,
                "the player must now be a referee of organiser A");
            that(AuditCount(ownerId, AuditAction.GrantRole, AuditOutcome.Allowed) == 1,
                "and the appointment is in the log");
        });

        check("org staff: re-granting CHANGES the role rather than adding a second row", () =>
        {
            Post(owner, "Grant", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.Nickname"] = playerNick,
                ["Input.Role"] = nameof(OrganiserRole.Admin),
            }, "/pl/org/staff");

            using var db = Db();
            var rows = db.OrganiserStaff.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.OrganiserId == orgA && s.AccountId == playerId).ToList();

            that(rows.Count == 1,
                $"two rows would make RoleIn() depend on dictionary insertion order; got {rows.Count}");
            that(rows[0].Role == OrganiserRole.Admin, "and the role must be the new one");
        });

        check("org staff: the LAST owner cannot be revoked, and cannot demote themselves either", () =>
        {
            var revoke = Post(owner, "Revoke", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.AccountId"] = ownerId.ToString(),
            }, "/pl/org/staff");

            that(revoke.StatusCode == HttpStatusCode.OK,
                $"the refusal is shown on the page, not thrown; got {(int)revoke.StatusCode}");
            that(StaffRow(orgA, ownerId)?.Role == OrganiserRole.Owner,
                "an organiser with no owner is an organiser nobody can ever staff again");

            Post(owner, "Grant", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.Nickname"] = ownerNick,
                ["Input.Role"] = nameof(OrganiserRole.Admin),
            }, "/pl/org/staff");

            that(StaffRow(orgA, ownerId)?.Role == OrganiserRole.Owner,
                "demotion is the same hole as removal, so it is closed by the same rule");
        });

        check("org staff: with a second owner in place, the first may finally step down", () =>
        {
            Post(owner, "Grant", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.Nickname"] = playerNick,
                ["Input.Role"] = nameof(OrganiserRole.Owner),
            }, "/pl/org/staff");

            var response = Post(owner, "Revoke", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.AccountId"] = ownerId.ToString(),
            }, "/pl/org/staff");

            that(response.StatusCode == HttpStatusCode.Redirect,
                $"handing over then leaving is what makes the last-owner rule a safeguard and not a trap; got {(int)response.StatusCode}");
            that(StaffRow(orgA, ownerId) is null, "the row must be gone");
            that(AuditCount(ownerId, AuditAction.RevokeRole, AuditOutcome.Allowed) == 1,
                "losing access is exactly what the log exists to answer later");
        });

        check("org staff: an owner of A cannot grant inside B", () =>
        {
            var response = Post(otherOwner, "Grant", new()
            {
                ["Input.OrganiserId"] = orgA.ToString(),
                ["Input.Nickname"] = refNick,
                ["Input.Role"] = nameof(OrganiserRole.Owner),
            }, "/pl/org/staff");

            that(response.StatusCode == HttpStatusCode.Forbidden,
                $"tenancy is not advisory; got {(int)response.StatusCode}");
            that(StaffRow(orgA, refereeId)?.Role == OrganiserRole.Referee,
                "the referee of A must still be only a referee");
        });
    }
}
