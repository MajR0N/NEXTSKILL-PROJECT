using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BracketPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_bye",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_conditional",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "loser_to_match_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "loser_to_slot",
                table: "matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "round",
                table: "matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // "" is not a BracketSide. Existing matches predate double
            // elimination entirely, so Upper is the truthful backfill — the
            // fourth time EF has produced an empty-string default for an enum
            // column in this project, and the fourth time it needed correcting.
            migrationBuilder.AddColumn<string>(
                name: "side",
                table: "matches",
                type: "text",
                nullable: false,
                defaultValue: "Upper");

            migrationBuilder.AddColumn<Guid>(
                name: "winner_team_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "winner_to_match_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "winner_to_slot",
                table: "matches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_matches_loser_to_match_id",
                table: "matches",
                column: "loser_to_match_id");

            migrationBuilder.CreateIndex(
                name: "IX_matches_winner_to_match_id",
                table: "matches",
                column: "winner_to_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_one_match_per_bracket_position",
                table: "matches",
                columns: new[] { "tournament_id", "side", "round", "position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_matches_matches_loser_to_match_id",
                table: "matches",
                column: "loser_to_match_id",
                principalTable: "matches",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_matches_matches_winner_to_match_id",
                table: "matches",
                column: "winner_to_match_id",
                principalTable: "matches",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_matches_matches_loser_to_match_id",
                table: "matches");

            migrationBuilder.DropForeignKey(
                name: "FK_matches_matches_winner_to_match_id",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_loser_to_match_id",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_winner_to_match_id",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "ix_one_match_per_bracket_position",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "is_bye",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "is_conditional",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "loser_to_match_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "loser_to_slot",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "position",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "round",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "side",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "winner_team_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "winner_to_match_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "winner_to_slot",
                table: "matches");
        }
    }
}
