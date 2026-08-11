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
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();
    public DbSet<OneTimeCode> OneTimeCodes => Set<OneTimeCode>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<GameServer> Servers => Set<GameServer>();
    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<TournamentTemplate> TournamentTemplates => Set<TournamentTemplate>();
    public DbSet<TournamentDiscordWebhook> TournamentDiscordWebhooks => Set<TournamentDiscordWebhook>();
    public DbSet<DiscordAnnouncement> DiscordAnnouncements => Set<DiscordAnnouncement>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<TournamentRegistration> Registrations => Set<TournamentRegistration>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<OrganiserStaff> OrganiserStaff => Set<OrganiserStaff>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LftListing> LftListings => Set<LftListing>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<NewsPost> News => Set<NewsPost>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums as strings: readable in psql, and immune to someone reordering
        // the enum and silently reinterpreting existing rows.
        b.Entity<GameServer>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.Format).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Tournament>().Property(x => x.TeamSize).HasConversion<string>();
        b.Entity<TournamentTemplate>().Property(x => x.Game).HasConversion<string>();
        b.Entity<TournamentTemplate>().Property(x => x.Format).HasConversion<string>();
        b.Entity<TournamentTemplate>().Property(x => x.TeamSize).HasConversion<string>();
        b.Entity<DiscordAnnouncement>().Property(x => x.Type).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Integrity).HasConversion<string>();
        b.Entity<Match>().Property(x => x.Side).HasConversion<string>();
        b.Entity<Match>().Property(x => x.WinnerToSlot).HasConversion<string>();
        b.Entity<Match>().Property(x => x.LoserToSlot).HasConversion<string>();
        b.Entity<Rating>().Property(x => x.Game).HasConversion<string>();
        b.Entity<Account>().Property(x => x.PlatformRole).HasConversion<string>();
        b.Entity<Account>().Property(x => x.TwoFactorMethod).HasConversion<string>();
        b.Entity<OneTimeCode>().Property(x => x.Purpose).HasConversion<string>();
        b.Entity<TeamMembership>().Property(x => x.Role).HasConversion<string>();
        b.Entity<TeamMembership>().Property(x => x.Slot).HasConversion<string>();
        b.Entity<OrganiserStaff>().Property(x => x.Role).HasConversion<string>();
        b.Entity<Notification>().Property(x => x.Type).HasConversion<string>();
        b.Entity<LftListing>().Property(x => x.Game).HasConversion<string>();

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
        b.Entity<TournamentTemplate>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<TournamentDiscordWebhook>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<DiscordAnnouncement>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<Match>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<TournamentRegistration>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<OrganiserStaff>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
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
            // Email is stored already lowercased (011), so a plain unique index
            // is case-insensitive in effect and needs no expression index.
            e.HasIndex(x => x.Email).IsUnique();
            //
            // Nickname keeps the case the player typed, so uniqueness needs an
            // index on lower(nickname) instead — added as raw SQL in the
            // migration, since EF has no model-level expression indexes.
            // Without it "Pukawka" and "pukawka" are two accounts, which is an
            // impersonation vector, not a cosmetic issue.
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Nickname).HasMaxLength(32);
            e.Property(x => x.SteamId).HasMaxLength(20);
            e.Property(x => x.DiscordId).HasMaxLength(20);

            // An external identity can prove ownership of ONE NEXTSKILL
            // account, never two. Application checks give a friendly answer;
            // these indexes close the simultaneous-callback race (011).
            e.HasIndex(x => x.SteamId).IsUnique().HasFilter("steam_id IS NOT NULL");
            e.HasIndex(x => x.DiscordId).IsUnique().HasFilter("discord_id IS NOT NULL");
        });

        // Sessions are global like the accounts they belong to. The index is the
        // logout-everywhere query: every session for one account.
        b.Entity<Session>(e => e.HasIndex(x => x.AccountId));

        b.Entity<RecoveryCode>(e =>
        {
            e.HasIndex(x => x.AccountId);
            e.Property(x => x.CodeHash).HasMaxLength(64);
        });

        b.Entity<OneTimeCode>(e =>
        {
            e.HasIndex(x => new { x.AccountId, x.Purpose });
            e.Property(x => x.CodeHash).HasMaxLength(64);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            // The hash IS the lookup key — a reset link is redeemed by hashing
            // what arrived and finding the row, never by trusting an id in the
            // URL alongside it.
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.AccountId);
            e.Property(x => x.TokenHash).HasMaxLength(64);
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

        b.Entity<Tournament>(e =>
        {
            // Season is global, tournament is tenant-owned. The foreign key
            // makes an assignment real rather than an unchecked Guid, while
            // Restrict preserves permanent archive membership (021).
            e.HasIndex(x => x.SeasonId);
            e.HasOne<Season>().WithMany()
                .HasForeignKey(x => x.SeasonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TournamentDiscordWebhook>(e =>
        {
            e.HasIndex(x => x.TournamentId)
                .IsUnique()
                .HasDatabaseName("ix_one_discord_webhook_per_tournament");
            e.Property(x => x.LastError).HasMaxLength(64);
            e.HasOne<Tournament>().WithMany()
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DiscordAnnouncement>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.DeduplicationKey).HasMaxLength(160);
            e.Property(x => x.LastError).HasMaxLength(64);
            e.HasIndex(x => x.DeduplicationKey)
                .IsUnique()
                .HasDatabaseName("ix_one_discord_announcement_per_event");
            e.HasIndex(x => x.NextAttemptAt)
                .HasFilter("sent_at IS NULL AND abandoned_at IS NULL")
                .HasDatabaseName("ix_due_discord_announcements");
            e.HasOne<Tournament>().WithMany()
                .HasForeignKey(x => x.TournamentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Match>().WithMany()
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TournamentTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.NameKey).HasMaxLength(80);
            e.HasIndex(x => new { x.OrganiserId, x.NameKey })
                .IsUnique()
                .HasDatabaseName("ix_one_tournament_template_name_per_organiser");
        });

        b.Entity<Match>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.ForfeitReason).HasMaxLength(500);

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

            // Routing as real foreign keys. The whole point is that the database
            // refuses a link to a match that does not exist — the application
            // cannot "mostly" get routing right any more (023).
            //
            // NoAction rather than Cascade: deleting a match that others route
            // into should FAIL loudly, not quietly delete half a bracket.
            e.HasOne<Match>().WithMany()
                .HasForeignKey(x => x.WinnerToMatchId)
                .OnDelete(DeleteBehavior.NoAction);

            e.HasOne<Match>().WithMany()
                .HasForeignKey(x => x.LoserToMatchId)
                .OnDelete(DeleteBehavior.NoAction);

            // One match per bracket position per tournament. Generating twice
            // would otherwise produce a second, overlapping bracket rather than
            // an error (023).
            e.HasIndex(x => new { x.TournamentId, x.Side, x.Round, x.Position })
                .IsUnique()
                .HasDatabaseName("ix_one_match_per_bracket_position");
        });

        b.Entity<TeamMembership>(e =>
        {
            // One ACTIVE membership per person per team. Partial index, so a
            // player who left and rejoined does not collide with their own
            // history — the same technique as one-live-match-per-server (007).
            e.HasIndex(x => new { x.TeamId, x.AccountId })
                .IsUnique()
                .HasFilter("left_at IS NULL")
                .HasDatabaseName("ix_one_active_membership_per_team");
            e.HasIndex(x => x.AccountId);
        });

        b.Entity<OrganiserStaff>(e =>
            e.HasIndex(x => new { x.OrganiserId, x.AccountId }).IsUnique());

        b.Entity<TournamentRegistration>(e =>
        {
            e.Property(x => x.ForfeitReason).HasMaxLength(500);
            // One ACTIVE entry per team per tournament. Partial, so a team that
            // withdrew and re-entered does not collide with its own history —
            // the same technique as one-live-match-per-server (007) and
            // one-active-membership-per-team (045).
            e.HasIndex(x => new { x.TournamentId, x.TeamId })
                .IsUnique()
                .HasFilter("withdrawn_at IS NULL")
                .HasDatabaseName("ix_one_active_entry_per_tournament");

            // Seeds must be unique within a tournament, or two teams share a
            // bracket position and one of them silently disappears (023).
            e.HasIndex(x => new { x.TournamentId, x.Seed })
                .IsUnique()
                .HasFilter("seed IS NOT NULL AND withdrawn_at IS NULL")
                .HasDatabaseName("ix_one_team_per_seed");
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Data).HasColumnType("jsonb");
            // The bell count query: unread, newest first, for one account.
            e.HasIndex(x => new { x.AccountId, x.ReadAt, x.CreatedAt });
        });

        b.Entity<LftListing>(e =>
        {
            // One live listing per account per game.
            e.HasIndex(x => new { x.AccountId, x.Game })
                .IsUnique()
                .HasDatabaseName("ix_one_lft_listing_per_account_game");
            // The board query: unexpired, by game.
            e.HasIndex(x => new { x.Game, x.ExpiresAt });
        });

        b.Entity<Season>(e => e.HasIndex(x => x.Number).IsUnique());

        b.Entity<NewsPost>(e =>
        {
            // One row per post per locale (018).
            e.HasIndex(x => new { x.Slug, x.Locale }).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(120);
            e.Property(x => x.Locale).HasMaxLength(5);
            e.Property(x => x.Title).HasMaxLength(200);
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
