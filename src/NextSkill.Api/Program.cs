using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Matches;
using NextSkill.Api.Localisation;
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

// Localisation: four locales, locale in the URL path (decision 018).
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

builder.Services.AddRazorPages(o =>
{
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
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRequestLocalization();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

// Plugin-facing API: HMAC-signed, plugin-initiated in every direction (009).
app.MapPluginApi();

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
