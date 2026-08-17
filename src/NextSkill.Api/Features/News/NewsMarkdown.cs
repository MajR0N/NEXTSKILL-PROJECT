using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace NextSkill.Api.Features.News;

/// <summary>
/// Renders a news post's markdown (decisions 015, 040 §5, 051 §8).
///
/// <b><c>DisableHtml()</c> alone is not enough, and this was proven rather than
/// assumed.</b> It stops an author embedding raw <c>&lt;script&gt;</c>, which is
/// the attack everyone thinks of — and it does nothing about the URL inside an
/// ordinary markdown link. <c>[click](javascript:alert(1))</c> passes straight
/// through it and becomes a working <c>href</c> on a page every visitor sees.
///
/// So the scheme is filtered too, with an <b>allow-list</b>: http, https,
/// mailto, and relative or anchor links. Allow-listing rather than blocking
/// <c>javascript:</c> and friends, because the list of dangerous schemes is
/// open-ended (<c>vbscript:</c>, <c>data:</c>, and whatever a browser adds next)
/// while the list of ones a news post legitimately needs is short and stable.
/// </summary>
public static class NewsMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        // No raw HTML. An author writes markdown; markdown is all they get.
        .DisableHtml()
        .UseAutoLinks()
        .Build();

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);

        // ── filter the DOCUMENT, not the renderer ─────────────────────────
        //
        // Markdig exposes an HtmlRenderer.LinkRewriter hook, and using it alone
        // was tried first. It does not fire for every link shape — a
        // scheme-relative `//evil.example` reached the output untouched while
        // `javascript:` was caught, which is the worst kind of defence: one that
        // appears to work because the case you tested happened to be covered.
        //
        // Rewriting the parsed tree removes the question. Links and images are
        // both LinkInline in Markdig, so one walk reaches every URL that can
        // possibly be emitted, whatever the renderer does with it afterwards.
        foreach (var link in document.Descendants<LinkInline>())
            link.Url = Safe(link.Url ?? "");

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);

        // Kept as well, deliberately. It costs one line and it catches any URL
        // an extension might synthesise after parsing.
        renderer.LinkRewriter = Safe;

        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    /// <summary>
    /// Returns the URL when its scheme is allowed, and an empty string when it
    /// is not.
    ///
    /// <b>Emptied rather than dropped</b>: the link text stays visible, so a
    /// reader sees the words the author wrote and simply cannot be navigated
    /// anywhere by them. Removing the whole element would silently delete
    /// content and make the sanitising invisible to the author who caused it.
    /// </summary>
    private static string Safe(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";

        var trimmed = url.Trim();

        // FIRST, and the order is the whole point. A scheme-relative
        // `//evil.example` is an absolute URL wearing a relative costume, and it
        // also starts with '/' — so testing "is it relative?" before "is it
        // scheme-relative?" waves it straight through. That is precisely the bug
        // this had, and the check below caught it (040, same trick the
        // open-redirect guard refuses).
        if (trimmed.StartsWith("//", StringComparison.Ordinal)) return "";

        // Relative paths and in-page anchors carry no scheme and cannot leave
        // this origin.
        if (trimmed.StartsWith('/') || trimmed.StartsWith('#')) return trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            // Not absolute and not obviously relative. Anything containing a
            // colon before the first slash is trying to be a scheme, so it is
            // refused; the rest is an ordinary relative path.
            var colon = trimmed.IndexOf(':');
            var slash = trimmed.IndexOf('/');
            return colon >= 0 && (slash < 0 || colon < slash) ? "" : trimmed;
        }

        return parsed.Scheme is "http" or "https" or "mailto" ? trimmed : "";
    }
}
