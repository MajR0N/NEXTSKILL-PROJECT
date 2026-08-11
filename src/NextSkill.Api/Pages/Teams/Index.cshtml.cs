using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Access;

namespace NextSkill.Api.Pages.Teams;

/// <summary>
/// The team directory, plus the signed-in visitor's own teams and pending
/// invites (decisions 021, 043).
///
/// <b>Invites live here until the notification bell exists.</b> 043 decided the
/// bell is where invites belong; it is not built yet, and an invite nobody can
/// find is an invite nobody accepts. Putting them on the page a player would
/// visit anyway costs one query and keeps the feature honest in the meantime.
/// </summary>
public class IndexModel(NextSkillDb db) : PageModel
{
    public sealed record Row(Guid Id, string Name, string Tag, string? City, int Members);
    public sealed record Invite(Guid TeamId, string Name, string Tag);

    public IReadOnlyList<Row> All { get; private set; } = [];
    public IReadOnlyList<Row> Mine { get; private set; } = [];
    public IReadOnlyList<Invite> Invites { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Active members only: someone who left must not still inflate a roster
        // count, and a pending invite is not a member at all.
        //
        // A correlated count, not a group-join. EF translates this to one scalar
        // subquery per column and it reads like the sentence above; the
        // GroupJoin/SelectMany version could not be translated at all.
        All = await db.Teams
            // Disbanded teams keep their page — links to them stay valid, and
            // old brackets still name them — but they leave the directory (045).
            .Where(t => t.DisbandedAt == null)
            // Ordering happens BEFORE the projection: sorting a positional
            // record leaves EF nothing to translate.
            .OrderBy(t => t.Name)
            .Select(t => new Row(t.Id, t.Name, t.Tag, t.City,
                db.TeamMemberships.Count(m => m.TeamId == t.Id && m.LeftAt == null && m.JoinedAt != null)))
            .AsNoTracking()
            .ToListAsync();

        if (Sessions.AccountIdOf(User) is not { } accountId) return;

        var mineIds = await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.LeftAt == null && m.JoinedAt != null)
            .Select(m => m.TeamId)
            .ToListAsync();

        Mine = [.. All.Where(r => mineIds.Contains(r.Id))];

        Invites = await db.TeamMemberships
            .Where(m => m.AccountId == accountId && m.JoinedAt == null && m.LeftAt == null)
            .Join(db.Teams, m => m.TeamId, t => t.Id, (m, t) => new Invite(t.Id, t.Name, t.Tag))
            .AsNoTracking()
            .ToListAsync();
    }

    public static string Initials(string name) => DetailsModel.Initials(name);
    public static int Palette(string tag) => DetailsModel.Palette(tag);
}
