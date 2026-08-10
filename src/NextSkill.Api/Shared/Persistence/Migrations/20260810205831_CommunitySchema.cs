using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommunitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "season_id",
                table: "tournaments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "platform_role",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lft_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: true),
                    availability = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lft_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "news",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    locale = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    pinned = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "organiser_staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organiser_staff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seasons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_memberships", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lft_listings_game_expires_at",
                table: "lft_listings",
                columns: new[] { "game", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_one_lft_listing_per_account_game",
                table: "lft_listings",
                columns: new[] { "account_id", "game" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_news_slug_locale",
                table: "news",
                columns: new[] { "slug", "locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_account_id_read_at_created_at",
                table: "notifications",
                columns: new[] { "account_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_organiser_staff_organiser_id_account_id",
                table: "organiser_staff",
                columns: new[] { "organiser_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seasons_number",
                table: "seasons",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_active_membership_per_team",
                table: "team_memberships",
                columns: new[] { "team_id", "account_id" },
                unique: true,
                filter: "left_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_account_id",
                table: "team_memberships",
                column: "account_id");

            // ── search: trigram, accent-insensitive (decision 043) ──────────
            //
            // pg_trgm matches on three-character overlap, so it tolerates typos
            // and partial words. unaccent strips diacritics, so someone typing
            // "Stara Szkola" finds "Stara Szkoła" — which is the whole point for
            // a Polish audience.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");

            // unaccent() is NOT marked IMMUTABLE, because it depends on a
            // dictionary that could in principle change — so PostgreSQL refuses
            // to build an index on it directly. The standard workaround is an
            // IMMUTABLE wrapper that pins the dictionary explicitly.
            //
            // Without this, "CREATE INDEX ... unaccent(name)" fails with
            // "functions in index expression must be marked IMMUTABLE", which is
            // a confusing error if you have not met it before.
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.ns_unaccent(text)
                RETURNS text
                LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS
                $$ SELECT public.unaccent('public.unaccent', $1) $$;");

            migrationBuilder.Sql(@"
                CREATE INDEX ix_teams_name_trgm ON teams
                USING gin (public.ns_unaccent(lower(name)) gin_trgm_ops);");
            migrationBuilder.Sql(@"
                CREATE INDEX ix_teams_tag_trgm ON teams
                USING gin (public.ns_unaccent(lower(tag)) gin_trgm_ops);");
            migrationBuilder.Sql(@"
                CREATE INDEX ix_accounts_nickname_trgm ON accounts
                USING gin (public.ns_unaccent(lower(nickname)) gin_trgm_ops);");
            migrationBuilder.Sql(@"
                CREATE INDEX ix_tournaments_name_trgm ON tournaments
                USING gin (public.ns_unaccent(lower(name)) gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lft_listings");

            migrationBuilder.DropTable(
                name: "news");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "organiser_staff");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "team_memberships");

            migrationBuilder.DropColumn(
                name: "season_id",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "platform_role",
                table: "accounts");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_teams_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_teams_tag_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accounts_nickname_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tournaments_name_trgm;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.ns_unaccent(text);");
        }
    }
}
