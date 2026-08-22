using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AvailabilityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_team_openings_availability_hours",
                table: "team_openings",
                column: "availability_hours")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_lft_listings_availability_hours",
                table: "lft_listings",
                column: "availability_hours")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_team_openings_availability_hours",
                table: "team_openings");

            migrationBuilder.DropIndex(
                name: "IX_lft_listings_availability_hours",
                table: "lft_listings");
        }
    }
}
