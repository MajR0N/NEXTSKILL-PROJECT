using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Persistence;
using Npgsql;

namespace NextSkill.Api.Shared.Auth;

public enum ExternalAccountProvider { Steam, Discord }

public sealed class ExternalLinkOptions
{
    /// <summary>
    /// Canonical HTTPS origin used in provider callback URLs. It is configured,
    /// never inferred from Host, so a forged Host header cannot become an OAuth
    /// redirect URI.
    /// </summary>
    public string? PublicOrigin { get; set; }
    public DiscordLinkOptions Discord { get; set; } = new();
}

public sealed class DiscordLinkOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public interface IExternalIdentityProvider
{
    ExternalAccountProvider Kind { get; }
    bool IsConfigured { get; }
    Uri AuthorizationUri(Uri callback, string state);
    Task<string?> VerifyAsync(HttpRequest request, Uri callback, string state, CancellationToken cancellationToken);
}

/// <summary>
/// Steam's browser OpenID 2.0 flow. Valve still documents this endpoint for
/// linking a verified 64-bit SteamID to an existing web account (011).
/// </summary>
public sealed class SteamOpenIdProvider(IHttpClientFactory clients) : IExternalIdentityProvider
{
    public const string ClientName = "steam-openid";
    private static readonly Uri Endpoint = new("https://steamcommunity.com/openid/login");
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    public ExternalAccountProvider Kind => ExternalAccountProvider.Steam;
    public bool IsConfigured => true; // OpenID linking needs no Valve client secret.

    public Uri AuthorizationUri(Uri callback, string state)
    {
        var returnTo = ExternalAccountLinks.WithQuery(callback, "state", state);
        var realm = new Uri(callback.GetLeftPart(UriPartial.Authority) + "/");
        return ExternalAccountLinks.WithQuery(Endpoint,
        [
            new("openid.ns", "http://specs.openid.net/auth/2.0"),
            new("openid.mode", "checkid_setup"),
            new("openid.return_to", returnTo.AbsoluteUri),
            new("openid.realm", realm.AbsoluteUri),
            new("openid.identity", IdentifierSelect),
            new("openid.claimed_id", IdentifierSelect),
        ]);
    }

    public async Task<string?> VerifyAsync(
        HttpRequest request,
        Uri callback,
        string state,
        CancellationToken cancellationToken)
    {
        var query = request.Query;
        if (query["openid.mode"] != "id_res") return null;

        var expectedReturnTo = ExternalAccountLinks.WithQuery(callback, "state", state).AbsoluteUri;
        if (!string.Equals(query["openid.return_to"], expectedReturnTo, StringComparison.Ordinal)) return null;
        if (!string.Equals(query["openid.op_endpoint"], Endpoint.AbsoluteUri, StringComparison.Ordinal)) return null;

        var claimed = query["openid.claimed_id"].ToString();
        if (claimed.Length > 200 || !string.Equals(query["openid.identity"], claimed, StringComparison.Ordinal))
            return null;

        var steamId = SteamIdFromClaimedId(claimed);
        if (steamId is null) return null;

        // Send only OpenID fields back, to Valve's FIXED endpoint. Trusting an
        // op_endpoint supplied by the callback would turn this verifier into SSRF.
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var (key, values) in query)
        {
            if (!key.StartsWith("openid.", StringComparison.Ordinal) || values.Count != 1) continue;
            var value = values.ToString();
            if (value.Length > 4096 || fields.Count >= 32) return null;
            fields.Add(new(key, value));
        }

        fields.RemoveAll(x => x.Key == "openid.mode");
        fields.Add(new("openid.mode", "check_authentication"));

        using var response = await clients.CreateClient(ClientName).PostAsync(
            Endpoint,
            new FormUrlEncodedContent(fields),
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var valid = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(line => string.Equals(line, "is_valid:true", StringComparison.Ordinal));

        return valid ? steamId : null;
    }

    private static string? SteamIdFromClaimedId(string claimed)
    {
        if (!Uri.TryCreate(claimed, UriKind.Absolute, out var uri)
            || uri.Host != "steamcommunity.com"
            || uri.Scheme is not ("http" or "https")) return null;

        const string prefix = "/openid/id/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var id = uri.AbsolutePath[prefix.Length..];

        return id.Length == 17 && ulong.TryParse(id, out _) ? id : null;
    }
}

/// <summary>
/// Discord authorization-code linking with the minimum <c>identify</c> scope.
/// Access and refresh tokens are used only to read /users/@me and are never
/// persisted; the verified snowflake is the whole link.
/// </summary>
public sealed class DiscordOAuthProvider(
    IHttpClientFactory clients,
    IOptions<ExternalLinkOptions> options) : IExternalIdentityProvider
{
    public const string ClientName = "discord-oauth";
    private static readonly Uri AuthorizationEndpoint = new("https://discord.com/oauth2/authorize");
    private static readonly Uri TokenEndpoint = new("https://discord.com/api/v10/oauth2/token");
    private static readonly Uri UserEndpoint = new("https://discord.com/api/v10/users/@me");
    private readonly DiscordLinkOptions config = options.Value.Discord;

    public ExternalAccountProvider Kind => ExternalAccountProvider.Discord;
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config.ClientId) && !string.IsNullOrWhiteSpace(config.ClientSecret);

    public Uri AuthorizationUri(Uri callback, string state)
    {
        if (!IsConfigured) throw new InvalidOperationException("Discord OAuth is not configured.");

        return ExternalAccountLinks.WithQuery(AuthorizationEndpoint,
        [
            new("response_type", "code"),
            new("client_id", config.ClientId!),
            new("scope", "identify"),
            new("state", state),
            new("redirect_uri", callback.AbsoluteUri),
        ]);
    }

    public async Task<string?> VerifyAsync(
        HttpRequest request,
        Uri callback,
        string state,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured || request.Query.ContainsKey("error")) return null;
        var code = request.Query["code"].ToString();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 1024) return null;

        using var tokenResponse = await clients.CreateClient(ClientName).PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId!,
                ["client_secret"] = config.ClientSecret!,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = callback.AbsoluteUri,
            }),
            cancellationToken);
        if (!tokenResponse.IsSuccessStatusCode) return null;

        var token = await tokenResponse.Content.ReadFromJsonAsync<DiscordToken>(cancellationToken);
        if (token?.AccessToken is not { Length: > 0 and <= 4096 }) return null;

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var userResponse = await clients.CreateClient(ClientName).SendAsync(userRequest, cancellationToken);
        if (!userResponse.IsSuccessStatusCode) return null;

        var user = await userResponse.Content.ReadFromJsonAsync<DiscordUser>(cancellationToken);
        return user?.Id is { Length: > 0 and <= 20 } id && ulong.TryParse(id, out _) ? id : null;
    }

    private sealed record DiscordToken([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record DiscordUser([property: JsonPropertyName("id")] string Id);
}

/// <summary>
/// Issues and consumes the provider correlation state. The protected payload is
/// tied to the current account AND server-side session; the HttpOnly cookie
/// makes it one-browser and one-use in normal operation.
/// </summary>
public sealed class ExternalLinkState(IDataProtectionProvider protection, TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector protector = protection.CreateProtector("NextSkill.ExternalLinkState.v1");

    private sealed record Payload(
        int Version,
        Guid AccountId,
        Guid SessionId,
        ExternalAccountProvider Provider,
        string Nonce,
        DateTimeOffset ExpiresAt);

    public string Issue(HttpContext http, Guid accountId, ExternalAccountProvider provider, Uri callback)
    {
        var sessionId = Sessions.SessionIdOf(http.User)
            ?? throw new InvalidOperationException("External linking requires a live session.");
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(24));
        var payload = new Payload(1, accountId, sessionId, provider, nonce, time.GetUtcNow() + Lifetime);

        http.Response.Cookies.Append(CookieName(provider), nonce, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = Lifetime,
            Path = callback.AbsolutePath,
        });

        return protector.Protect(JsonSerializer.Serialize(payload));
    }

    public bool TryConsume(
        HttpContext http,
        ExternalAccountProvider provider,
        Uri callback,
        string? encoded)
    {
        var cookieName = CookieName(provider);
        var cookie = http.Request.Cookies[cookieName];
        http.Response.Cookies.Delete(cookieName, new CookieOptions { Path = callback.AbsolutePath });

        if (string.IsNullOrWhiteSpace(cookie) || string.IsNullOrWhiteSpace(encoded) || encoded.Length > 4096)
            return false;

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(encoded));
            if (payload is not { Version: 1 }
                || payload.Provider != provider
                || payload.AccountId != Sessions.AccountIdOf(http.User)
                || payload.SessionId != Sessions.SessionIdOf(http.User)
                || payload.ExpiresAt < time.GetUtcNow()) return false;

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload.Nonce),
                Encoding.UTF8.GetBytes(cookie));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static string CookieName(ExternalAccountProvider provider) =>
        $"ns_link_{provider.ToString().ToLowerInvariant()}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record ExternalLinkStart(Uri? Redirect, string? Error)
{
    public bool Success => Redirect is not null;
}

public sealed record ExternalLinkCompletion(bool Success, string Notice);

/// <summary>
/// The account-linking boundary: callback origin, correlation, provider proof,
/// uniqueness and persistence live behind one small interface for the page.
/// </summary>
public sealed class ExternalAccountLinks(
    NextSkillDb db,
    IEnumerable<IExternalIdentityProvider> providers,
    ExternalLinkState state,
    IOptions<ExternalLinkOptions> options)
{
    private readonly IReadOnlyDictionary<ExternalAccountProvider, IExternalIdentityProvider> providersByKind =
        providers.ToDictionary(p => p.Kind);
    private readonly Uri? publicOrigin = ValidOrigin(options.Value.PublicOrigin);

    public bool CanStart(ExternalAccountProvider provider) =>
        publicOrigin is not null
        && providersByKind.TryGetValue(provider, out var implementation)
        && implementation.IsConfigured;

    public string UnavailableBecause(ExternalAccountProvider provider) => provider switch
    {
        ExternalAccountProvider.Discord => "Połączenie Discord wymaga konfiguracji aplikacji OAuth.",
        _ => "Połączenie konta wymaga skonfigurowanego publicznego adresu HTTPS.",
    };

    public ExternalLinkStart Start(HttpContext http, ExternalAccountProvider provider)
    {
        if (Sessions.AccountIdOf(http.User) is not { } accountId) return new(null, "Zaloguj się ponownie.");
        if (!CanStart(provider)) return new(null, UnavailableBecause(provider));

        var callback = Callback(provider);
        var encodedState = state.Issue(http, accountId, provider, callback);
        return new(providersByKind[provider].AuthorizationUri(callback, encodedState), null);
    }

    public async Task<ExternalLinkCompletion> CompleteAsync(
        HttpContext http,
        ExternalAccountProvider provider,
        string? encodedState,
        CancellationToken cancellationToken)
    {
        if (!CanStart(provider)) return new(false, "link-unavailable");
        var callback = Callback(provider);
        if (!state.TryConsume(http, provider, callback, encodedState)) return new(false, "link-invalid");
        if (Sessions.AccountIdOf(http.User) is not { } accountId) return new(false, "link-invalid");

        var externalId = await providersByKind[provider]
            .VerifyAsync(http.Request, callback, encodedState!, cancellationToken);
        if (externalId is null) return new(false, "link-rejected");

        var conflict = provider switch
        {
            ExternalAccountProvider.Steam => await db.Accounts.AnyAsync(
                a => a.Id != accountId && a.SteamId == externalId, cancellationToken),
            ExternalAccountProvider.Discord => await db.Accounts.AnyAsync(
                a => a.Id != accountId && a.DiscordId == externalId, cancellationToken),
            _ => true,
        };
        if (conflict) return new(false, "link-in-use");

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null) return new(false, "link-invalid");

        if (provider == ExternalAccountProvider.Steam) account.SteamId = externalId;
        else account.DiscordId = externalId;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The unique database indexes close the race between two callbacks.
            return new(false, "link-in-use");
        }

        return new(true, provider == ExternalAccountProvider.Steam ? "steam-linked" : "discord-linked");
    }

    public async Task<string> UnlinkAsync(
        ClaimsPrincipal user,
        ExternalAccountProvider provider,
        CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(user) is not { } accountId) return "link-invalid";
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null) return "link-invalid";

        if (provider == ExternalAccountProvider.Steam) account.SteamId = null;
        else account.DiscordId = null;
        await db.SaveChangesAsync(cancellationToken);

        return provider == ExternalAccountProvider.Steam ? "steam-unlinked" : "discord-unlinked";
    }

    private Uri Callback(ExternalAccountProvider provider)
    {
        var handler = provider == ExternalAccountProvider.Steam ? "SteamCallback" : "DiscordCallback";
        return new Uri(publicOrigin!, $"/{Locales.Current}/me?handler={handler}");
    }

    private static Uri? ValidOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    public static Uri WithQuery(Uri uri, string key, string value) =>
        WithQuery(uri, [new(key, value)]);

    public static Uri WithQuery(Uri uri, IEnumerable<KeyValuePair<string, string?>> values)
    {
        var builder = new UriBuilder(uri);
        var added = QueryString.Create(values).ToUriComponent().TrimStart('?');
        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? added
            : builder.Query.TrimStart('?') + "&" + added;
        return builder.Uri;
    }
}
