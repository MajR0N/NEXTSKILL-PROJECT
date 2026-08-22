using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;
using Npgsql;

namespace NextSkill.Api.Pages.Admin;

/// <summary>
/// The news CMS (decisions 015, 021, 051 §8).
///
/// <b>Posts were seeded by hand until this existed.</b> The renderer, the
/// scheme allow-list, the scheduling rule and the pinned-on-home behaviour were
/// all built and checked against rows inserted with SQL — which meant the one
/// content type the platform actually publishes had no author.
///
/// <b>Deliberately not a rich-text editor.</b> The body is markdown in a
/// textarea, because the whole XSS defence rests on markdown being parsed with
/// raw HTML disabled (051 §8). An editor that produced HTML would be an editor
/// that produced the exact input the sanitiser exists to refuse.
/// </summary>
public partial class NewsModel(
    NextSkillDb db,
    TimeProvider clock,
    IStringLocalizer<UiText> text) : PageModel
{
    /// <summary>Lowercase words joined by single hyphens. It ends up in a URL.</summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern { get; }

    public sealed class PostInput
    {
        /// <summary>Null when creating.</summary>
        public Guid? Id { get; set; }

        public string Slug { get; set; } = "";
        public string Locale { get; set; } = Locales.Default;
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public bool Pinned { get; set; }

        /// <summary>
        /// Empty means draft: invisible to readers until it has a time in the
        /// past (021, 046's rule for drafts, applied to content).
        ///
        /// ponytail: read as UTC, and the form says so. Ceiling — a Polish
        /// author typing 20:00 means 20:00 in Warsaw; the upgrade path is a
        /// per-account timezone, which nothing else in the product needs yet.
        /// </summary>
        public DateTime? PublishedAt { get; set; }
    }

    public sealed record PostRow(
        Guid Id, string Slug, string Locale, string Title, bool Pinned, DateTimeOffset? PublishedAt);

    [BindProperty]
    public PostInput Input { get; set; } = new();

    public IReadOnlyList<PostRow> Posts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is { } denied) return denied;

        if (id is { } editing)
        {
            var post = await db.News.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == editing, cancellationToken);
            if (post is null) return NotFound();

            Input = new PostInput
            {
                Id = post.Id,
                Slug = post.Slug,
                Locale = post.Locale,
                Title = post.Title,
                Body = post.Body,
                Pinned = post.Pinned,
                PublishedAt = post.PublishedAt?.UtcDateTime,
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        if (!AccessPolicy.Can(actor, Operation.ManageNews))
        {
            // Writing to the page every visitor sees is worth a row whether or
            // not it succeeded — and a refusal changes nothing else (051 §7).
            Audit.Record(
                db, actor.AccountId, AuditAction.PublishNews, clock.GetUtcNow(),
                AuditOutcome.Denied, retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            return Forbid();
        }

        Validate();
        if (!ModelState.IsValid)
        {
            if (await LoadAsync(cancellationToken) is { } denied) return denied;
            return Page();
        }

        var publishedAt = Input.PublishedAt is { } local
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc))
            : (DateTimeOffset?)null;

        NewsPost post;
        if (Input.Id is { } id)
        {
            var existing = await db.News.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (existing is null) return NotFound();
            post = existing;
        }
        else
        {
            post = new NewsPost
            {
                Id = Guid.NewGuid(),
                Slug = "", Locale = "", Title = "", Body = "",
                CreatedAt = clock.GetUtcNow(),
            };
            db.News.Add(post);
        }

        post.Slug = Input.Slug;
        post.Locale = Input.Locale;
        post.Title = Input.Title;
        // Stored exactly as written. Sanitising happens at RENDER, in one place
        // (`NewsMarkdown`), so a post written before a rule tightened is covered
        // by the new rule rather than by whatever was true the day it was saved.
        post.Body = Input.Body;
        post.Pinned = Input.Pinned;
        post.PublishedAt = publishedAt;

        Audit.Record(
            db, actor.AccountId, AuditAction.PublishNews, clock.GetUtcNow(),
            resourceId: post.Id,
            reason: $"{post.Slug}/{post.Locale}: {(publishedAt is null ? "draft" : publishedAt.Value.ToString("u"))}");

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg
            && pg.SqlState == PostgresErrorCodes.UniqueViolation
            && string.Equals(pg.ConstraintName, "IX_news_slug_locale", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, text["News.Error.Duplicate"]);
            db.ChangeTracker.Clear();
            if (await LoadAsync(cancellationToken) is { } denied) return denied;
            return Page();
        }

        return Redirect($"/{Locales.Current}/admin/news");
    }

    private void Validate()
    {
        Input.Slug = Input.Slug.Trim().ToLowerInvariant();
        Input.Title = Input.Title.Trim();
        Input.Body = Input.Body.Trim();

        if (Input.Slug.Length is < 1 or > 120 || !SlugPattern.IsMatch(Input.Slug))
            ModelState.AddModelError("Input.Slug", text["News.Error.Slug"]);

        if (!Locales.IsSupported(Input.Locale))
            ModelState.AddModelError("Input.Locale", text["News.Error.Locale"]);

        if (Input.Title.Length is < 1 or > 200)
            ModelState.AddModelError("Input.Title", text["News.Error.Title"]);

        // A ceiling rather than a judgement about length: an accidental paste of
        // a whole page should fail at the form, not at the database.
        if (Input.Body.Length is < 1 or > 20000)
            ModelState.AddModelError("Input.Body", text["News.Error.Body"]);
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ManageNews)) return Forbid();

        // Every post in every locale, published or not — the whole point of the
        // editor is to see what readers cannot.
        Posts = await db.News
            .OrderByDescending(n => n.PublishedAt ?? n.CreatedAt)
            .ThenBy(n => n.Locale)
            .Take(200)
            .Select(n => new PostRow(n.Id, n.Slug, n.Locale, n.Title, n.Pinned, n.PublishedAt))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return null;
    }
}
