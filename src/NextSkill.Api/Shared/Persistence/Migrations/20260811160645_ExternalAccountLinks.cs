using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalAccountLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "steam_id",
                table: "accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_discord_id",
                table: "accounts",
                column: "discord_id",
                unique: true,
                filter: "discord_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_steam_id",
                table: "accounts",
                column: "steam_id",
                unique: true,
                filter: "steam_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_discord_id",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "IX_accounts_steam_id",
                table: "accounts");

            migrationBuilder.AlterColumn<string>(
                name: "steam_id",
                table: "accounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "discord_id",
                table: "accounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
