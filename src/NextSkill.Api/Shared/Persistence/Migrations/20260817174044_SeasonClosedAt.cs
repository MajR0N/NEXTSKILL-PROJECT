using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeasonClosedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "closed_at",
                table: "seasons",
                type: "timestamp with time zone",
                nullable: true);

            // Any season already closed predates this column, so it has no
            // recoverable closing instant. Its end date is the best honest
            // approximation and beats leaving a row that the constraint below
            // would reject.
            migrationBuilder.Sql(
                "UPDATE seasons SET closed_at = COALESCE(ends_at, starts_at) " +
                "WHERE closed AND closed_at IS NULL;");

            // ── the freeze cannot be half-applied (051 §4) ─────────────────
            //
            // A closed season whose closing instant was never recorded is a
            // season whose archive cannot be reconstructed — the table IS the
            // ledger read as at that moment, so without it there is nothing to
            // read as at. Nothing in the application closes a season yet; the
            // constraint exists now so that whatever eventually does cannot get
            // it wrong, in the same spirit as one-live-match-per-server (007).
            migrationBuilder.Sql("""
                ALTER TABLE seasons
                ADD CONSTRAINT ck_closed_season_records_when
                CHECK (NOT closed OR closed_at IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE seasons DROP CONSTRAINT IF EXISTS ck_closed_season_records_when;");

            migrationBuilder.DropColumn(
                name: "closed_at",
                table: "seasons");
        }
    }
}
