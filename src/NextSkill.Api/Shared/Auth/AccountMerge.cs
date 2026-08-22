using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Shared.Auth;

public enum AccountMergeError
{
    None,
    SameAccount,
    AccountMissing,
    ConfirmationFailed,
    SharedMatch,
    SharedTeam,
    SharedOrganiser,
    LastPlatformOwner,
}

public sealed record AccountMergeResult(bool Success, AccountMergeError Error, int MovedMatches = 0)
{
    public static AccountMergeResult Merged(int movedMatches) => new(true, AccountMergeError.None, movedMatches);
    public static AccountMergeResult Refused(AccountMergeError error) => new(false, error);
}

/// <summary>
/// One person, two accounts (decision 015).
///
/// [043](043-page-contents.md) chose a single account model precisely so this
/// would be rare — an organiser is nearly always a player too — but rare is not
/// never: someone registers twice before linking Steam, or gets a
/// <c>setinfo</c> token onto the wrong account.
///
/// <b>A merge moves history; it never decides which of two roles, rosters or
/// results was the real one.</b> That single rule is why this file has no
/// arbitration logic in it: wherever the two accounts genuinely coexisted — the
/// same match, the same live roster, the same organiser — the merge is
/// <i>refused</i> and a human resolves the overlap first. Guessing there would
/// silently rewrite a competitive record, which is the one thing the platform
/// sells.
/// </summary>
public sealed class AccountMerge(NextSkillDb db)
{
    /// <param name="confirmationNickname">
    /// The <b>losing</b> account's exact nickname, typed by the operator — the
    /// same confirmation shape as disbanding a team and erasing an account
    /// (045, 011). A merge cannot be undone by clicking again.
    /// </param>
    public async Task<AccountMergeResult> MergeAsync(
        Guid loserId,
        Guid survivorId,
        string confirmationNickname,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (loserId == survivorId) return AccountMergeResult.Refused(AccountMergeError.SameAccount);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Both rows, locked in id order. The ordering is what stops two staff
        // merging the same pair in opposite directions from deadlocking; the
        // lock itself serialises this with sign-in, exactly as erasure does.
        var ids = new[] { loserId, survivorId };
        var accounts = await db.Accounts
            .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);

        var loser = accounts.FirstOrDefault(a => a.Id == loserId);
        var survivor = accounts.FirstOrDefault(a => a.Id == survivorId);

        if (loser is null || survivor is null || loser.DeletedAt is not null || survivor.DeletedAt is not null)
            return AccountMergeResult.Refused(AccountMergeError.AccountMissing);

        if (!string.Equals(confirmationNickname.Trim(), loser.Nickname, StringComparison.Ordinal))
            return AccountMergeResult.Refused(AccountMergeError.ConfirmationFailed);

        // ── the three overlaps, refused rather than arbitrated ───────────
        //
        // Cross-tenant by nature: one person plays in many organisers'
        // tournaments, so asking "where did these two identities coexist" cannot
        // be answered inside one tenant's filter. Scoped to two account ids, so
        // it can only ever see those two people's rows (036).
        var participants = db.MatchParticipants.IgnoreQueryFilters();

        // Two identities in one match is not a duplicate account — it is one
        // human counted twice in a result, and the ledger's unique index would
        // reject the move anyway.
        if (await participants.AnyAsync(p =>
                p.AccountId == loserId &&
                participants.Any(o => o.AccountId == survivorId && o.MatchId == p.MatchId),
                cancellationToken))
            return AccountMergeResult.Refused(AccountMergeError.SharedMatch);

        if (await db.TeamMemberships.AnyAsync(m =>
                m.AccountId == loserId && m.LeftAt == null &&
                db.TeamMemberships.Any(o =>
                    o.AccountId == survivorId && o.TeamId == m.TeamId && o.LeftAt == null),
                cancellationToken))
            return AccountMergeResult.Refused(AccountMergeError.SharedTeam);

        var staff = db.OrganiserStaff.IgnoreQueryFilters();
        if (await staff.AnyAsync(s =>
                s.AccountId == loserId &&
                staff.Any(o => o.AccountId == survivorId && o.OrganiserId == s.OrganiserId),
                cancellationToken))
            return AccountMergeResult.Refused(AccountMergeError.SharedOrganiser);

        // The same guard erasure applies, for the same reason: a platform with
        // no owner has nobody who can appoint one.
        if (loser.PlatformRole == PlatformRole.Owner &&
            !await db.Accounts.AnyAsync(a =>
                a.Id != loserId && a.DeletedAt == null && a.PlatformRole == PlatformRole.Owner,
                cancellationToken))
            return AccountMergeResult.Refused(AccountMergeError.LastPlatformOwner);

        // ── the merge itself ─────────────────────────────────────────────

        var movedMatches = await participants
            .Where(p => p.AccountId == loserId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AccountId, survivorId), cancellationToken);

        await db.TeamMemberships
            .Where(m => m.AccountId == loserId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AccountId, survivorId), cancellationToken);

        await staff
            .Where(s => s.AccountId == loserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AccountId, survivorId), cancellationToken);

        var steamId = loser.SteamId;
        var discordId = loser.DiscordId;
        var setinfoTokenHash = loser.SetinfoTokenHash;
        var phoneNumber = loser.PhoneNumber;
        var phoneVerifiedAt = loser.PhoneVerifiedAt;

        // Saved on its own, BEFORE the survivor claims anything: Steam and
        // Discord ids are unique indexes, and PostgreSQL checks those per
        // statement rather than at commit — so the losing row has to let go
        // first, whatever order EF would otherwise have picked.
        await AccountDeletion.TombstoneAsync(db, loser, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Only what the survivor does not already have. A merge never overwrites
        // a live identity with a dead one.
        survivor.SteamId ??= steamId;
        survivor.DiscordId ??= discordId;
        survivor.SetinfoTokenHash ??= setinfoTokenHash;
        if (survivor.PhoneNumber is null)
        {
            survivor.PhoneNumber = phoneNumber;
            survivor.PhoneVerifiedAt = phoneVerifiedAt;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Every affected result is now about a different player, so the numbers
        // are restated the only way that stays correct — the same full replay a
        // reversed result uses. The sweeper rebuilds; the loser has no
        // participation left, so no rating is regenerated for them.
        //
        // Superseded ledger rows keep the losing account's subject id, exactly
        // as audit rows keep its actor id: both record what was true when they
        // were written, and a merge is not a licence to edit history.
        await DisputeResolution.ReplayAsync(db, now, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return AccountMergeResult.Merged(movedMatches);
    }
}
