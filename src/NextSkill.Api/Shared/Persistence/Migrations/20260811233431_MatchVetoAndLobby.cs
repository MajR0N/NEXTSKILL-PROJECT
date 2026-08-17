using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchVetoAndLobby : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_maps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    map = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    team_a_starts_as = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_maps", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_maps_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_vetos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_a_ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    team_b_ready_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    turn_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    best_of = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    map_pool = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_vetos", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_vetos_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "match_veto_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_veto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    map = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    side = table.Column<string>(type: "text", nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_veto_actions", x => x.id);
                    table.ForeignKey(
                        name: "FK_match_veto_actions_match_vetos_match_veto_id",
                        column: x => x.match_veto_id,
                        principalTable: "match_vetos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_one_map_per_match_order",
                table: "match_maps",
                columns: new[] { "match_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_action_per_veto_step",
                table: "match_veto_actions",
                columns: new[] { "match_veto_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_veto_per_match",
                table: "match_vetos",
                column: "match_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_maps");

            migrationBuilder.DropTable(
                name: "match_veto_actions");

            migrationBuilder.DropTable(
                name: "match_vetos");
        }
    }
}
