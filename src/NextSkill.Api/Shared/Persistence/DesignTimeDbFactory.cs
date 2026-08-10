using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Api.Shared.Persistence;

/// <summary>
/// Lets `dotnet ef` construct the context without booting the whole app.
/// Design-time only — never used at runtime.
/// </summary>
public sealed class DesignTimeDbFactory : IDesignTimeDbContextFactory<NextSkillDb>
{
    public NextSkillDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NextSkillDb>()
            .UseNpgsql("Host=localhost;Database=nextskill;Username=nextskill;Password=dev")
            .Options;

        return new NextSkillDb(options, new DesignTimeTenant());
    }

    /// <summary>
    /// Migrations only need the model shape, not a real tenant. A fixed id keeps
    /// the query filters expressible without pretending a request exists.
    /// </summary>
    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid? CurrentOrganiserId => Guid.Empty;
        public Guid RequireOrganiserId() => Guid.Empty;
    }
}
