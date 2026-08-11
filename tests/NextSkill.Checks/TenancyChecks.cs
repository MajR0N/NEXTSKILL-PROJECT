using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Tournaments;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Checks;

/// <summary>
/// Tenancy isolation — the most important checks in the project (decision 034).
///
/// NEXTSKILL is multi-tenant: organisers share one database, separated by a
/// column rather than by walls. A missing scope is not a bug, it is a data
/// breach — so this must be proven against a real PostgreSQL, not a mock.
/// </summary>
public static class TenancyChecks
{
    private sealed class FixedTenant(Guid? id) : ITenantContext
    {
        public Guid? CurrentOrganiserId => id;
        public Guid RequireOrganiserId() => id
            ?? throw new InvalidOperationException("No organiser in scope.");
    }

    public static void Run(string connectionString, Action<string, Action> check, Action<bool, string> that)
    {
        NextSkillDb Db(Guid? tenant) => new(
            new DbContextOptionsBuilder<NextSkillDb>().UseNpgsql(connectionString).Options,
            new FixedTenant(tenant));

        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var stamp = DateTimeOffset.UtcNow.Ticks;

        // Seed: two organisers, one tournament each. Written with no filter
        // interference by using each organiser's own context.
        using (var db = Db(orgA))
        {
            db.Organisers.Add(new Organiser { Id = orgA, Name = "A", Slug = $"a-{stamp}", CreatedAt = DateTimeOffset.UtcNow });
            db.Organisers.Add(new Organiser { Id = orgB, Name = "B", Slug = $"b-{stamp}", CreatedAt = DateTimeOffset.UtcNow });
            db.Tournaments.Add(new Tournament { Id = Guid.NewGuid(), OrganiserId = orgA, Name = "A Cup", Game = Game.Cs2, Format = TournamentFormat.SingleElimination, Status = TournamentStatus.Draft, CreatedAt = DateTimeOffset.UtcNow });
            db.Tournaments.Add(new Tournament { Id = Guid.NewGuid(), OrganiserId = orgB, Name = "B Cup", Game = Game.Cs16, Format = TournamentFormat.RoundRobin, Status = TournamentStatus.Draft, CreatedAt = DateTimeOffset.UtcNow });
            db.SaveChanges();
        }

        check("tenancy: organiser B cannot SEE organiser A's tournament in a list", () =>
        {
            using var db = Db(orgB);
            var names = db.Tournaments.Select(t => t.Name).ToList();
            that(names.Contains("B Cup"), "B must see its own tournament");
            that(!names.Contains("A Cup"), "B MUST NOT see A's tournament — this would be a data breach");
        });

        check("tenancy: fetching A's tournament BY ID as B returns nothing (→ 404, not 403)", () =>
        {
            Guid aId;
            using (var dbA = Db(orgA)) aId = dbA.Tournaments.Single(t => t.Name == "A Cup").Id;

            using var dbB = Db(orgB);
            var found = dbB.Tournaments.FirstOrDefault(t => t.Id == aId);
            that(found is null,
                "B must not retrieve A's tournament by direct ID. A 403 would confirm it exists — 404 leaks nothing");
        });

        check("tenancy: NO tenant in scope THROWS rather than returning everything", () =>
        {
            using var db = Db(null);
            var threw = false;
            try { _ = db.Tournaments.ToList(); }
            catch (InvalidOperationException) { threw = true; }
            catch (Exception ex) when (ex.InnerException is InvalidOperationException) { threw = true; }

            that(threw,
                "an unscoped query MUST throw. Failing open here would return every organiser's rows");
        });

        check("global entities stay global: accounts are visible to any tenant", () =>
        {
            var email = $"check-{stamp}@nextskill.test";
            using (var db = Db(orgA))
            {
                db.Accounts.Add(new Account { Id = Guid.NewGuid(), Email = email, PasswordHash = "x", Nickname = $"nick{stamp}", CreatedAt = DateTimeOffset.UtcNow });
                db.SaveChanges();
            }

            using var dbB = Db(orgB);
            that(dbB.Accounts.Any(a => a.Email == email),
                "accounts are deliberately NOT tenant-scoped — cross-organiser ratings and profiles are the point of a platform (036)");
        });

        // Clean up so repeated runs stay honest.
        using (var db = Db(orgA))
        {
            db.Database.ExecuteSqlRaw(
                "DELETE FROM tournaments WHERE organiser_id IN ({0},{1}); DELETE FROM accounts WHERE email LIKE {2}; DELETE FROM organisers WHERE id IN ({0},{1});",
                orgA, orgB, $"check-{stamp}@nextskill.test");
        }
    }
}
