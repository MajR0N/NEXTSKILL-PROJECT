using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace NextSkill.Api.Localisation;

/// <summary>
/// Prefixes every Razor Page route with a locale segment: <c>/pl/ranking</c>,
/// <c>/en/ranking</c>, and so on (decision 018).
///
/// The locale lives in the URL rather than in a cookie or header because
/// <b>URLs are effectively permanent</b>: NEXTSKILL grows through links pasted
/// into Discord and forums, and a link must resolve to the same language for
/// everyone who opens it. A cookie-based locale would make the same URL show
/// different content to different people, which breaks sharing and breaks
/// crawler-generated link previews.
/// </summary>
public sealed class LocaleRouteConvention : IPageRouteModelConvention
{
    private const string Prefix = "culture";

    public void Apply(PageRouteModel model)
    {
        // ONE localised route per page, and it is the collapsed form.
        //
        // Razor's own "/Ranking/Index" → "/Ranking" collapsing happens AFTER
        // route conventions run, so the collapsed template does not exist yet and
        // has to be produced here. Generating both would put "/pl/ranking/index"
        // and "/pl/ranking" into circulation for one page — and 018 wants exactly
        // one canonical URL per page per language, because pasted links are
        // permanent and have to agree with each other.
        //
        // The original unlocalised selectors are left alone: they keep "/Error"
        // reachable for the exception handler, and they are never linked to.
        // SuppressLinkGeneration means "this route still MATCHES, but never
        // generate a link from it". Razor and AddPageRoute both use it to mark
        // the non-canonical spelling of a page — "Ranking/Index" when the real
        // URL is "Ranking", or "Teams/Details" when it is "teams/{id}".
        //
        // Several source selectors collapse to one localised template, so the
        // flag is only carried over when EVERY source was suppressed. Taking it
        // from whichever happened to come first silently stopped the site
        // linking to /pl/ranking at all — it fell back to /ranking?culture=pl,
        // which works and is wrong.
        var localised = model.Selectors
            .GroupBy(s => Collapse(s.AttributeRouteModel!.Template ?? ""), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Template: g.Key, Suppress: g.All(s => s.AttributeRouteModel!.SuppressLinkGeneration)))
            .OrderBy(x => x.Template.Length)
            .Select(x => new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel
                {
                    Order = -1,
                    SuppressLinkGeneration = x.Suppress,
                    Template = AttributeRouteModel.CombineTemplates(
                        $"{{{Prefix}:regex(^{Locales.RoutePattern}$)}}", x.Template),
                },
            })
            .ToList();

        // Inserted ahead of the originals: link generation walks endpoints in
        // order, so position is what decides the URL the site actually emits.
        for (var i = 0; i < localised.Count; i++)
            model.Selectors.Insert(i, localised[i]);
    }

    private static string Collapse(string template) =>
        template.Equals("Index", StringComparison.OrdinalIgnoreCase) ? ""
        : template.EndsWith("/Index", StringComparison.OrdinalIgnoreCase) ? template[..^"/Index".Length]
        : template;
}
