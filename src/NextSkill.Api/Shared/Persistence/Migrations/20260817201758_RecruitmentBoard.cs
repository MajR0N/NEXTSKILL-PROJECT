using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecruitmentBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lft_listings_game_expires_at",
                table: "lft_listings");

            migrationBuilder.AlterColumn<string>(
                name: "note",
                table: "lft_listings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "availability_hours",
                table: "lft_listings",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "lft_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cycle",
                table: "lft_listings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string[]>(
                name: "languages",
                table: "lft_listings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                table: "lft_listings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string[]>(
                name: "regions",
                table: "lft_listings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reminder_sent_at",
                table: "lft_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "roles",
                table: "lft_listings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "slots",
                table: "lft_listings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                table: "lft_listings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recruitment_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    by_player = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recruitment_blocks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "team_openings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game = table.Column<string>(type: "text", nullable: false),
                    slot = table.Column<string>(type: "text", nullable: false),
                    contact_captain_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    roles = table.Column<string[]>(type: "text[]", nullable: false),
                    languages = table.Column<string[]>(type: "text[]", nullable: false),
                    regions = table.Column<string[]>(type: "text[]", nullable: false),
                    availability_hours = table.Column<int[]>(type: "integer[]", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_openings", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_openings_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captain_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_grants", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_grants_team_openings_team_opening_id",
                        column: x => x.team_opening_id,
                        principalTable: "team_openings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opening_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_opening_applications_team_openings_team_opening_id",
                        column: x => x.team_opening_id,
                        principalTable: "team_openings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_interests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_opening_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lft_listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_interests", x => x.id);
                    table.ForeignKey(
                        name: "FK_team_interests_team_openings_team_opening_id",
                        column: x => x.team_opening_id,
                        principalTable: "team_openings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lft_listings_expires_at",
                table: "lft_listings",
                column: "expires_at",
                filter: "closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_lft_listings_game_published_at",
                table: "lft_listings",
                columns: new[] { "game", "published_at" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_grants_expires_at",
                table: "contact_grants",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_one_contact_grant_per_opening_player",
                table: "contact_grants",
                columns: new[] { "team_opening_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_application_per_opening_cycle",
                table: "opening_applications",
                columns: new[] { "team_opening_id", "account_id", "cycle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_opening_applications_account_id_state",
                table: "opening_applications",
                columns: new[] { "account_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_one_block_per_player_team",
                table: "recruitment_blocks",
                columns: new[] { "account_id", "team_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_interest_per_opening_cycle",
                table: "team_interests",
                columns: new[] { "team_opening_id", "account_id", "cycle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_interests_account_id_state",
                table: "team_interests",
                columns: new[] { "account_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_team_openings_expires_at",
                table: "team_openings",
                column: "expires_at",
                filter: "closed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_team_openings_game_published_at",
                table: "team_openings",
                columns: new[] { "game", "published_at" });

            migrationBuilder.CreateIndex(
                name: "IX_team_openings_team_id_closed_at",
                table: "team_openings",
                columns: new[] { "team_id", "closed_at" });

            // Listings written before 056 have no publication time and no cycle.
            // Leaving published_at at year 1 would sort every one of them behind
            // everything forever — a data bug dressed as an ordering quirk. Their
            // creation time is what they actually meant.
            migrationBuilder.Sql(
                "UPDATE lft_listings SET published_at = created_at WHERE published_at < created_at;");
            migrationBuilder.Sql(
                "UPDATE lft_listings SET cycle = 1 WHERE cycle < 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_grants");

            migrationBuilder.DropTable(
                name: "opening_applications");

            migrationBuilder.DropTable(
                name: "recruitment_blocks");

            migrationBuilder.DropTable(
                name: "team_interests");

            migrationBuilder.DropTable(
                name: "team_openings");

            migrationBuilder.DropIndex(
                name: "IX_lft_listings_expires_at",
                table: "lft_listings");

            migrationBuilder.DropIndex(
                name: "IX_lft_listings_game_published_at",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "availability_hours",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "cycle",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "languages",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "regions",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "roles",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "slots",
                table: "lft_listings");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                table: "lft_listings");

            migrationBuilder.AlterColumn<string>(
                name: "note",
                table: "lft_listings",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_lft_listings_game_expires_at",
                table: "lft_listings",
                columns: new[] { "game", "expires_at" });
        }
    }
}
