using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Teams;

namespace NextSkill.Api.Features.Matches;

/// <summary>
/// Who played (decision 050 §§1–2).
///
/// <b>Rows are written at <c>match_loaded</c>, not at bracket generation.</b>
/// 023 generates the whole bracket up front, so rounds 2+ have no teams yet and
/// nothing to snapshot. Writing at load also means a match nobody ever played
/// correctly has no participants at all, rather than a confident list of people
/// who never connected.
/// </summary>
public static class MatchParticipants
{
    /// <summary>
    /// Freezes both lineups if they are not frozen already.
    ///
    /// Idempotent by the unique index and by this early return together: the
    /// plugin retries, and a retried <c>match_loaded</c> must not mint a second
    /// set of link codes for people who already have one.
    /// </summary>
    public static async Task DeclareAsync(
        NextSkillDb db, Match match, Shared.Auth.SecretProtector secrets, CancellationToken ct = default)
    {
        if (match.TeamAId is null && match.TeamBId is null) return;

        if (await db.MatchParticipants.AnyAsync(p => p.MatchId == match.Id, ct)) return;

        // 1v1, 2v2 or 5v5 — the format is the cap (045).
        var lineupSize = (int)await db.Tournaments
            .Where(t => t.Id == match.TournamentId)
            .Select(t => t.TeamSize)
            .FirstAsync(ct);

        foreach (var teamId in new[] { match.TeamAId, match.TeamBId })
        {
            if (teamId is not { } team) continue;

            // Layer 1 of 050 §2: the lineup DEFAULTS, and no captain action is
            // needed for a team to have a valid one. Starters before
            // substitutes, then longest-serving first — a stable order, so the
            // same roster always declares the same five.
            //
            // Coaches are excluded structurally rather than by a filter here:
            // IsFieldable is the single place that rule lives (045).
            var roster = await db.TeamMemberships
                .Where(m => m.TeamId == team && m.JoinedAt != null && m.LeftAt == null)
                .OrderBy(m => m.Slot)
                .ThenBy(m => m.JoinedAt)
                .Select(m => new { m.AccountId, m.Slot })
                .ToListAsync(ct);

            var lineup = roster
                .Where(m => RosterRules.IsFieldable(m.Slot))
                .Take(lineupSize);

            foreach (var member in lineup)
            {
                db.MatchParticipants.Add(new MatchParticipant
                {
                    Id = Guid.NewGuid(),
                    OrganiserId = match.OrganiserId,
                    MatchId = match.Id,
                    AccountId = member.AccountId,
                    TeamId = team,
                    Slot = member.Slot,
                    Provenance = ParticipantProvenance.Declared,
                    LinkCode = secrets.Protect(NewLinkCode()),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }
    }

    /// <summary>
    /// Promotes one declared row to <see cref="ParticipantProvenance.Bound"/>,
    /// or refuses.
    ///
    /// <b>Both factors, checked here and nowhere else</b> (011). Returns false
    /// for every failure without saying which one failed: the caller is a game
    /// server, and telling it "the token was right but the code was wrong" turns
    /// this into an oracle for harvesting.
    /// </summary>
    public static async Task<bool> TryBindAsync(
        NextSkillDb db,
        Guid matchId,
        Guid accountId,
        string setinfoToken,
        string linkCode,
        Shared.Auth.SecretProtector secrets,
        CancellationToken ct = default)
    {
        // Factor 3 of 011's check list, and the cheapest: is this person even on
        // the roster we froze? An account nobody declared cannot bind at all,
        // however good its credentials are.
        var participant = await db.MatchParticipants
            .FirstOrDefaultAsync(p => p.MatchId == matchId && p.AccountId == accountId, ct);

        if (participant is null || participant.LinkCode is null) return false;

        // Already bound. Single-use falls out of the provenance itself, so there
        // is no consumed flag anyone can forget to set.
        if (participant.Provenance == ParticipantProvenance.Bound) return false;

        var storedHash = await db.Accounts
            .Where(a => a.Id == accountId && a.DeletedAt == null)
            .Select(a => a.SetinfoTokenHash)
            .FirstOrDefaultAsync(ct);

        if (storedHash is null || !Shared.Auth.SetinfoTokens.Verify(setinfoToken, storedHash))
            return false;

        // Decrypted for the comparison and nothing else. An unrecoverable code
        // binds nobody, which fails closed.
        if (secrets.Reveal(participant.LinkCode) is not { } expected) return false;

        // Fixed-time, like the token: a four-character code is short enough that
        // an ordinary string compare leaks its prefix to a patient caller.
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected),
                System.Text.Encoding.UTF8.GetBytes(linkCode)))
            return false;

        participant.Provenance = ParticipantProvenance.Bound;
        participant.BoundAt = DateTimeOffset.UtcNow;
        participant.LinkCode = null;
        return true;
    }

    /// <summary>
    /// Four characters from an unambiguous alphabet — no O/0, no I/1/L.
    ///
    /// Someone reads this off a web page and types it into a game console with
    /// a match waiting, so the failure this guards against is a misread
    /// character, not a brute-force search. Guessing is already pointless: a
    /// correct guess is worthless without the victim's config token.
    /// </summary>
    private static string NewLinkCode() =>
        RandomNumberGenerator.GetString("23456789ABCDEFGHJKMNPQRSTUVWXYZ", 4);
}
