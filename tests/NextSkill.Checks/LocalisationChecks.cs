using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using NextSkill.Api.Localisation;

namespace NextSkill.Checks;

/// <summary>
/// Framework-native UI localisation (decisions 018, 051).
///
/// Polish lives in the neutral resource. An untranslated locale therefore
/// falls back through ResourceManager rather than through application code.
/// </summary>
public static class LocalisationChecks
{
    private sealed class SentinelLocalizer : IStringLocalizer<UiText>
    {
        public LocalizedString this[string name] =>
            new(name, name == "Navigation.Teams" ? "TEAMS_FROM_LOCALIZER" : name);

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        check("localisation: German falls back to the neutral Polish UI resource", () =>
        {
            using var factory = TestHost.Create();
            using var scope = factory.Services.CreateScope();
            var text = scope.ServiceProvider.GetRequiredService<IStringLocalizer<UiText>>();

            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de");
                CultureInfo.CurrentUICulture = new CultureInfo("de");

                var teams = text["Navigation.Teams"];
                that(!teams.ResourceNotFound, "the neutral resource must contain Navigation.Teams");
                that(teams.Value == "Drużyny",
                    $"German should fall back to neutral Polish, got '{teams.Value}'");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        });

        check("localisation: the rendered shell reads navigation text through the localizer", () =>
        {
            using var factory = TestHost.Create(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStringLocalizer<UiText>>();
                services.AddSingleton<IStringLocalizer<UiText>, SentinelLocalizer>();
            }));
            using var http = factory.NewClient();

            var html = http.GetStringAsync("/pl/account/login").GetAwaiter().GetResult();
            that(html.Contains("TEAMS_FROM_LOCALIZER", StringComparison.Ordinal),
                "the layout must render Navigation.Teams through IStringLocalizer<UiText>");
        });

        check("localisation: an untranslated German route renders neutral Polish UI", () =>
        {
            using var factory = TestHost.Create();
            using var http = factory.NewClient();

            var html = http.GetStringAsync("/de/account/login").GetAwaiter().GetResult();
            var visibleText = WebUtility.HtmlDecode(html);
            that(visibleText.Contains("Drużyny", StringComparison.Ordinal),
                "the German route should render the neutral Polish navigation value");
            that(!html.Contains("Navigation.Teams", StringComparison.Ordinal),
                "a resource key must never leak into rendered HTML");
        });
    }
}
