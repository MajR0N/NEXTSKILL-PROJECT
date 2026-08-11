using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiscordTournamentAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "registration_open_revision",
                table: "tournaments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "discord_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    deduplication_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    abandoned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discord_announcements", x => x.id);
                    table.ForeignKey(
                        name: "FK_discord_announcements_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_discord_announcements_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tournament_discord_webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tournament_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_url = table.Column<string>(type: "text", nullable: true),
                    configured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tournament_discord_webhooks", x => x.id);
                    table.ForeignKey(
                        name: "FK_tournament_discord_webhooks_tournaments_tournament_id",
                        column: x => x.tournament_id,
                        principalTable: "tournaments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discord_announcements_match_id",
                table: "discord_announcements",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "IX_discord_announcements_tournament_id",
                table: "discord_announcements",
                column: "tournament_id");

            migrationBuilder.CreateIndex(
                name: "ix_due_discord_announcements",
                table: "discord_announcements",
                column: "next_attempt_at",
                filter: "sent_at IS NULL AND abandoned_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_one_discord_announcement_per_event",
                table: "discord_announcements",
                column: "deduplication_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_discord_webhook_per_tournament",
                table: "tournament_discord_webhooks",
                column: "tournament_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discord_announcements");

            migrationBuilder.DropTable(
                name: "tournament_discord_webhooks");

            migrationBuilder.DropColumn(
                name: "registration_open_revision",
                table: "tournaments");
        }
    }
}
