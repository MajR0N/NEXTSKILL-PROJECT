using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ban_reason",
                table: "accounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "banned_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "banned_by_account_id",
                table: "accounts",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ban_reason",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "banned_at",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "banned_by_account_id",
                table: "accounts");
        }
    }
}
