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
    public void Apply(PageRouteModel model)
    {
        // Snapshot first: we mutate the collection while iterating it.
        foreach (var selector in model.Selectors.ToList())
        {
            var template = selector.AttributeRouteModel!.Template;

            model.Selectors.Add(new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel
                {
                    Order = -1,
                    Template = AttributeRouteModel.CombineTemplates(
                        $"{{culture:regex(^{Locales.RoutePattern}$)}}", template),
                },
            });
        }
    }
}
