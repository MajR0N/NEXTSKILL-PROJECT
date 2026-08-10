using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServerHmacSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hmac_secret",
                table: "servers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hmac_secret",
                table: "servers");
        }
    }
}
