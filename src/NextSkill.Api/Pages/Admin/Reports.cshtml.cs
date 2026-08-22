using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Admin;

/// <summary>
/// Reported adverts (decisions 015, 056).
///
/// [056](056-lft-board.md) kept <b>report</b> separate from decline and block so
/// that exactly one of the three would reach a human — and then nothing existed
/// for that human to read. This is that surface.
///
/// <b>A takedown sets <c>ClosedAt</c> as well as <c>ModeratedAt</c></b>, which
/// is why no public query changed: every board read already filters on
/// <c>ClosedAt == null &amp;&amp; ExpiresAt &gt; now</c>. The second column is
/// what stops the author republishing the same row.
/// </summary>
public class ReportsModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed class DecisionInput
    {
        public Guid ReportId { get; set; }
        public string Reason { get; set; } = "";
    }

    public sealed record ReportRow(
        Guid Id,
        bool IsListing,
        string Subject,
        string? Note,
        string Reporter,
        string? Reason,
        DateTimeOffset CreatedAt,
        int OtherReports);

    [BindProperty]
    public DecisionInput Input { get; set; } = new();

    public IReadOnlyList<ReportRow> Reports { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is { } denied) return denied;
        return Page();
    }

    /// <summary>The advert is fine. The report leaves the queue and nothing else changes.</summary>
    public Task<IActionResult> OnPostDismissAsync(CancellationToken cancellationToken) =>
        DecideAsync(remove: false, cancellationToken);

    /// <summary>The advert comes down and cannot be republished.</summary>
    public Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken) =>
        DecideAsync(remove: true, cancellationToken);

    private async Task<IActionResult> DecideAsync(bool remove, CancellationToken cancellationToken)
    {
        var reason = Input.Reason.Trim();
        if (reason.Length is 0 or > 500) return BadRequest();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        var now = clock.GetUtcNow();

        if (!AccessPolicy.Can(actor, Operation.ModerateUsers))
        {
            Audit.Record(
                db, actor.AccountId, AuditAction.ModerateAdvert, now,
                AuditOutcome.Denied, resourceId: Input.ReportId,
                retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            return Forbid();
        }

        var report = await db.AdvertReports
            .FirstOrDefaultAsync(r => r.Id == Input.ReportId && r.ResolvedAt == null, cancellationToken);
        if (report is null) return NotFound();

        report.ResolvedAt = now;
        report.ResolvedByAccountId = actor.AccountId;
        report.Removed = remove;

        if (remove)
        {
            // Both columns, in one statement each. ClosedAt is what every
            // existing public read already looks at; ModeratedAt is what the
            // republication guard looks at.
            if (report.LftListingId is { } listingId)
            {
                await db.LftListings.Where(l => l.Id == listingId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(l => l.ClosedAt, now)
                        .SetProperty(l => l.ModeratedAt, now), cancellationToken);
            }
            else
            {
                await db.TeamOpenings.Where(o => o.Id == report.TeamOpeningId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.ClosedAt, now)
                        .SetProperty(o => o.ModeratedAt, now), cancellationToken);
            }

            // Every other open report about the same advert is answered by the
            // same decision. Leaving them would make the queue grow with work
            // already done.
            await db.AdvertReports
                .Where(r => r.ResolvedAt == null && r.Id != report.Id &&
                            (r.LftListingId == report.LftListingId && report.LftListingId != null ||
                             r.TeamOpeningId == report.TeamOpeningId && report.TeamOpeningId != null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ResolvedAt, now)
                    .SetProperty(r => r.ResolvedByAccountId, actor.AccountId)
                    .SetProperty(r => r.Removed, true), cancellationToken);
        }

        Audit.Record(
            db, actor.AccountId, AuditAction.ModerateAdvert, now,
            resourceId: report.LftListingId ?? report.TeamOpeningId,
            reason: $"{(remove ? "removed" : "dismissed")}: {reason}");

        await db.SaveChangesAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/admin/reports");
    }

    private async Task<IActionResult?> LoadAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ModerateUsers)) return Forbid();

        var open = await db.AdvertReports
            .Where(r => r.ResolvedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(100)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
        {
            Reports = [];
            return null;
        }

        var listingIds = open.Where(r => r.LftListingId is not null).Select(r => r.LftListingId!.Value).ToList();
        var openingIds = open.Where(r => r.TeamOpeningId is not null).Select(r => r.TeamOpeningId!.Value).ToList();

        var listings = await db.LftListings.AsNoTracking()
            .Where(l => listingIds.Contains(l.Id))
            .Join(db.Accounts, l => l.AccountId, a => a.Id, (l, a) => new { l.Id, Subject = a.Nickname, l.Note })
            .ToDictionaryAsync(x => x.Id, x => (x.Subject, x.Note), cancellationToken);

        var openings = await db.TeamOpenings.AsNoTracking()
            .Where(o => openingIds.Contains(o.Id))
            .Join(db.Teams, o => o.TeamId, t => t.Id, (o, t) => new { o.Id, Subject = t.Name, o.Note })
            .ToDictionaryAsync(x => x.Id, x => (x.Subject, x.Note), cancellationToken);

        var reporters = await db.Accounts.AsNoTracking()
            .Where(a => open.Select(r => r.ReporterAccountId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Nickname, cancellationToken);

        Reports = [.. open.Select(r =>
        {
            var isListing = r.LftListingId is not null;
            var advert = isListing
                ? listings.GetValueOrDefault(r.LftListingId!.Value)
                : openings.GetValueOrDefault(r.TeamOpeningId!.Value);

            return new ReportRow(
                r.Id,
                isListing,
                advert.Item1 ?? "—",
                advert.Item2,
                reporters.GetValueOrDefault(r.ReporterAccountId, "—"),
                r.Reason,
                r.CreatedAt,
                // How many other people flagged the same advert. One complaint
                // and six are different facts, and merging them hides the one
                // that matters.
                open.Count(o => o.Id != r.Id &&
                    (isListing ? o.LftListingId == r.LftListingId : o.TeamOpeningId == r.TeamOpeningId)));
        })];

        return null;
    }
}
