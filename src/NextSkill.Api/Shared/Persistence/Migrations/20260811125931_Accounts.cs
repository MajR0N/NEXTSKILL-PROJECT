using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Accounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_accounts_nickname",
                table: "accounts");

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_account_id",
                table: "sessions",
                column: "account_id");

            // Nickname uniqueness is CASE-INSENSITIVE, which needs an index on an
            // expression — something EF has no model-level syntax for, so it is
            // written by hand here and the plain index above was dropped.
            //
            // This is not cosmetic. Without it "Pukawka" and "pukawka" are two
            // separate accounts, and impersonating a known player costs one
            // shift key (011, 040).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_accounts_nickname_lower ON accounts (lower(nickname));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accounts_nickname_lower;");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_nickname",
                table: "accounts",
                column: "nickname",
                unique: true);
        }
    }
}
