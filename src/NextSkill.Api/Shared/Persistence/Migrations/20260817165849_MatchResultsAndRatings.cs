using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchResultsAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dispute_reason",
                table: "matches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disputed_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "disputed_by_account_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rated_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "team_a_confirmed_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "team_b_confirmed_at",
                table: "matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "match_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<string>(type: "text", nullable: false),
                    provenance = table.Column<string>(type: "text", nullable: false),
                    link_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    bound_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_participants", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_participants_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rating_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_is_team = table.Column<bool>(type: "boolean", nullable: false),
                    game = table.Column<string>(type: "text", nullable: false),
                    value_before = table.Column<double>(type: "double precision", nullable: true),
                    value_after = table.Column<double>(type: "double precision", nullable: true),
                    deviation_before = table.Column<double>(type: "double precision", nullable: true),
                    deviation_after = table.Column<double>(type: "double precision", nullable: true),
                    volatility_before = table.Column<double>(type: "double precision", nullable: true),
                    volatility_after = table.Column<double>(type: "double precision", nullable: true),
                    excluded_because = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rating_changes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_matches_awaiting_rating",
                table: "matches",
                columns: new[] { "ended_at", "id" },
                filter: "rated_at IS NULL AND ended_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_one_participant_per_match_account",
                table: "match_participants",
                columns: new[] { "match_id", "account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_rating_change_per_match_subject",
                table: "rating_changes",
                columns: new[] { "match_id", "subject_id", "game" },
                unique: true,
                filter: "match_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_rating_changes_subject_id_game_applied_at",
                table: "rating_changes",
                columns: new[] { "subject_id", "game", "applied_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_participants");

            migrationBuilder.DropTable(
                name: "rating_changes");

            migrationBuilder.DropIndex(
                name: "ix_matches_awaiting_rating",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "dispute_reason",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "disputed_at",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "disputed_by_account_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "rated_at",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "team_a_confirmed_at",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "team_b_confirmed_at",
                table: "matches");
        }
    }
}
