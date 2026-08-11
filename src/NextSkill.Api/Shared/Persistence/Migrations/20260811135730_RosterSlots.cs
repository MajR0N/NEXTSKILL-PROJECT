using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RosterSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's generated default was "", which is not a RosterSlot at all —
            // every existing row would have failed to read back as an enum.
            // "Player" is the honest backfill: before slots existed, everyone on
            // a roster was a player (decision 045).
            migrationBuilder.AddColumn<string>(
                name: "slot",
                table: "team_memberships",
                type: "text",
                nullable: false,
                defaultValue: "Player");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "slot",
                table: "team_memberships");
        }
    }
}
