using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LedgerSupersede : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_one_rating_change_per_match_subject",
                table: "rating_changes");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "superseded_at",
                table: "rating_changes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_rating_change_per_match_subject",
                table: "rating_changes",
                columns: new[] { "match_id", "subject_id", "game" },
                unique: true,
                filter: "match_id IS NOT NULL AND superseded_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_one_rating_change_per_match_subject",
                table: "rating_changes");

            migrationBuilder.DropColumn(
                name: "superseded_at",
                table: "rating_changes");

            migrationBuilder.CreateIndex(
                name: "ix_one_rating_change_per_match_subject",
                table: "rating_changes",
                columns: new[] { "match_id", "subject_id", "game" },
                unique: true,
                filter: "match_id IS NOT NULL");
        }
    }
}
