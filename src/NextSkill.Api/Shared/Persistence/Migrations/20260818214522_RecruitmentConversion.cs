using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecruitmentConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "from_team_opening_id",
                table: "team_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_memberships_from_team_opening_id",
                table: "team_memberships",
                column: "from_team_opening_id");

            migrationBuilder.AddForeignKey(
                name: "FK_team_memberships_team_openings_from_team_opening_id",
                table: "team_memberships",
                column: "from_team_opening_id",
                principalTable: "team_openings",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_team_memberships_team_openings_from_team_opening_id",
                table: "team_memberships");

            migrationBuilder.DropIndex(
                name: "IX_team_memberships_from_team_opening_id",
                table: "team_memberships");

            migrationBuilder.DropColumn(
                name: "from_team_opening_id",
                table: "team_memberships");
        }
    }
}
