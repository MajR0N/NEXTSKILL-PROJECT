using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
// `Pages.Account` is a sibling namespace, so the bare entity name would resolve to it.
using AccountRow = NextSkill.Api.Shared.Persistence.Account;

namespace NextSkill.Api.Pages.Admin;

/// <summary>
/// Merging one person's two accounts (decision 015).
///
/// Kept off <c>/admin/users</c> deliberately: a ban acts on one account that
/// staff searched for, while a merge acts on a <b>pair</b> and needs a typed
/// confirmation before it runs. Two destructive forms with different shapes on
/// one search-results page is how the wrong button gets pressed.
///
/// <b>Owner-only</b> (<see cref="Operation.MergeAccounts"/>). It shipped on
/// <c>ModerateUsers</c> because 015 lists merges in the same panel section as
/// bans — and a ban ends access while a merge rewrites who a finished result
/// was about and replays every rating on the platform. Setting the
/// <c>trusted</c> flag was already owner-only for considerably less.
/// </summary>
public class MergeModel(
    NextSkillDb db,
    AccountMerge merge,
    TimeProvider clock,
    IStringLocalizer<UiText> text) : PageModel
{
    public sealed class MergeInput
    {
        /// <summary>The account that disappears.</summary>
        public string LosingNickname { get; set; } = "";

        /// <summary>The account that keeps the history.</summary>
        public string SurvivingNickname { get; set; } = "";

        /// <summary>The losing nickname again, typed to confirm.</summary>
        public string Confirmation { get; set; } = "";

        public string Reason { get; set; } = "";
    }

    [BindProperty]
    public MergeInput Input { get; set; } = new();

    public string? Done { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        return AccessPolicy.Can(actor, Operation.MergeAccounts) ? Page() : Forbid();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var now = clock.GetUtcNow();

        if (!AccessPolicy.Can(actor, Operation.MergeAccounts))
        {
            Audit.Record(
                db, actor.AccountId, AuditAction.MergeAccounts, now,
                AuditOutcome.Denied, retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            return Forbid();
        }

        var reason = Input.Reason.Trim();
        if (reason.Length is 0 or > 500)
        {
            ModelState.AddModelError(string.Empty, text["Merge.Error.Reason"]);
            return Page();
        }

        var loser = await FindAsync(Input.LosingNickname, cancellationToken);
        var survivor = await FindAsync(Input.SurvivingNickname, cancellationToken);
        if (loser is null || survivor is null)
        {
            ModelState.AddModelError(string.Empty, text["Merge.Error.NoSuchPlayer"]);
            return Page();
        }

        var result = await merge.MergeAsync(
            loser.Id, survivor.Id, Input.Confirmation, now, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, text[$"Merge.Error.{result.Error}"]);
            return Page();
        }

        // Recorded after the merge committed, because the nicknames it names are
        // read before the losing one is anonymised — and an audit row saying
        // "Usunięty-a1b2c3" would answer nobody's question later.
        Audit.Record(
            db, actor.AccountId, AuditAction.MergeAccounts, now,
            resourceId: loser.Id,
            reason: $"{Input.LosingNickname.Trim()} → {survivor.Nickname} ({result.MovedMatches} meczów): {reason}");
        await db.SaveChangesAsync(cancellationToken);

        Done = survivor.Nickname;
        Input = new MergeInput();
        return Page();
    }

    private Task<AccountRow?> FindAsync(string nickname, CancellationToken cancellationToken)
    {
        var trimmed = nickname.Trim();
        return trimmed.Length == 0
            ? Task.FromResult<AccountRow?>(null)
            : db.Accounts.FirstOrDefaultAsync(
                a => a.DeletedAt == null && a.Nickname.ToLower() == trimmed.ToLower(),
                cancellationToken);
    }
}
