using System.Security.Cryptography;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Discord;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Email;
using NextSkill.Api.Shared.Sms;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// One PostgreSQL database (decision 008): recording a result, advancing a
// bracket and updating ratings must happen in ONE transaction (035).
//
// Fail fast with a sentence rather than letting a missing value surface 70
// frames deep inside Npgsql as "The ConnectionString property has not been
// initialized" — which is technically accurate and tells you nothing about
// what to do.
var connectionString = builder.Configuration.GetConnectionString("NextSkill");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"No connection string 'NextSkill' for environment '{builder.Environment.EnvironmentName}'. " +
        "In development it comes from appsettings.Development.json, which is only loaded when " +
        "ASPNETCORE_ENVIRONMENT=Development. In production set ConnectionStrings__NextSkill as an " +
        "environment variable (decision 012: secrets never live in files).");
}

builder.Services.AddDbContext<NextSkillDb>(o => o.UseNpgsql(connectionString));

// Injected so signature expiry can be tested without waiting five minutes.
builder.Services.AddSingleton(TimeProvider.System);

// Tenancy is scoped per request and fails closed (decisions 036, 037).
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// ── authentication: signed cookie + server-side session row (011) ─────────
//
// The cookie alone would be unrevocable: valid until it expires, whatever
// happens server-side. Pairing it with a `sessions` row makes logout real —
// see Sessions.Validate.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "ns_session";
        o.Cookie.HttpOnly = true;                          // JS can never read it
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always; // HTTPS only
        o.Cookie.SameSite = SameSiteMode.Lax;              // CSRF defence in depth
        o.ExpireTimeSpan = Sessions.Lifetime;
        o.SlidingExpiration = true;

        // LoginPath is a fixed string, but our URLs carry a locale (018) — so
        // the redirect is built per request instead, keeping the user's language.
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.Redirect(Sessions.LoginUrl(ctx.Request));
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        o.Events.OnValidatePrincipal = Sessions.ValidateAsync;
    });

// ── outbound messaging (decisions 011, 047) ───────────────────────────────
//
// FAIL CLOSED. In development, messages are written to files so the reset link
// and the SMS code are followable. In production there is no adapter yet, so
// the app REFUSES TO START rather than accepting a password-reset request and
// silently dropping the email — which would look identical to working.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmailSender>(sp => new FileEmailSender(
        builder.Configuration["Email:Directory"] ?? FileEmailSender.DefaultDirectory,
        sp.GetRequiredService<ILogger<FileEmailSender>>()));

    builder.Services.AddSingleton<ISmsSender>(sp => new FileSmsSender(
        builder.Configuration["Sms:Directory"] ?? FileSmsSender.DefaultDirectory,
        sp.GetRequiredService<ILogger<FileSmsSender>>()));
}
else
{
    throw new InvalidOperationException(
        "No email or SMS provider is configured. Password reset (011) and the SMS second factor " +
        "(047) both depend on outbound messages, and a provider that silently drops them is " +
        "indistinguishable from one that works. Configure a real sender before deploying.");
}

// The pending-login state between password and second factor (047).
builder.Services.AddScoped<TwoFactorLogin>();

// Encryption at rest for the two secrets that must stay RECOVERABLE — the
// server signing key and the TOTP seed. Everything else that can be hashed
// already is (012).
builder.Services.AddSingleton<SecretProtector>();

// Optional external identities remain LINKS to the native account (011), never
// alternate login methods. Steam OpenID needs no secret; Discord activates when
// its application credentials are supplied through configuration.
builder.Services.Configure<ExternalLinkOptions>(builder.Configuration.GetSection("ExternalAccounts"));
builder.Services.AddHttpClient(SteamOpenIdProvider.ClientName);
builder.Services.AddHttpClient(DiscordOAuthProvider.ClientName, client =>
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "NEXTSKILL/0.1 (+https://github.com/MajR0N/NEXTSKILL-PROJECT)"));
builder.Services.AddScoped<IExternalIdentityProvider, SteamOpenIdProvider>();
builder.Services.AddScoped<IExternalIdentityProvider, DiscordOAuthProvider>();
builder.Services.AddScoped<ExternalLinkState>();
builder.Services.AddScoped<ExternalAccountLinks>();
builder.Services.AddScoped<AccountDeletion>();

// Tournament announcements use a transactional outbox. The request that
// opens registration or records a result only writes a durable row; this
// worker delivers it independently so Discord cannot roll back tournament
// state (decision 014).
builder.Services.Configure<DiscordAnnouncementOptions>(
    builder.Configuration.GetSection("DiscordAnnouncements"));
builder.Services.AddSingleton<DiscordWebhookProtector>();
builder.Services.AddSingleton<IDiscordWebhookTransport, DiscordWebhookTransport>();
builder.Services.AddScoped<DiscordAnnouncementDispatcher>();
builder.Services.AddHostedService<DiscordAnnouncementWorker>();

// Ratings settle AFTER results do (025, 050). The sweeper applies final results
// in (EndedAt, Id) order, because Glicko-2 is rating-period based and an
// out-of-order application gives different numbers.
builder.Services.AddScoped<RatingSweeper>();

// The headbar's unread count. Scoped, because it reads the request's own
// signed-in account and nobody else's (043).
builder.Services.AddScoped<NextSkill.Api.Features.Notifications.NotificationBell>();

// Advert expiry, the 48-hour reminder and request retention (056). The board is
// correct without this having run; the worker only tidies state.
builder.Services.AddScoped<NextSkill.Api.Features.Recruitment.RecruitmentLifecycle>();
builder.Services.AddHostedService<NextSkill.Api.Features.Recruitment.RecruitmentLifecycleWorker>();
builder.Services.AddHostedService<RatingSweepWorker>();

// OpenAPI 3.1, generated from the real endpoint signatures rather than
// hand-written (009, 039). .NET 10 emits 3.1 by default, and NSwag turns the
// document into the TypeScript client 004 planned for.
//
// A hand-maintained spec drifts from the code within weeks, and a plugin author
// debugging against a stale spec is debugging the wrong thing.
builder.Services.AddOpenApi();

// HSTS: a year, subdomains included (012). The default 30 days is short enough
// that a browser which has not visited recently is back to trusting http.
//
// `Preload` is deliberately OFF. Getting on the preload list means browsers
// refuse http for this domain before ever contacting it — excellent, and
// effectively irreversible. It belongs at the end of Phase 6, once the domain
// is settled and every subdomain genuinely serves TLS.
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

// Behind Cloudflare, every request arrives from a Cloudflare edge address, so
// the limiter below would partition the entire internet into ONE bucket and let
// a single attacker lock everyone out of logging in (011, 042). The forwarded
// header is only honoured for requests that actually came from a configured
// network — an unconditional read would let anyone mint a fresh bucket per
// request, which is strictly worse than the bug being fixed.
//
// Configured empty by default: a direct-connect deployment trusts nothing and
// keeps using the socket address.
builder.Services.Configure<ForwardedHeadersOptions>(o => TrustedProxies.Configure(
    o,
    builder.Configuration.GetSection("Hosting:TrustedProxies").Get<string[]>() ?? []));

// Rate limiting on login and registration (011). Built into ASP.NET Core, so
// this is configuration rather than a hand-rolled bucket that gets it wrong.
var ipPartitionKey = RandomNumberGenerator.GetBytes(32);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy(Sessions.AuthRateLimitPolicy, ctx =>
    {
        // Only ATTEMPTS count. Rate-limiting GET would mean a user who reloads
        // the login page is locked out of logging in — punishing the wrong
        // behaviour, and a self-inflicted denial of service.
        if (!HttpMethods.IsPost(ctx.Request.Method))
            return RateLimitPartition.GetNoLimiter("read");

        // Per IP, because the attacker chooses the account name — limiting per
        // account only lets one IP spray a whole user list unimpeded.
        //
        // TOKEN BUCKET, not a fixed window. A fixed window has a seam at every
        // boundary: twenty attempts at 4:59 and twenty more at 5:01 is forty in
        // two seconds, which is exactly the burst the limit exists to stop. A
        // bucket refills continuously, so the sustained rate is the real limit
        // and there is no edge to aim at.
        //
        // Ten to spend at once — a person who mistypes a password twice and then
        // fumbles a TOTP code never notices — refilling at one per ten seconds,
        // so a sustained attack gets six attempts a minute rather than hundreds.
        //
        // Partitions on the visitor's address. Behind a configured trusted
        // proxy that is the forwarded client address, not the edge's — see
        // TrustedProxies, and note that UseForwardedHeaders must run BEFORE
        // UseRateLimiter or this reads the proxy again.
        return RateLimitPartition.GetTokenBucketLimiter(
            IpAbuseKeys.For(ctx.Connection.RemoteIpAddress, ipPartitionKey),
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,
                TokensPerPeriod = 1,
                ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                AutoReplenishment = true,

                // Refuse immediately rather than queueing. A queued login attempt
                // is a request held open on the server while the person stares at
                // a spinner — worse for them and worse for us than a clear no.
                QueueLimit = 0,
            });
    });
});

// Localisation: four locales, locale in the URL path (decision 018).
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddRazorPages(o =>
{
    // Entity pages get a short, shareable URL: /pl/teams/{id} rather than
    // /pl/teams/details/{id}. These links get pasted into Discord and live
    // forever (018), so the ugly form must never be the one we emit.
    //
    // Registered BEFORE the locale convention on purpose — conventions run in
    // order, and the locale prefix has to apply to this route too.
    o.Conventions.AddPageRoute("/Teams/Details", "teams/{id:guid}");
    o.Conventions.AddPageRoute("/Teams/Edit", "teams/{id:guid}/edit");
    o.Conventions.AddPageRoute("/Teams/Restore", "teams/{id:guid}/restore");
    o.Conventions.AddPageRoute("/Players/Details", "players/{id:guid}");
    o.Conventions.AddPageRoute("/Account/Profile", "me");
    o.Conventions.AddPageRoute("/Account/Recruitment", "me/recruitment");       // 056: the one private surface
    o.Conventions.AddPageRoute("/Account/TwoFactor", "account/two-factor");
    o.Conventions.AddPageRoute("/Tournaments/Details", "t/{id:guid}");          // 043: the link pasted mid-event
    o.Conventions.AddPageRoute("/Tournaments/Edit", "t/{id:guid}/edit");
    o.Conventions.AddPageRoute("/Tournaments/Bracket", "t/{id:guid}/bracket");  // 041: a different page SHAPE, not a tab
    o.Conventions.AddPageRoute("/Tournaments/Forfeit", "t/{id:guid}/matches/{matchId:guid}/forfeit");
    o.Conventions.AddPageRoute("/Matches/Details", "m/{id:guid}");              // 021/028: one durable match + lobby URL
    o.Conventions.AddPageRoute("/Seasons/Details", "seasons/{number:int}");     // 021: a permanent archive URL
    o.Conventions.AddPageRoute("/News/Details", "news/{slug}");                 // 015: a stable, shareable post URL

    o.Conventions.Add(new LocaleRouteConvention());
});

// Canonical lowercase URLs. Links live forever in Discord and forums (018),
// so /pl/ranking and /pl/Ranking must not both circulate as "the" URL.
builder.Services.Configure<RouteOptions>(o => o.LowercaseUrls = true);

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(Locales.Default);
    o.SupportedCultures = Locales.Cultures;
    o.SupportedUICultures = Locales.Cultures;

    // The URL is the source of truth for language. Accept-Language is only a
    // hint for the very first visit, and a saved preference beats it — but
    // neither may override an explicit locale in the path, or a shared link
    // would show different languages to different people.
    o.RequestCultureProviders =
    [
        new RouteDataRequestCultureProvider { RouteDataStringKey = "culture", Options = o },
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});

var app = builder.Build();

// FIRST, before anything reads an address or a scheme. The rate limiter, the
// HTTPS redirect and HSTS all reason about the request as the visitor made it,
// not as the proxy relayed it (011, 042). Inert when no proxy is trusted.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");

    // HSTS tells the browser "never speak http to this host again", which closes
    // the one-request window where a first plain-http visit can be intercepted.
    // Development is excluded on purpose: the header applies to the whole of
    // localhost, so enabling it here would break every other local project over
    // http — including ones that have nothing to do with NEXTSKILL.
    app.UseHsts();
}

// Anything that does arrive over http is bounced to https before it reaches a
// handler. Combined with the Secure session cookie (011), a session token can
// never travel in the clear.
app.UseHttpsRedirection();
// A reset link travels in a URL, so the page it lands on must not leak that
// URL to anything it references. Cheap, and it applies to every page (012).
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.UseRouting();
app.UseRequestLocalization();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

// Plugin-facing API: HMAC-signed, plugin-initiated in every direction (009).
app.MapPluginApi();

// Development only for now. The document itself carries no secrets — every
// endpoint is HMAC-protected regardless — but publishing a machine-readable map
// of the API is a decision for 039's public-API work, not a side effect of
// generating types. Serving it is one line when that decision is made.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The platform organiser must exist before anything can use it (043). Seeding
// runs with NO tenant in scope, which is fine: Organiser and Season are global.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NextSkillDb>();
    await PlatformSeed.EnsureAsync(db);

    // Encrypt anything still stored in the clear from before SecretProtector
    // existed. A migration cannot do this — it has no access to the key ring —
    // so startup is the only place it can happen. Idempotent: a second boot
    // finds nothing to encrypt.
    var encrypted = await scope.ServiceProvider
        .GetRequiredService<SecretProtector>()
        .BackfillAsync(db);

    if (encrypted > 0)
    {
        app.Logger.LogInformation(
            "Encrypted {Count} stored secrets that predated encryption at rest.", encrypted);
    }
}

// A bare "/" carries no locale. Redirect to the best match rather than
// rendering an unlocalised page, so every real page has exactly one canonical
// URL per language (018).
app.MapGet("/", (HttpContext ctx) =>
{
    var wanted = ctx.Request.GetTypedHeaders().AcceptLanguage
        .OrderByDescending(x => x.Quality ?? 1)
        .Select(x => x.Value.Value?.Split('-')[0])
        .FirstOrDefault(Locales.IsSupported);

    return Results.Redirect($"/{wanted ?? Locales.Default}", permanent: false);
});

app.Run();
