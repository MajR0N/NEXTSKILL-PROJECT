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
        var localised = model.Selectors
            .Select(s => Collapse(s.AttributeRouteModel!.Template ?? ""))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t.Length)
            .Select(t => new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel
                {
                    Order = -1,
                    Template = AttributeRouteModel.CombineTemplates(
                        $"{{{Prefix}:regex(^{Locales.RoutePattern}$)}}", t),
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
