using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvertModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "moderated_at",
                table: "team_openings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "moderated_at",
                table: "lft_listings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "advert_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lft_listing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_opening_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    removed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_advert_reports", x => x.id);
                    table.CheckConstraint("ck_advert_report_one_target", "(lft_listing_id IS NOT NULL)::int + (team_opening_id IS NOT NULL)::int = 1");
                    table.ForeignKey(
                        name: "FK_advert_reports_lft_listings_lft_listing_id",
                        column: x => x.lft_listing_id,
                        principalTable: "lft_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_advert_reports_team_openings_team_opening_id",
                        column: x => x.team_opening_id,
                        principalTable: "team_openings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_advert_reports_created_at",
                table: "advert_reports",
                column: "created_at",
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_advert_reports_lft_listing_id",
                table: "advert_reports",
                column: "lft_listing_id");

            migrationBuilder.CreateIndex(
                name: "IX_advert_reports_team_opening_id",
                table: "advert_reports",
                column: "team_opening_id");

            migrationBuilder.CreateIndex(
                name: "ix_one_report_per_listing",
                table: "advert_reports",
                columns: new[] { "reporter_account_id", "lft_listing_id" },
                unique: true,
                filter: "lft_listing_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_one_report_per_opening",
                table: "advert_reports",
                columns: new[] { "reporter_account_id", "team_opening_id" },
                unique: true,
                filter: "team_opening_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "advert_reports");

            migrationBuilder.DropColumn(
                name: "moderated_at",
                table: "team_openings");

            migrationBuilder.DropColumn(
                name: "moderated_at",
                table: "lft_listings");
        }
    }
}
