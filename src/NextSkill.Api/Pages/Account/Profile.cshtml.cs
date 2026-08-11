using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Auth;

namespace NextSkill.Api.Pages.Account;

/// <summary>
/// The signed-in account surface at <c>/me</c> (decision 029).
/// The setinfo token is the first mutation hosted here. It is rotate-only: the
/// stored hash can report that a token exists, but can never retrieve one.
/// </summary>
[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class ProfileModel(
    NextSkillDb db,
    ExternalAccountLinks externalLinks,
    AccountDeletion accountDeletion) : PageModel
{
    public sealed class DeleteAccountForm
    {
        [Required(ErrorMessage = "Podaj aktualne hasło.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Wpisz swój nick, aby potwierdzić usunięcie.")]
        public string ConfirmationNickname { get; set; } = "";
    }

    public Guid AccountId { get; private set; }
    public string Nickname { get; private set; } = "";
    public string Email { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public bool SetinfoConfigured { get; private set; }
    public bool SteamLinked { get; private set; }
    public bool DiscordLinked { get; private set; }
    public TwoFactorMethod? TwoFactorMethod { get; private set; }
    public int ActiveSessions { get; private set; }
    public bool SteamLinkAvailable { get; private set; }
    public bool DiscordLinkAvailable { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Notice { get; set; }

    [BindProperty]
    public DeleteAccountForm DeleteAccount { get; set; } = new();

    public string? NoticeMessage => Notice switch
    {
        "steam-linked" => "Konto Steam zostało połączone.",
        "discord-linked" => "Konto Discord zostało połączone.",
        "steam-unlinked" => "Konto Steam zostało odłączone.",
        "discord-unlinked" => "Konto Discord zostało odłączone.",
        "link-in-use" => "To konto zewnętrzne jest już połączone z innym graczem.",
        "link-rejected" => "Dostawca nie potwierdził tożsamości. Rozpocznij łączenie ponownie.",
        "link-invalid" => "Próba połączenia wygasła albo nie pochodzi z tej sesji.",
        "link-unavailable" => "Ten dostawca nie jest jeszcze skonfigurowany.",
        _ => null,
    };

    /// <summary>
    /// The copy-ready command produced by THIS request. It is never persisted,
    /// never put in TempData/cookies, and disappears on the next GET.
    /// </summary>
    public string? FreshSetinfoCommand { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();
        return await LoadAsync(accountId) ? Page() : Challenge();
    }

    public async Task<IActionResult> OnPostRotateSetinfoAsync()
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId);
        if (account is null) return Challenge();

        var (token, hash) = SetinfoTokens.Create();
        account.SetinfoTokenHash = hash;
        await db.SaveChangesAsync();

        // Render directly rather than redirecting through TempData. The default
        // TempData provider is cookie-backed, which would put this credential in
        // the browser's cookie jar even if encrypted.
        FreshSetinfoCommand = SetinfoTokens.Command(token);

        return await LoadAsync(accountId) ? Page() : Challenge();
    }

    public async Task<IActionResult> OnPostLogoutEverywhereAsync()
    {
        if (Sessions.AccountIdOf(User) is null) return Challenge();

        await Sessions.SignOutEverywhereAsync(HttpContext, db);
        return Redirect($"/{Locales.Current}/account/login?allSessionsRevoked=true");
    }

    public async Task<IActionResult> OnPostDeleteAccountAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        if (!ModelState.IsValid)
            return await LoadAsync(accountId) ? Page() : Challenge();

        var result = await accountDeletion.DeleteAsync(
            accountId,
            DeleteAccount.Password,
            DeleteAccount.ConfirmationNickname,
            cancellationToken);

        if (!result.Success)
        {
            if (result.Error == AccountDeletionError.AccountMissing) return Challenge();

            ModelState.AddModelError(string.Empty, result.Error switch
            {
                AccountDeletionError.ReauthenticationFailed =>
                    "Hasło lub wpisany nick są nieprawidłowe.",
                AccountDeletionError.SoleTeamCaptain =>
                    "Najpierw przekaż kapitanat albo rozwiąż drużynę, której jesteś jedynym kapitanem.",
                AccountDeletionError.SoleOrganiserOwner =>
                    "Najpierw przekaż własność organizatora innemu właścicielowi.",
                AccountDeletionError.LastPlatformOwner =>
                    "Ostatni właściciel platformy musi najpierw przekazać tę rolę.",
                _ => "Nie udało się usunąć konta.",
            });

            return await LoadAsync(accountId) ? Page() : Challenge();
        }

        await Sessions.SignOutAsync(HttpContext, db);
        return Redirect($"/{Locales.Current}/account/login?accountDeleted=true");
    }

    public Task<IActionResult> OnPostLinkSteamAsync() => StartLinkAsync(ExternalAccountProvider.Steam);
    public Task<IActionResult> OnPostLinkDiscordAsync() => StartLinkAsync(ExternalAccountProvider.Discord);

    public Task<IActionResult> OnGetSteamCallbackAsync(string? state, CancellationToken cancellationToken) =>
        CompleteLinkAsync(ExternalAccountProvider.Steam, state, cancellationToken);

    public Task<IActionResult> OnGetDiscordCallbackAsync(string? state, CancellationToken cancellationToken) =>
        CompleteLinkAsync(ExternalAccountProvider.Discord, state, cancellationToken);

    public Task<IActionResult> OnPostUnlinkSteamAsync(CancellationToken cancellationToken) =>
        UnlinkAsync(ExternalAccountProvider.Steam, cancellationToken);

    public Task<IActionResult> OnPostUnlinkDiscordAsync(CancellationToken cancellationToken) =>
        UnlinkAsync(ExternalAccountProvider.Discord, cancellationToken);

    private async Task<IActionResult> StartLinkAsync(ExternalAccountProvider provider)
    {
        var start = externalLinks.Start(HttpContext, provider);
        if (start.Success) return Redirect(start.Redirect!.AbsoluteUri);

        ModelState.AddModelError(string.Empty, start.Error!);
        if (Sessions.AccountIdOf(User) is not { } accountId || !await LoadAsync(accountId))
            return Challenge();
        return Page();
    }

    private async Task<IActionResult> CompleteLinkAsync(
        ExternalAccountProvider provider,
        string? state,
        CancellationToken cancellationToken)
    {
        var result = await externalLinks.CompleteAsync(HttpContext, provider, state, cancellationToken);
        return LocalRedirect(ProfileUrl(result.Notice));
    }

    private async Task<IActionResult> UnlinkAsync(
        ExternalAccountProvider provider,
        CancellationToken cancellationToken)
    {
        var notice = await externalLinks.UnlinkAsync(User, provider, cancellationToken);
        return LocalRedirect(ProfileUrl(notice));
    }

    private static string ProfileUrl(string notice) =>
        $"/{Locales.Current}/me?notice={Uri.EscapeDataString(notice)}";

    private async Task<bool> LoadAsync(Guid accountId)
    {

        var account = await db.Accounts
            .Where(a => a.Id == accountId && a.DeletedAt == null)
            .Select(a => new
            {
                a.Id,
                a.Nickname,
                a.Email,
                a.CreatedAt,
                SetinfoConfigured = a.SetinfoTokenHash != null,
                SteamLinked = a.SteamId != null,
                DiscordLinked = a.DiscordId != null,
                a.TwoFactorMethod,
            })
            .AsNoTracking()
            .SingleOrDefaultAsync();

        if (account is null) return false;

        AccountId = account.Id;
        Nickname = account.Nickname;
        Email = account.Email;
        CreatedAt = account.CreatedAt;
        SetinfoConfigured = account.SetinfoConfigured;
        SteamLinked = account.SteamLinked;
        DiscordLinked = account.DiscordLinked;
        TwoFactorMethod = account.TwoFactorMethod;
        SteamLinkAvailable = externalLinks.CanStart(ExternalAccountProvider.Steam);
        DiscordLinkAvailable = externalLinks.CanStart(ExternalAccountProvider.Discord);

        var now = DateTimeOffset.UtcNow;
        ActiveSessions = await db.Sessions.CountAsync(s => s.AccountId == accountId && s.ExpiresAt > now);
        return true;
    }
}
