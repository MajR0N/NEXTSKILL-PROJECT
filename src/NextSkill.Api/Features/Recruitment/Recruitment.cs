using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Recruitment;

namespace NextSkill.Api.Features.Recruitment;

/// <summary>
/// The state transitions behind the board (decision 056).
///
/// <b>Allow-listed commands, never entity binding.</b> Every method here is a
/// named thing a person can do; there is no path that takes a state from a form
/// field and writes it, which is how a "withdraw" button becomes an "accept"
/// button for anyone willing to edit HTML.
/// </summary>
public static class Recruitment
{
    /// <summary>Why an approach is refused, or null when it may proceed.</summary>
    public static async Task<string?> RejectApplicationAsync(
        NextSkillDb db, TeamOpening opening, Guid accountId, DateTimeOffset now, CancellationToken ct)
    {
        if (opening.ClosedAt is not null || opening.ExpiresAt <= now)
            return "To ogłoszenie jest już nieaktywne.";

        // The designated captain is the only identity ever revealed. Without one,
        // accepting would have nobody to reveal — so approaches pause rather
        // than quietly exposing a different captain (056).
        if (opening.ContactCaptainAccountId is null)
            return "Ogłoszenie czeka na wskazanie kapitana do kontaktu.";

        if (await db.RecruitmentBlocks.AnyAsync(
                b => b.AccountId == accountId && b.TeamId == opening.TeamId, ct))
            return "Nie można wysłać zgłoszenia do tej drużyny.";

        // Already on this roster: the board is for finding a team, not for
        // applying to your own.
        if (await db.TeamMemberships.AnyAsync(
                m => m.TeamId == opening.TeamId && m.AccountId == accountId && m.LeftAt == null, ct))
            return "Już należysz do tej drużyny.";

        if (await db.OpeningApplications.AnyAsync(
                a => a.TeamOpeningId == opening.Id && a.AccountId == accountId && a.Cycle == opening.Cycle, ct))
            return "Zgłoszenie do tego ogłoszenia już zostało wysłane.";

        var pending = await db.OpeningApplications.CountAsync(
            a => a.AccountId == accountId && a.State == RecruitmentState.Pending, ct);

        if (pending >= RecruitmentRules.MaxPendingPerSide)
            return $"Masz już {RecruitmentRules.MaxPendingPerSide} oczekujących zgłoszeń.";

        return null;
    }

    /// <summary>Why a team approach is refused, or null when it may proceed.</summary>
    public static async Task<string?> RejectInterestAsync(
        NextSkillDb db, TeamOpening opening, LftListing listing, DateTimeOffset now, CancellationToken ct)
    {
        if (opening.ClosedAt is not null || opening.ExpiresAt <= now)
            return "To ogłoszenie jest już nieaktywne.";

        // A team may only approach somebody who is actually advertising, in the
        // same game. Without both, this is a context-free direct message — the
        // exact thing 056 refuses to build.
        if (listing.ClosedAt is not null || listing.ExpiresAt <= now)
            return "Ten gracz nie ma aktywnego ogłoszenia.";

        if (listing.Game != opening.Game)
            return "Ogłoszenie gracza dotyczy innej gry.";

        if (await db.RecruitmentBlocks.AnyAsync(
                b => b.AccountId == listing.AccountId && b.TeamId == opening.TeamId, ct))
            return "Nie można wysłać zaproszenia do tego gracza.";

        if (await db.TeamInterests.AnyAsync(
                i => i.TeamOpeningId == opening.Id && i.AccountId == listing.AccountId && i.Cycle == opening.Cycle, ct))
            return "Ten gracz został już zaproszony do tego ogłoszenia.";

        // Per TEAM, not per opening. Counting per opening would let a team with
        // five vacancies approach fifty people, which is the invite-spam hole
        // 045 closed arriving through a different door (051 §5).
        var openings = db.TeamOpenings.Where(o => o.TeamId == opening.TeamId).Select(o => o.Id);
        var pending = await db.TeamInterests.CountAsync(
            i => openings.Contains(i.TeamOpeningId) && i.State == RecruitmentState.Pending, ct);

        if (pending >= RecruitmentRules.MaxPendingPerSide)
            return $"Drużyna ma już {RecruitmentRules.MaxPendingPerSide} oczekujących zaproszeń.";

        return null;
    }

    /// <summary>
    /// Both sides agreed. Records the grant that lets exactly one contact
    /// detail cross, in each direction, once.
    ///
    /// <b>This is a conversation starting, not recruitment succeeding.</b> Only
    /// acceptance of the resulting roster invitation is success, and the two
    /// stay separately measurable so a marketing claim cannot quietly use the
    /// larger number (056).
    /// </summary>
    public static ContactGrant Accept(
        NextSkillDb db, TeamOpening opening, Guid accountId, DateTimeOffset now)
    {
        var grant = new ContactGrant
        {
            Id = Guid.NewGuid(),
            TeamOpeningId = opening.Id,
            AccountId = accountId,
            // Frozen at acceptance. If the captain changes later, the person who
            // was told a name is not retroactively told a different one.
            CaptainAccountId = opening.ContactCaptainAccountId!.Value,
            CreatedAt = now,
            ExpiresAt = now + RecruitmentRules.ContactGrantLifetime,
        };

        db.ContactGrants.Add(grant);
        return grant;
    }

    /// <summary>
    /// Closes every unanswered request from a superseded advert cycle.
    ///
    /// <b>Neutral, not a rejection.</b> The advert changed under them; saying
    /// "declined" would attribute a decision nobody made, and a decline also
    /// carries a resubmission block this must not impose.
    /// </summary>
    public static async Task CloseOldCycleAsync(
        NextSkillDb db, Guid openingId, int newCycle, DateTimeOffset now, CancellationToken ct)
    {
        await db.OpeningApplications
            .Where(a => a.TeamOpeningId == openingId && a.Cycle < newCycle && a.State == RecruitmentState.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.State, RecruitmentState.ListingChanged)
                .SetProperty(a => a.ResolvedAt, now), ct);

        await db.TeamInterests
            .Where(i => i.TeamOpeningId == openingId && i.Cycle < newCycle && i.State == RecruitmentState.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.State, RecruitmentState.ListingChanged)
                .SetProperty(i => i.ResolvedAt, now), ct);
    }

    /// <summary>
    /// The contact detail a person chose to share, or null.
    ///
    /// <b>Only ever reached through an accepted, live grant.</b> Provider
    /// identifiers appear in no public projection anywhere, which is why this
    /// returns a display string built here rather than exposing the account.
    /// </summary>
    public static async Task<string?> SharableContactAsync(
        NextSkillDb db, Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId && a.DeletedAt == null)
            .Select(a => new { a.DiscordId, a.SteamId })
            .FirstOrDefaultAsync(ct);

        if (account is null) return null;

        // Discord first, deliberately: it keeps this usable for the non-Steam
        // CS 1.6 players who are the moat, and Steam ownership is never an
        // implicit requirement (056).
        if (account.DiscordId is not null) return $"Discord: {account.DiscordId}";
        if (account.SteamId is not null) return $"Steam: {account.SteamId}";
        return null;
    }
}
