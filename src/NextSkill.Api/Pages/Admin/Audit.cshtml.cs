using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Admin;

/// <summary>
/// The audit log, read-only (decisions 012 §7, 015, 036).
///
/// <b>Until this page existed the log was storage rather than security.</b> An
/// append-only record nobody can view protects nothing; it only becomes evidence
/// when someone can go and look at it after an incident.
///
/// Read-only in the strongest sense available: there is no write handler here,
/// and PostgreSQL refuses an <c>UPDATE</c> on the table regardless of what any
/// code asks for.
/// </summary>
public class AuditModel(NextSkillDb db) : PageModel
{
    public const int PageSize = 50;

    public sealed record Entry(
        DateTimeOffset OccurredAt,
        string Actor,
        AuditAction Action,
        AuditOutcome Outcome,
        string? Organiser,
        Guid? ResourceId,
        string? Reason);

    [BindProperty(SupportsGet = true)]
    public int Skip { get; set; }

    /// <summary>Filter to refusals — the rows an incident review starts from.</summary>
    [BindProperty(SupportsGet = true)]
    public bool DeniedOnly { get; set; }

    public IReadOnlyList<Entry> Entries { get; private set; } = [];
    public bool HasMore { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.ViewAuditLog)) return Forbid();

        Skip = Math.Clamp(Skip, 0, 100_000);

        var query = db.AuditEntries.AsQueryable();
        if (DeniedOnly) query = query.Where(a => a.Outcome == AuditOutcome.Denied);

        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip(Skip)
            .Take(PageSize + 1)
            .Select(a => new
            {
                a.OccurredAt,
                a.ActorAccountId,
                a.Action,
                a.Outcome,
                a.OrganiserId,
                a.ResourceId,
                a.Reason,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        HasMore = rows.Count > PageSize;
        rows = rows.Take(PageSize).ToList();
        if (rows.Count == 0) return Page();

        // Names are resolved for display only. An erased account is a tombstone
        // whose id survives, so its rows still read as a real actor rather than
        // vanishing — append-only and erasure stop competing precisely because
        // nothing personal is stored here to begin with (051 §7).
        var actorNames = await db.Accounts
            .Where(a => rows.Select(r => r.ActorAccountId).Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Nickname, cancellationToken);

        var organiserNames = await db.Organisers
            .Where(o => rows.Select(r => r.OrganiserId).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);

        Entries = rows.Select(r => new Entry(
            r.OccurredAt,
            actorNames.GetValueOrDefault(r.ActorAccountId, "(usunięte konto)"),
            r.Action,
            r.Outcome,
            r.OrganiserId is { } id ? organiserNames.GetValueOrDefault(id) : null,
            r.ResourceId,
            r.Reason)).ToArray();

        return Page();
    }
}
