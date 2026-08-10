using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Localization.Routing;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// One PostgreSQL database (decision 008): recording a result, advancing a
// bracket and updating ratings must happen in ONE transaction (035).
builder.Services.AddDbContext<NextSkillDb>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("NextSkill")));

// Tenancy is scoped per request and fails closed (decisions 036, 037).
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// Localisation: four locales, locale in the URL path (decision 018).
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

builder.Services.AddRazorPages(o =>
{
    o.Conventions.Add(new LocaleRouteConvention());
});

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
