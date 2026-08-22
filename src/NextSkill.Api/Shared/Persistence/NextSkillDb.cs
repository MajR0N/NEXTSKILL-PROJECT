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
    public DbSet<MatchVeto> MatchVetos => Set<MatchVeto>();
    public DbSet<MatchVetoAction> MatchVetoActions => Set<MatchVetoAction>();
    public DbSet<MatchMap> MatchMaps => Set<MatchMap>();
    public DbSet<MatchParticipant> MatchParticipants => Set<MatchParticipant>();
    public DbSet<TournamentRegistration> Registrations => Set<TournamentRegistration>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<RatingChange> RatingChanges => Set<RatingChange>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<OrganiserStaff> OrganiserStaff => Set<OrganiserStaff>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<LftListing> LftListings => Set<LftListing>();
    public DbSet<TeamOpening> TeamOpenings => Set<TeamOpening>();
    public DbSet<OpeningApplication> OpeningApplications => Set<OpeningApplication>();
    public DbSet<TeamInterest> TeamInterests => Set<TeamInterest>();
    public DbSet<RecruitmentBlock> RecruitmentBlocks => Set<RecruitmentBlock>();
    public DbSet<ContactGrant> ContactGrants => Set<ContactGrant>();
    public DbSet<Season> Seasons => Set<Season>();
    public DbSet<NewsPost> News => Set<NewsPost>();
    public DbSet<AdvertReport> AdvertReports => Set<AdvertReport>();

    /// <summary>
    /// Accent-insensitive text, for search (decisions 043, 051 §8).
    ///
    /// <b>PostgreSQL's own <c>unaccent()</c> cannot be used directly.</b> It is
    /// declared <c>STABLE</c> rather than <c>IMMUTABLE</c> — because its
    /// behaviour depends on a dictionary that could be redefined — and
    /// PostgreSQL refuses to build an index on a non-immutable expression. 043's
    /// search SQL was written against it and simply does not execute; the
    /// migration adds a one-line <c>IMMUTABLE</c> wrapper, which is the standard
    /// resolution and the one 051 §8 recorded.
    ///
    /// It matters for this audience specifically: without it a Polish player
    /// typing <c>lodz</c> or <c>slask</c> finds nothing.
    /// </summary>
    [DbFunction("f_unaccent", Schema = "public")]
    public static string Unaccent(string value) =>
        throw new NotSupportedException("f_unaccent is evaluated by PostgreSQL, never in memory.");

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
        b.Entity<MatchVetoAction>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<MatchVetoAction>().Property(x => x.Side).HasConversion<string>();
        b.Entity<MatchMap>().Property(x => x.TeamAStartsAs).HasConversion<string>();
        b.Entity<MatchParticipant>().Property(x => x.Slot).HasConversion<string>();
        b.Entity<MatchParticipant>().Property(x => x.Provenance).HasConversion<string>();
        b.Entity<Rating>().Property(x => x.Game).HasConversion<string>();
        b.Entity<RatingChange>().Property(x => x.Game).HasConversion<string>();
        b.Entity<AuditEntry>().Property(x => x.Action).HasConversion<string>();
        b.Entity<AuditEntry>().Property(x => x.Outcome).HasConversion<string>();
        b.Entity<AuditEntry>().Property(x => x.Retention).HasConversion<string>();
        b.Entity<Account>().Property(x => x.PlatformRole).HasConversion<string>();
        b.Entity<Account>().Property(x => x.TwoFactorMethod).HasConversion<string>();
        b.Entity<OneTimeCode>().Property(x => x.Purpose).HasConversion<string>();
        b.Entity<TeamMembership>().Property(x => x.Role).HasConversion<string>();
        b.Entity<TeamMembership>().Property(x => x.Slot).HasConversion<string>();
        b.Entity<OrganiserStaff>().Property(x => x.Role).HasConversion<string>();
        b.Entity<Notification>().Property(x => x.Type).HasConversion<string>();
        b.Entity<LftListing>().Property(x => x.Game).HasConversion<string>();
        b.Entity<TeamOpening>().Property(x => x.Game).HasConversion<string>();
        b.Entity<TeamOpening>().Property(x => x.Slot).HasConversion<string>();
        b.Entity<OpeningApplication>().Property(x => x.State).HasConversion<string>();
        b.Entity<TeamInterest>().Property(x => x.State).HasConversion<string>();

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
        b.Entity<MatchVeto>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<MatchVetoAction>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<MatchMap>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<MatchParticipant>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<TournamentRegistration>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        b.Entity<OrganiserStaff>().HasQueryFilter(x => x.OrganiserId == tenant.RequireOrganiserId());
        //
        // Accounts, Teams, Ratings and RatingChanges are deliberately UNFILTERED: they are
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
            e.Property(x => x.BanReason).HasMaxLength(500);

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
            e.Property(x => x.DisputeReason).HasMaxLength(500);

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

            // The rating sweeper's only query, in (EndedAt, Id) order. PARTIAL,
            // so it holds the matches still owed a rating rather than every
            // match ever played — which is the whole point of the RatedAt
            // column existing next to the ledger's unique index (050 §4).
            e.HasIndex(x => new { x.EndedAt, x.Id })
                .HasFilter("rated_at IS NULL AND ended_at IS NOT NULL")
                .HasDatabaseName("ix_matches_awaiting_rating");

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

        b.Entity<MatchVeto>(e =>
        {
            e.Property(x => x.BestOf).HasDefaultValue(1);
            e.HasIndex(x => x.MatchId)
                .IsUnique()
                .HasDatabaseName("ix_one_veto_per_match");
            e.HasOne<Match>().WithOne()
                .HasForeignKey<MatchVeto>(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MatchVetoAction>(e =>
        {
            e.Property(x => x.Map).HasMaxLength(64);
            e.HasIndex(x => new { x.MatchVetoId, x.Sequence })
                .IsUnique()
                .HasDatabaseName("ix_one_action_per_veto_step");
            e.HasOne<MatchVeto>().WithMany()
                .HasForeignKey(x => x.MatchVetoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MatchMap>(e =>
        {
            e.Property(x => x.Map).HasMaxLength(64);
            e.HasIndex(x => new { x.MatchId, x.Order })
                .IsUnique()
                .HasDatabaseName("ix_one_map_per_match_order");
            e.HasOne<Match>().WithMany()
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MatchParticipant>(e =>
        {
            // Sized for the ENCRYPTED form, not the four characters a player
            // reads off the page (012).
            e.Property(x => x.LinkCode).HasMaxLength(512);

            // One row per person per match. A `player_bound` event PROMOTES the
            // declared row; it must never insert a second one, or a player would
            // be rated twice for the same match (050).
            e.HasIndex(x => new { x.MatchId, x.AccountId })
                .IsUnique()
                .HasDatabaseName("ix_one_participant_per_match_account");

            e.HasOne<Match>().WithMany()
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);

            // The two reads this table has: an organiser's own history, and
            // staff sweeping recent decisions across the platform.
            e.HasIndex(x => new { x.OrganiserId, x.OccurredAt });
            e.HasIndex(x => new { x.Retention, x.OccurredAt });
        });

        b.Entity<RatingChange>(e =>
        {
            e.Property(x => x.ExcludedBecause).HasMaxLength(200);

            // THE SWEEPER'S IDEMPOTENCY, and the reason there is no application
            // lock around rating. Two sweeps of the same match cannot both
            // write; the second one hits the database and stops. Filtered
            // because a season RD widening has no match and several may share a
            // subject (050 §3).
            e.HasIndex(x => new { x.MatchId, x.SubjectId, x.Game })
                .IsUnique()
                .HasFilter("match_id IS NOT NULL AND superseded_at IS NULL")
                .HasDatabaseName("ix_one_rating_change_per_match_subject");

            // 049's trend chart: one subject, one game, in order.
            e.HasIndex(x => new { x.SubjectId, x.Game, x.AppliedAt });
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

            // SetNull rather than Cascade: a closed or deleted opening must
            // never take a roster place with it (045).
            e.HasOne<TeamOpening>().WithMany()
                .HasForeignKey(x => x.FromTeamOpeningId)
                .OnDelete(DeleteBehavior.SetNull);
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

            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);

            // The board query, in its actual order: freshness, never rating.
            // Rating is visible and filterable and must never buy prominence,
            // or the board starves exactly the new players it exists to serve.
            e.HasIndex(x => new { x.Game, x.PublishedAt });
            e.HasIndex(x => x.ExpiresAt).HasFilter("closed_at IS NULL");

            // Same reason as team_openings: `&&` needs GIN, not a btree.
            e.HasIndex(x => x.AvailabilityHours).HasMethod("gin");
        });

        b.Entity<TeamOpening>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);

            e.HasIndex(x => new { x.Game, x.PublishedAt });
            e.HasIndex(x => new { x.TeamId, x.ClosedAt });
            e.HasIndex(x => x.ExpiresAt).HasFilter("closed_at IS NULL");

            // The schedule filter is an array intersection (`&&`), which only a
            // GIN index can answer without reading every live advert.
            e.HasIndex(x => x.AvailabilityHours).HasMethod("gin");

            e.HasOne<Team>().WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AdvertReport>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);

            // Exactly one target. Enforced by the database rather than by
            // whichever handler happens to build the row, because a report
            // pointing at nothing — or at two things — is unmoderatable.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_advert_report_one_target",
                "(lft_listing_id IS NOT NULL)::int + (team_opening_id IS NOT NULL)::int = 1"));

            // One report per person per advert. Partial, one per target column,
            // so a reporter cannot flood the queue by clicking twice.
            e.HasIndex(x => new { x.ReporterAccountId, x.LftListingId })
                .IsUnique()
                .HasFilter("lft_listing_id IS NOT NULL")
                .HasDatabaseName("ix_one_report_per_listing");
            e.HasIndex(x => new { x.ReporterAccountId, x.TeamOpeningId })
                .IsUnique()
                .HasFilter("team_opening_id IS NOT NULL")
                .HasDatabaseName("ix_one_report_per_opening");

            // The queue reads exactly this.
            e.HasIndex(x => x.CreatedAt).HasFilter("resolved_at IS NULL");

            e.HasOne<LftListing>().WithMany()
                .HasForeignKey(x => x.LftListingId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TeamOpening>().WithMany()
                .HasForeignKey(x => x.TeamOpeningId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OpeningApplication>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(300);

            // One application per player per opening PER CYCLE. The database
            // enforces it, so a decline cannot be worked around by resubmitting
            // and a double-click cannot consume two of the player's ten (056).
            e.HasIndex(x => new { x.TeamOpeningId, x.AccountId, x.Cycle })
                .IsUnique()
                .HasDatabaseName("ix_one_application_per_opening_cycle");

            e.HasIndex(x => new { x.AccountId, x.State });

            e.HasOne<TeamOpening>().WithMany()
                .HasForeignKey(x => x.TeamOpeningId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TeamInterest>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(300);

            // One live approach per opening/player pair. Without this, 045's
            // deliberately-closed 200-invite hole reopens through a different
            // door (051 §5).
            e.HasIndex(x => new { x.TeamOpeningId, x.AccountId, x.Cycle })
                .IsUnique()
                .HasDatabaseName("ix_one_interest_per_opening_cycle");

            e.HasIndex(x => new { x.AccountId, x.State });

            e.HasOne<TeamOpening>().WithMany()
                .HasForeignKey(x => x.TeamOpeningId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RecruitmentBlock>(e =>
        {
            e.HasIndex(x => new { x.AccountId, x.TeamId })
                .IsUnique()
                .HasDatabaseName("ix_one_block_per_player_team");
        });

        b.Entity<ContactGrant>(e =>
        {
            e.HasIndex(x => new { x.TeamOpeningId, x.AccountId })
                .IsUnique()
                .HasDatabaseName("ix_one_contact_grant_per_opening_player");
            e.HasIndex(x => x.ExpiresAt);

            e.HasOne<TeamOpening>().WithMany()
                .HasForeignKey(x => x.TeamOpeningId)
                .OnDelete(DeleteBehavior.Cascade);
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
