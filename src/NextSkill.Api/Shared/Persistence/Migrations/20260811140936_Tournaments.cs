using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Tournaments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "tournaments",
                type: "text",
                nullable: true);

            // EF's generated defaults were 0 and "" — a tournament nobody can
            // enter, and a team size that is not a TeamSize at all. Backfilled to
            // the values 046 treats as standard, so existing rows land somewhere
            // meaningful rather than somewhere that fails on read.
            migrationBuilder.AddColumn<int>(
                name: "max_teams",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 16);

            migrationBuilder.AddColumn<string>(
                name: "team_size",
                table: "tournaments",
                type: "text",
                nullable: false,
                defaultValue: "Squad");

            migrationBuilder.CreateTable(
                name: "registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registrations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_one_active_entry_per_tournament",
                table: "registrations",
                columns: new[] { "tournament_id", "team_id" },
                unique: true,
                filter: "withdrawn_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_one_team_per_seed",
                table: "registrations",
                columns: new[] { "tournament_id", "seed" },
                unique: true,
                filter: "seed IS NOT NULL AND withdrawn_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "registrations");

            migrationBuilder.DropColumn(
                name: "description",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "max_teams",
                table: "tournaments");

            migrationBuilder.DropColumn(
                name: "team_size",
                table: "tournaments");
        }
    }
}
