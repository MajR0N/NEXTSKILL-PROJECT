using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Admin;

/// <summary>
/// The <c>trusted</c> flag, and the only place it can be set (decisions 015,
/// 036, CONTEXT §3b).
///
/// <b>This closes a gap that had real consequences.</b> `organiser.trusted` has
/// been in the schema since the first migration and now genuinely changes
/// behaviour — a trusted organiser's result settles without waiting out the
/// challenge window (050 §5) — while there was no way to set it except by
/// editing the database by hand. Partner #1 is meant to be trusted.
///
/// <b>Platform owner only</b>, not staff. Staff moderate people; deciding whose
/// results skip corroboration is a different kind of authority, and 036 already
/// drew that line.
/// </summary>
public class OrganisersModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed class TrustInput
    {
        public Guid OrganiserId { get; set; }
        public bool Trusted { get; set; }
        public string Reason { get; set; } = "";
    }

    public sealed record OrganiserRow(Guid Id, string Name, string Slug, bool Trusted, int Tournaments);

    [BindProperty]
    public TrustInput Input { get; set; } = new();

    public IReadOnlyList<OrganiserRow> Organisers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();
        if (!AccessPolicy.Can(actor, Operation.SetOrganiserTrusted)) return Forbid();

        Organisers = await db.Organisers
            .OrderBy(o => o.Name)
            .Select(o => new OrganiserRow(
                o.Id, o.Name, o.Slug, o.Trusted,
                db.Tournaments.IgnoreQueryFilters().Count(t => t.OrganiserId == o.Id)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var reason = Input.Reason.Trim();
        if (reason.Length is 0 or > 500) return BadRequest();

        var actor = await Actors.CurrentAsync(db, User, cancellationToken);
        if (actor is null) return Challenge();

        if (!AccessPolicy.Can(actor, Operation.SetOrganiserTrusted))
        {
            // Trust decides whose results skip corroboration, so an attempt on
            // it is worth a row whether or not it succeeded (051 §7).
            Audit.Record(
                db, actor.AccountId, AuditAction.SetOrganiserTrusted, clock.GetUtcNow(),
                AuditOutcome.Denied, organiserId: Input.OrganiserId,
                retention: AuditRetention.Security);
            await db.SaveChangesAsync(cancellationToken);
            return Forbid();
        }

        var organiser = await db.Organisers
            .FirstOrDefaultAsync(o => o.Id == Input.OrganiserId, cancellationToken);
        if (organiser is null) return NotFound();

        organiser.Trusted = Input.Trusted;

        Audit.Record(
            db, actor.AccountId, AuditAction.SetOrganiserTrusted, clock.GetUtcNow(),
            organiserId: organiser.Id, resourceId: organiser.Id,
            reason: $"{(Input.Trusted ? "trusted" : "untrusted")}: {reason}");

        await db.SaveChangesAsync(cancellationToken);
        return Redirect($"/{Locales.Current}/admin/organisers");
    }
}
