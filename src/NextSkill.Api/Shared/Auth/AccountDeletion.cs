using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Shared.Auth;

public enum AccountDeletionError
{
    None,
    ReauthenticationFailed,
    SoleTeamCaptain,
    SoleOrganiserOwner,
    LastPlatformOwner,
    AccountMissing,
}

public sealed record AccountDeletionResult(bool Success, AccountDeletionError Error)
{
    public static AccountDeletionResult Deleted() => new(true, AccountDeletionError.None);
    public static AccountDeletionResult Refused(AccountDeletionError error) => new(false, error);
}

/// <summary>
/// GDPR erasure for an account whose stable id is already part of competition
/// history. The row survives as an anonymous tombstone; credentials, contact
/// details, provider identities and live/community state do not.
/// </summary>
public sealed class AccountDeletion(NextSkillDb db)
{
    public async Task<AccountDeletionResult> DeleteAsync(
        Guid accountId,
        string password,
        string confirmationNickname,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialises deletion with sign-in. Without the row lock, a two-factor
        // challenge already in flight could insert a fresh session just after
        // this operation deleted all existing ones.
        var account = await db.Accounts
            .FromSqlInterpolated($"SELECT * FROM accounts WHERE id = {accountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null || account.DeletedAt is not null)
            return AccountDeletionResult.Refused(AccountDeletionError.AccountMissing);

        var passwordCorrect = Passwords.Verify(password, account.PasswordHash);
        if (!passwordCorrect || !string.Equals(
                confirmationNickname.Trim(), account.Nickname, StringComparison.Ordinal))
            return AccountDeletionResult.Refused(AccountDeletionError.ReauthenticationFailed);

        var soleCaptain = await db.TeamMemberships.AnyAsync(m =>
            m.AccountId == accountId &&
            m.Role == TeamRole.Captain &&
            m.JoinedAt != null &&
            m.LeftAt == null &&
            db.Teams.Any(t => t.Id == m.TeamId && t.DisbandedAt == null) &&
            !db.TeamMemberships.Any(other =>
                other.TeamId == m.TeamId &&
                other.AccountId != accountId &&
                other.Role == TeamRole.Captain &&
                other.JoinedAt != null &&
                other.LeftAt == null), cancellationToken);

        if (soleCaptain)
            return AccountDeletionResult.Refused(AccountDeletionError.SoleTeamCaptain);

        var staff = db.OrganiserStaff.IgnoreQueryFilters();
        var soleOrganiserOwner = await staff.AnyAsync(s =>
            s.AccountId == accountId &&
            s.Role == OrganiserRole.Owner &&
            !staff.Any(other =>
                other.OrganiserId == s.OrganiserId &&
                other.AccountId != accountId &&
                other.Role == OrganiserRole.Owner), cancellationToken);

        if (soleOrganiserOwner)
            return AccountDeletionResult.Refused(AccountDeletionError.SoleOrganiserOwner);

        if (account.PlatformRole == PlatformRole.Owner &&
            !await db.Accounts.AnyAsync(a =>
                a.Id != accountId && a.DeletedAt == null && a.PlatformRole == PlatformRole.Owner,
                cancellationToken))
            return AccountDeletionResult.Refused(AccountDeletionError.LastPlatformOwner);

        var now = DateTimeOffset.UtcNow;
        var originalNickname = account.Nickname;
        var anonymousKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();

        // Pending invitations were never accepted history. Accepted membership
        // rows are retained but closed, preserving past rosters without leaving
        // an anonymous account occupying a live place.
        await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.JoinedAt == null && m.LeftAt == null)
            .ExecuteDeleteAsync(cancellationToken);
        await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.JoinedAt != null && m.LeftAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeftAt, now), cancellationToken);

        await db.Sessions.Where(s => s.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.PasswordResetTokens.Where(t => t.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.RecoveryCodes.Where(c => c.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.OneTimeCodes.Where(c => c.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.Notifications.Where(n => n.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.LftListings.Where(l => l.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // Ephemeral recruitment state goes immediately (056). Accepted roster
        // and rating history still survives behind the anonymous tombstone —
        // deleting results would corrupt tournaments — but a pending approach,
        // a note somebody wrote about themselves and a live contact grant are
        // exactly the transient personal data erasure is for.
        await db.OpeningApplications.Where(a => a.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.TeamInterests.Where(i => i.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);
        await db.ContactGrants
            .Where(g => g.AccountId == accountId || g.CaptainAccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.RecruitmentBlocks.Where(b => b.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // An opening naming this person as its contact loses that designation
        // rather than being deleted: the team's vacancy is the team's, and
        // approaches pause until a replacement captain is chosen.
        await db.TeamOpenings
            .Where(o => o.ContactCaptainAccountId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ContactCaptainAccountId, (Guid?)null), cancellationToken);
        await staff.Where(s => s.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        // Team-invite acceptance is the only current notification payload that
        // duplicates a player's nickname into somebody else's row.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM notifications WHERE data ->> 'player' = {originalNickname}",
            cancellationToken);

        account.Email = $"{anonymousKey}@deleted.invalid";
        account.Nickname = $"Usunięty-{anonymousKey[..12]}";
        account.PasswordHash = $"!deleted-{anonymousKey}";
        account.SetinfoTokenHash = null;
        account.PlatformRole = null;
        account.TwoFactorMethod = null;
        account.TwoFactorSecret = null;
        account.TwoFactorEnabledAt = null;
        account.PhoneNumber = null;
        account.PhoneVerifiedAt = null;
        account.SteamId = null;
        account.DiscordId = null;
        account.DeletedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AccountDeletionResult.Deleted();
    }
}
