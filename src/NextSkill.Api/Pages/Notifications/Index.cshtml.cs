using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Api.Pages.Notifications;

/// <summary>
/// The in-app inbox (decision 043).
///
/// Reading the page marks everything read, which is what an inbox does — a
/// per-row "mark as read" button is a second click for a state the act of
/// looking already established.
/// </summary>
public class IndexModel(NextSkillDb db, TimeProvider clock) : PageModel
{
    public sealed record Row(
        NotificationType Type,
        string? Subject,
        string? Detail,
        string? Link,
        bool WasUnread,
        DateTimeOffset CreatedAt);

    public IReadOnlyList<Row> Notifications { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Sessions.AccountIdOf(User) is not { } accountId) return Challenge();

        var rows = await db.Notifications
            .Where(n => n.AccountId == accountId)
            .OrderByDescending(n => n.CreatedAt)
            // Bounded. An inbox is a recent-activity view, not an archive, and
            // an unbounded list is a slow page for exactly the busiest users.
            .Take(50)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        Notifications = rows.Select(Project).ToArray();

        // Marked read AFTER projecting, so this render can still show which ones
        // were new. The next load shows them plain.
        await db.Notifications
            .Where(n => n.AccountId == accountId && n.ReadAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.ReadAt, clock.GetUtcNow()),
                cancellationToken);

        return Page();
    }

    /// <summary>
    /// Turns the stored rendering data into something readable.
    ///
    /// Every value here came from a <c>Data</c> document that is documented as
    /// rendering data and never a secret (012, 043) — no link codes, no tokens.
    /// A missing key renders as nothing rather than throwing: an old row written
    /// by an earlier shape of the code must not break the whole inbox.
    /// </summary>
    private static Row Project(Notification notification)
    {
        var data = notification.Data?.RootElement;

        string? Text(string key) =>
            data is { } element &&
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var teamId = Text("teamId");
        var matchId = Text("matchId");

        return new Row(
            notification.Type,
            Text("team") ?? Text("player"),
            Text("slot") ?? Text("role") ?? Text("tag"),
            teamId is not null ? $"/{Localisation.Locales.Current}/teams/{teamId}"
                : matchId is not null ? $"/{Localisation.Locales.Current}/m/{matchId}"
                : null,
            notification.ReadAt is null,
            notification.CreatedAt);
    }
}
