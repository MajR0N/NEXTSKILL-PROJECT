using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Api.Shared.Persistence;

/// <summary>
/// The one database (decision 008). Users, teams, matches, tournaments and
/// ratings live together so that recording a result, advancing a bracket and
/// updating ratings happen in <b>one transaction</b> — which decision 035 makes
/// a correctness requirement, not a convenience.
/// </summary>
public sealed class NextSkillDb(DbContextOptions<NextSkillDb> options, ITenantContext tenant)
    : DbContext(options)
{
    public DbSet<Organiser> Organisers => Set<Organiser>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<GameServer> Servers => Set<GameServer>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Rating> Ratings => Set<Rating>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums as strings: readable in psql, and immune to someone reordering
        // the enum and silently reinterpreting existing rows.
        b.Entity<GameServer>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.Format).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Integrity).HasConversion<string>();
        b.Entity<Rating>().Property(x => x.Game).HasConversion<string>();

        // ── tenancy: scoping is the DEFAULT, not something to remember ─────
        //
        // A missing `WHERE organiser_id = ?` is a data breach (037). Declared
        // once here, these filters apply to every query against these entities
        // forever — so forgetting is no longer possible.
        //
        // RequireOrganiserId() throws when there is no tenant, on purpose: a
        // null tenant that silently disabled the filter would return every
        // organiser's rows (036).
        b.Entity<GameServer>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<Tournament>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<Match>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        //
        // Accounts, Teams and Ratings are deliberately UNFILTERED: they are
        // global by design, because cross-organiser ratings and profiles are the
        // whole point of a platform rather than isolated installs (036).
        // Their access rules must therefore be explicit — the safety net does
        // not apply to them.

        b.Entity<Organiser>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(60);
        });

        b.Entity<Account>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Nickname).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Nickname).HasMaxLength(32);
            // Native inet, not a string: validates on write and supports subnet
            // operators for abuse detection (008).
            e.Property(x => x.LastSeenIp).HasColumnType("inet");
        });

        b.Entity<Team>(e =>
        {
            e.HasIndex(x => x.Tag).IsUnique();
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Tag).HasMaxLength(8);
        });

        b.Entity<GameServer>(e =>
        {
            e.HasIndex(x => new { x.OrganiserId, x.Host, x.Port }).IsUnique();
            e.Property(x => x.Host).HasMaxLength(253);
        });

        b.Entity<Match>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");

            // GIN over the whole payload: ANY key inside becomes searchable,
            // including keys we haven't thought of yet. This is the concrete
            // reason 008 chose Postgres — MySQL would need a generated column
            // and a migration per query pattern.
            e.HasIndex(x => x.Payload).HasMethod("gin");

            // ── the rule the database enforces so app code cannot forget ────
            //
            // One live match per server (007). A PARTIAL unique index: applies
            // only to rows where status = 'Live', so finished matches are
            // exempt. Two organisers racing to start a match on the same server
            // cannot both win — the database decides, and a race condition
            // becomes an error instead of corruption.
            e.HasIndex(x => x.ServerId)
                .IsUnique()
                .HasFilter("status = 'Live'")
                .HasDatabaseName("ix_one_live_match_per_server");

            e.HasIndex(x => new { x.TournamentId, x.Status });
        });

        b.Entity<Rating>(e =>
        {
            // One rating per subject per game — CS2 and CS 1.6 never mix (025).
            e.HasIndex(x => new { x.TeamId, x.Game }).IsUnique().HasFilter("team_id IS NOT NULL");
            e.HasIndex(x => new { x.AccountId, x.Game }).IsUnique().HasFilter("account_id IS NOT NULL");
        });

        // snake_case everywhere, so hand-written SQL and psql sessions read
        // naturally. EF's default PascalCase would need quoting in every query.
        foreach (var entity in b.Model.GetEntityTypes())
        {
            entity.SetTableName(ToSnake(entity.GetTableName()!));
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnake(property.Name));
        }
    }

    private static string ToSnake(string name) =>
        string.Concat(name.Select((c, i) =>
            char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
