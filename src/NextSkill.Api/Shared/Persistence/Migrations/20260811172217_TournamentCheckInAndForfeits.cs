using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TournamentCheckInAndForfeits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checked_in_at",
                table: "registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forfeit_reason",
                table: "registrations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "forfeited_at",
                table: "registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "forfeited_by_account_id",
                table: "registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forfeit_reason",
                table: "matches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "forfeited_by_account_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "forfeited_team_id",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "slot_a_resolved",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "slot_b_resolved",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing first-round and already-fed slots are resolved. A null
            // fed by an unfinished match is still pending; a cancelled source
            // is an explicit vacancy. Keeping that distinction is what stops a
            // forfeiting team from reappearing in double elimination.
            migrationBuilder.Sql("""
                UPDATE matches AS target
                SET slot_a_resolved = NOT EXISTS (
                    SELECT 1
                    FROM matches AS source
                    WHERE
                        ((source.winner_to_match_id = target.id AND source.winner_to_slot = 'A')
                         OR (source.loser_to_match_id = target.id AND source.loser_to_slot = 'A'))
                        AND source.winner_team_id IS NULL
                        AND source.status <> 'Cancelled'
                );

                UPDATE matches AS target
                SET slot_b_resolved = NOT EXISTS (
                    SELECT 1
                    FROM matches AS source
                    WHERE
                        ((source.winner_to_match_id = target.id AND source.winner_to_slot = 'B')
                         OR (source.loser_to_match_id = target.id AND source.loser_to_slot = 'B'))
                        AND source.winner_team_id IS NULL
                        AND source.status <> 'Cancelled'
                );

                UPDATE matches
                SET slot_a_resolved = FALSE, slot_b_resolved = FALSE
                WHERE is_conditional;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checked_in_at",
                table: "registrations");

            migrationBuilder.DropColumn(
                name: "forfeit_reason",
                table: "registrations");

            migrationBuilder.DropColumn(
                name: "forfeited_at",
                table: "registrations");

            migrationBuilder.DropColumn(
                name: "forfeited_by_account_id",
                table: "registrations");

            migrationBuilder.DropColumn(
                name: "forfeit_reason",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "forfeited_by_account_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "forfeited_team_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "slot_a_resolved",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "slot_b_resolved",
                table: "matches");
        }
    }
}
