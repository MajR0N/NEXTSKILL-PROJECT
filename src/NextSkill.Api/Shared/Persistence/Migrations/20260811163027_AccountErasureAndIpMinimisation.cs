using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountErasureAndIpMinimisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_ip",
                table: "accounts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "accounts");

            migrationBuilder.AddColumn<IPAddress>(
                name: "last_seen_ip",
                table: "accounts",
                type: "inet",
                nullable: true);
        }
    }
}
