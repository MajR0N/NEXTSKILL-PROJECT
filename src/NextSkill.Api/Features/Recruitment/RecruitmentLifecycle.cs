using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Recruitment;

namespace NextSkill.Api.Features.Recruitment;

/// <summary>
/// Expiry, the one reminder, and retention (decision 056).
///
/// <b>Reads exclude expired rows regardless of whether this has run.</b> The
/// worker tidies state; it is never what makes the board correct. A design where
/// a late worker leaves stale adverts on the public board would be exactly the
/// failure the fourteen-day expiry exists to prevent.
/// </summary>
public sealed class RecruitmentLifecycle(NextSkillDb db, TimeProvider clock)
{
    /// <summary>Runs one pass. Returns whether anything changed.</summary>
    public async Task<bool> SweepAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var touched = 0;

        // ── pending requests expire after seven days ──────────────────────
        //
        // Expiry frees the sender's cap. Without it a silent recipient would
        // permanently consume one of somebody's ten approaches.
        touched += await db.OpeningApplications
            .Where(a => a.State == RecruitmentState.Pending && a.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.State, RecruitmentState.Expired)
                .SetProperty(a => a.ResolvedAt, now), ct);

        touched += await db.TeamInterests
            .Where(i => i.State == RecruitmentState.Pending && i.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.State, RecruitmentState.Expired)
                .SetProperty(i => i.ResolvedAt, now), ct);

        // ── one reminder, 48 hours out, and never a second ────────────────
        //
        // ReminderSentAt is the idempotency: a retry or a double pass cannot
        // send twice, which is the same stable-event-key trick the Discord
        // outbox already uses (014).
        var dueAt = now + RecruitmentRules.ReminderLeadTime;

        var listings = await db.LftListings
            .Where(l => l.ClosedAt == null && l.ReminderSentAt == null &&
                        l.ExpiresAt > now && l.ExpiresAt <= dueAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var listing in listings)
        {
            listing.ReminderSentAt = now;
            Notify(listing.AccountId, listing.ExpiresAt, now);
            touched++;
        }

        var openings = await db.TeamOpenings
            .Where(o => o.ClosedAt == null && o.ReminderSentAt == null &&
                        o.ExpiresAt > now && o.ExpiresAt <= dueAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var opening in openings)
        {
            opening.ReminderSentAt = now;
            if (opening.ContactCaptainAccountId is { } captain)
                Notify(captain, opening.ExpiresAt, now);
            touched++;
        }

        // ── retention ─────────────────────────────────────────────────────
        //
        // Unsuccessful closed requests are hard-deleted after fourteen days.
        // Block markers are deliberately NOT swept: a durable refusal that
        // quietly lapses is not durable.
        var cutoff = now - RecruitmentRules.ClosedRequestRetention;

        touched += await db.OpeningApplications
            .Where(a => a.State != RecruitmentState.Pending &&
                        a.State != RecruitmentState.Converted &&
                        a.ResolvedAt != null && a.ResolvedAt <= cutoff)
            .ExecuteDeleteAsync(ct);

        touched += await db.TeamInterests
            .Where(i => i.State != RecruitmentState.Pending &&
                        i.State != RecruitmentState.Converted &&
                        i.ResolvedAt != null && i.ResolvedAt <= cutoff)
            .ExecuteDeleteAsync(ct);

        // A contact grant that has run its thirty days stops being a grant.
        touched += await db.ContactGrants
            .Where(g => g.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);
        return touched > 0;
    }

    private void Notify(Guid accountId, DateTimeOffset expiresAt, DateTimeOffset now) =>
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = NotificationType.RosterChanged,
            // Rendering data only, never a contact or an identifier (012).
            Data = JsonSerializer.SerializeToDocument(new { expiresAt }),
            CreatedAt = now,
        });
}

/// <summary>
/// Drives <see cref="RecruitmentLifecycle"/>. Same shape as the Discord and
/// rating workers: claim, do, idle.
/// </summary>
public sealed class RecruitmentLifecycleWorker(
    IServiceScopeFactory scopes,
    ILogger<RecruitmentLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<RecruitmentLifecycle>()
                    .SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recruitment lifecycle sweep failed.");
            }

            // Minutes, not seconds: nothing here is urgent, and the public board
            // is already correct without it.
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
