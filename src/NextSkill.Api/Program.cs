using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
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

// Rate limiting on login and registration (011). Built into ASP.NET Core, so
// this is configuration rather than a hand-rolled bucket that gets it wrong.
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
        // ponytail: partitions on the socket address. Ceiling: behind Cloudflare
        // (042) that is CLOUDFLARE'S address, so every visitor shares one bucket
        // and one attacker locks out the entire site. Upgrade path: resolve the
        // real client IP from CF-Connecting-IP with Cloudflare's ranges as known
        // proxies — required BEFORE this goes near production.
        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(5),
            });
    });
});

// Localisation: four locales, locale in the URL path (decision 018).
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

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
    o.Conventions.AddPageRoute("/Tournaments/Details", "t/{id:guid}");   // 043: the link pasted mid-event

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
