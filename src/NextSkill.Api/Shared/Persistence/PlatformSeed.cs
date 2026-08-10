using Microsoft.EntityFrameworkCore;

namespace NextSkill.Api.Shared.Persistence;

/// <summary>
/// Ensures the rows the platform cannot function without.
///
/// This is <b>not sample data</b>. NEXTSKILL is its own first organiser
/// (decision 043), so the organiser row is structural: without it there is
/// nowhere to hang first-party open cups, and the cold-start problem returns —
/// a new player picks "I want to play" and finds an empty tournament list.
///
/// Idempotent, and runs on every start in every environment.
/// </summary>
public static class PlatformSeed
{
    /// <summary>
    /// Fixed id, not generated. The platform organiser is referenced by
    /// configuration, by seed data and eventually by links — so it must be the
    /// same row in development, staging and production.
    /// </summary>
    public static readonly Guid PlatformOrganiserId = new("00000000-0000-0000-0000-000000000001");

    public const string PlatformSlug = "nextskill";

    public static async Task EnsureAsync(NextSkillDb db, CancellationToken ct = default)
    {
        // Organiser is not tenant-owned, so no filter to work around here.
        var exists = await db.Organisers.AnyAsync(o => o.Id == PlatformOrganiserId, ct);
        if (!exists)
        {
            db.Organisers.Add(new Organiser
            {
                Id = PlatformOrganiserId,
                Name = "NEXTSKILL",
                Slug = PlatformSlug,

                // Trusted, because we are not going to fix our own results.
                // Note what this does and does not buy: it skips the
                // team-confirmation gate, but NEVER the integrity gate — a
                // disputed result still blocks the bracket, even for us
                // (decisions 007, 035).
                Trusted = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        // Season 1 — the league format and the archive both assume a season
        // exists (decision 021).
        if (!await db.Seasons.AnyAsync(s => s.Number == 1, ct))
        {
            db.Seasons.Add(new Season
            {
                Id = Guid.NewGuid(),
                Number = 1,
                Name = "Sezon 1",
                StartsAt = DateTimeOffset.UtcNow,
                Closed = false,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
