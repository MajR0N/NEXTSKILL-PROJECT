using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextSkill.Api.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchUnaccent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // pg_trgm gives similarity indexes for "contains" queries; unaccent
            // strips diacritics. Both ship with PostgreSQL.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");

            // ── the fix 051 §8 recorded ───────────────────────────────────
            //
            // unaccent() is STABLE, not IMMUTABLE, because its dictionary could
            // in principle be redefined. PostgreSQL therefore refuses to index
            // any expression containing it — which means the search SQL 043
            // specified does not execute at all, rather than merely running
            // slowly.
            //
            // The wrapper below is the standard resolution: name the dictionary
            // explicitly so the result really is fixed for a given input, and
            // declare it IMMUTABLE so it can be indexed. The two-argument form
            // is what makes the promise honest — unaccent(text) alone resolves
            // the dictionary from the search path at call time.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.f_unaccent(text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                STRICT
                AS $$ SELECT public.unaccent('public.unaccent', $1) $$;
                """);

            // Trigram indexes over the accent-stripped, lowercased name of every
            // searchable thing. Without these a search is a sequential scan of
            // three tables on the most-linked page in the product.
            migrationBuilder.Sql(
                "CREATE INDEX ix_teams_search ON teams " +
                "USING gin (public.f_unaccent(lower(name)) gin_trgm_ops, public.f_unaccent(lower(tag)) gin_trgm_ops);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_accounts_search ON accounts " +
                "USING gin (public.f_unaccent(lower(nickname)) gin_trgm_ops);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_tournaments_search ON tournaments " +
                "USING gin (public.f_unaccent(lower(name)) gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_tournaments_search;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accounts_search;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_teams_search;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.f_unaccent(text);");

            // The extensions are deliberately left in place: other things may
            // have come to depend on them, and dropping a shared extension is
            // not this migration's business.
        }
    }
}
