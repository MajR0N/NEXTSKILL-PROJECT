using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organiser_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    retention = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_organiser_id_occurred_at",
                table: "audit_entries",
                columns: new[] { "organiser_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_retention_occurred_at",
                table: "audit_entries",
                columns: new[] { "retention", "occurred_at" });

            // ── append-only, enforced by the database (012 §7, 036) ────────
            //
            // "Staff can read it; nobody can edit it." A convention that no code
            // path issues an UPDATE is not that promise — it is a promise about
            // the code we have written so far, which is exactly the kind of
            // guarantee this schema has repeatedly refused to settle for
            // (partial unique indexes, routing as real foreign keys).
            //
            // UPDATE is blocked and DELETE deliberately is NOT: 051 §7 keeps a
            // retention_class column precisely so security rows can be swept on
            // a different clock later, and a trigger forbidding DELETE would
            // make that impossible without a migration. Editing history and
            // expiring it are different acts.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit_entries_are_append_only()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_entries is append-only (decisions 012, 036)';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER audit_entries_no_update
                BEFORE UPDATE ON audit_entries
                FOR EACH ROW EXECUTE FUNCTION audit_entries_are_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_entries_no_update ON audit_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_entries_are_append_only();");

            migrationBuilder.DropTable(
                name: "audit_entries");
        }
    }
}
