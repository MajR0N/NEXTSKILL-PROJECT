using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TournamentSeasonAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_tournaments_season_id",
                table: "tournaments",
                column: "season_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tournaments_seasons_season_id",
                table: "tournaments",
                column: "season_id",
                principalTable: "seasons",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tournaments_seasons_season_id",
                table: "tournaments");

            migrationBuilder.DropIndex(
                name: "IX_tournaments_season_id",
                table: "tournaments");
        }
    }
}
