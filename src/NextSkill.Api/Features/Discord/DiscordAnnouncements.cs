using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NextSkill.Api.Localisation;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;

namespace NextSkill.Api.Features.Discord;

public sealed class DiscordAnnouncementOptions
{
    public string PublicOrigin { get; set; } = "";
}

public sealed record DiscordEmbedField(string Name, string Value, bool Inline = true);

public sealed record DiscordEmbedDraft(
    string Title,
    string Description,
    string RelativePath,
    IReadOnlyList<DiscordEmbedField> Fields);

/// <summary>
/// Writes event facts to the transactional outbox. No network call happens in
/// the tournament or match transaction: Discord being slow or unavailable
/// must never roll back a real result (decision 014).
/// </summary>
public static class DiscordAnnouncements
{
    public static async Task EnqueueRegistrationOpenedAsync(
        NextSkillDb db,
        Tournament tournament,
        CancellationToken cancellationToken = default)
    {
        tournament.RegistrationOpenRevision++;
        if (!await IsConfiguredAsync(db, tournament.Id, cancellationToken)) return;

        var teams = await db.Registrations.CountAsync(
            r => r.TournamentId == tournament.Id && r.WithdrawnAt == null,
            cancellationToken);
        var starts = tournament.StartsAt?.ToString("yyyy-MM-dd") ?? "do ustalenia";

        await AddAsync(
            db,
            tournament,
            matchId: null,
            DiscordAnnouncementType.RegistrationOpened,
            $"registration-open:{tournament.Id:N}:{tournament.RegistrationOpenRevision}",
            new DiscordEmbedDraft(
                $"Zapisy otwarte · {tournament.Name}",
                "Turniej przyjmuje zgłoszenia drużyn.",
                $"/{Locales.Default}/t/{tournament.Id}",
                [
                    new("Format", FormatName(tournament)),
                    new("Drużyny", $"{teams}/{tournament.MaxTeams}"),
                    new("Start", starts),
                ]),
            cancellationToken);
    }

    public static async Task EnqueueMatchStartedAsync(
        NextSkillDb db,
        Match match,
        CancellationToken cancellationToken = default)
    {
        if (!await IsConfiguredAsync(db, match.TournamentId, cancellationToken)) return;

        var tournament = await db.Tournaments.SingleAsync(t => t.Id == match.TournamentId, cancellationToken);
        var (teamA, teamB) = await TeamNamesAsync(db, match, cancellationToken);
        var server = match.ServerId is { } serverId
            ? await db.Servers.Where(s => s.Id == serverId)
                .Select(s => s.Host + ":" + s.Port)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        await AddAsync(
            db,
            tournament,
            match.Id,
            DiscordAnnouncementType.MatchStarted,
            $"match-start:{match.Id:N}",
            new DiscordEmbedDraft(
                $"Mecz startuje · {teamA} vs {teamB}",
                tournament.Name,
                $"/{Locales.Default}/t/{tournament.Id}/bracket",
                [
                    new("Mapa", match.Map ?? "do ustalenia"),
                    new("Serwer", server ?? "przydzielany"),
                ]),
            cancellationToken);
    }

    public static async Task EnqueueResultConfirmedAsync(
        NextSkillDb db,
        Match match,
        CancellationToken cancellationToken = default)
    {
        if (match.Integrity != Domain.Matches.MatchIntegrity.Confirmed || match.IsBye ||
            !await IsConfiguredAsync(db, match.TournamentId, cancellationToken)) return;

        var tournament = await db.Tournaments.SingleAsync(t => t.Id == match.TournamentId, cancellationToken);
        var (teamA, teamB) = await TeamNamesAsync(db, match, cancellationToken);
        var score = match.Status == MatchStatus.Forfeited
            ? "walkower"
            : match.ScoreA is { } scoreA && match.ScoreB is { } scoreB
                ? $"{scoreA}–{scoreB}"
                : "wynik administracyjny";

        await AddAsync(
            db,
            tournament,
            match.Id,
            DiscordAnnouncementType.ResultConfirmed,
            $"match-result:{match.Id:N}",
            new DiscordEmbedDraft(
                $"Wynik potwierdzony · {teamA} {score} {teamB}",
                tournament.Name,
                $"/{Locales.Default}/t/{tournament.Id}/bracket",
                [
                    new("Mapa", match.Map ?? "—"),
                    new("Wynik", score),
                ]),
            cancellationToken);
    }

    private static async Task AddAsync(
        NextSkillDb db,
        Tournament tournament,
        Guid? matchId,
        DiscordAnnouncementType type,
        string deduplicationKey,
        DiscordEmbedDraft draft,
        CancellationToken cancellationToken)
    {
        if (db.DiscordAnnouncements.Local.Any(a => a.DeduplicationKey == deduplicationKey) ||
            await db.DiscordAnnouncements.AnyAsync(
                a => a.DeduplicationKey == deduplicationKey, cancellationToken)) return;

        var now = DateTimeOffset.UtcNow;
        db.DiscordAnnouncements.Add(new DiscordAnnouncement
        {
            Id = Guid.NewGuid(),
            OrganiserId = tournament.OrganiserId,
            TournamentId = tournament.Id,
            MatchId = matchId,
            Type = type,
            DeduplicationKey = deduplicationKey,
            Payload = JsonSerializer.SerializeToDocument(draft),
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }

    private static Task<bool> IsConfiguredAsync(
        NextSkillDb db,
        Guid tournamentId,
        CancellationToken cancellationToken) =>
        db.TournamentDiscordWebhooks.AnyAsync(
            w => w.TournamentId == tournamentId && w.ProtectedUrl != null && w.DisabledAt == null,
            cancellationToken);

    private static async Task<(string A, string B)> TeamNamesAsync(
        NextSkillDb db,
        Match match,
        CancellationToken cancellationToken)
    {
        var ids = new[] { match.TeamAId, match.TeamBId }
            .Where(id => id is not null).Select(id => id!.Value).ToList();
        var names = await db.Teams.Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return (
            match.TeamAId is { } a && names.TryGetValue(a, out var nameA) ? nameA : "Drużyna A",
            match.TeamBId is { } b && names.TryGetValue(b, out var nameB) ? nameB : "Drużyna B");
    }

    private static string FormatName(Tournament tournament) =>
        $"{(int)tournament.TeamSize}v{(int)tournament.TeamSize} · " + tournament.Format switch
        {
            Domain.Tournaments.TournamentFormat.SingleElimination => "pojedyncza eliminacja",
            Domain.Tournaments.TournamentFormat.DoubleElimination => "podwójna eliminacja",
            Domain.Tournaments.TournamentFormat.RoundRobin => "każdy z każdym",
            Domain.Tournaments.TournamentFormat.GroupStage => "faza grupowa",
            Domain.Tournaments.TournamentFormat.League => "liga",
            _ => tournament.Format.ToString(),
        };
}

/// <summary>Claims one due outbox row, delivers it, then records the outcome.</summary>
public sealed class DiscordAnnouncementDispatcher(
    NextSkillDb db,
    TenantContext tenant,
    DiscordWebhookProtector protector,
    IDiscordWebhookTransport transport,
    IOptions<DiscordAnnouncementOptions> options,
    TimeProvider time)
{
    public const int MaxAttempts = 6;

    public async Task<bool> DeliverNextAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var job = await db.DiscordAnnouncements
            .FromSqlInterpolated($"""
                SELECT * FROM discord_announcements
                WHERE sent_at IS NULL AND abandoned_at IS NULL AND next_attempt_at <= {now}
                ORDER BY next_attempt_at, created_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null) return false;

        tenant.Set(job.OrganiserId);
        var webhook = await db.TournamentDiscordWebhooks.SingleOrDefaultAsync(
            w => w.TournamentId == job.TournamentId, cancellationToken);

        if (webhook?.ProtectedUrl is null || webhook.DisabledAt is not null)
        {
            Abandon(job, now, "configuration_removed");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (!protector.TryUnprotect(webhook.ProtectedUrl, out var webhookUri))
        {
            Abandon(job, now, "credential_unreadable");
            webhook.LastFailedAt = now;
            webhook.LastError = job.LastError;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (!TryPublicOrigin(options.Value.PublicOrigin, out var origin))
        {
            Abandon(job, now, "public_origin_invalid");
            webhook.LastFailedAt = now;
            webhook.LastError = job.LastError;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        DiscordEmbedDraft? draft;
        try { draft = job.Payload.Deserialize<DiscordEmbedDraft>(); }
        catch (JsonException) { draft = null; }

        if (draft is null || !Uri.TryCreate(origin, draft.RelativePath, out var linkedPage))
        {
            Abandon(job, now, "payload_invalid");
            webhook.LastFailedAt = now;
            webhook.LastError = job.LastError;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        using var outgoing = Render(draft, linkedPage, job.CreatedAt);
        DiscordSendResult result;
        try
        {
            result = await transport.SendAsync(webhookUri!, outgoing, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A transport implementation must never be able to put the secret
            // request URI into logs or durable error state via its exception.
            result = new(false, "delivery_error");
        }
        job.AttemptCount++;

        if (result.Succeeded)
        {
            job.SentAt = now;
            job.LastError = null;
            webhook.LastSucceededAt = now;
            webhook.LastFailedAt = null;
            webhook.LastError = null;
        }
        else
        {
            job.LastError = SafeCode(result.Code);
            webhook.LastFailedAt = now;
            webhook.LastError = job.LastError;

            if (job.AttemptCount >= MaxAttempts)
                job.AbandonedAt = now;
            else
                job.NextAttemptAt = now + RetryDelay(job.AttemptCount, result.RetryAfter);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static JsonDocument Render(
        DiscordEmbedDraft draft,
        Uri linkedPage,
        DateTimeOffset createdAt) =>
        JsonSerializer.SerializeToDocument(new
        {
            username = "NEXTSKILL",
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = draft.Title,
                    description = draft.Description,
                    url = linkedPage.AbsoluteUri,
                    fields = draft.Fields.Select(field => new
                    {
                        name = field.Name,
                        value = field.Value,
                        inline = field.Inline,
                    }),
                    timestamp = createdAt,
                },
            },
        });

    private static bool TryPublicOrigin(string value, out Uri origin)
    {
        origin = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length > 0 ||
            parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
            return false;
        origin = parsed;
        return true;
    }

    private static TimeSpan RetryDelay(int attempt, TimeSpan? requested)
    {
        var backoff = attempt switch
        {
            1 => TimeSpan.FromSeconds(15),
            2 => TimeSpan.FromMinutes(1),
            3 => TimeSpan.FromMinutes(5),
            4 => TimeSpan.FromMinutes(15),
            5 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6),
        };
        if (requested is { } retry && retry > backoff)
            backoff = retry;
        return backoff > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : backoff;
    }

    private static void Abandon(DiscordAnnouncement job, DateTimeOffset now, string code)
    {
        job.AttemptCount++;
        job.AbandonedAt = now;
        job.LastError = code;
    }

    private static string SafeCode(string code) =>
        code.Length <= 64 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? code
            : "delivery_error";
}

public sealed class DiscordAnnouncementWorker(
    IServiceScopeFactory scopes,
    ILogger<DiscordAnnouncementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var found = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                found = await scope.ServiceProvider
                    .GetRequiredService<DiscordAnnouncementDispatcher>()
                    .DeliverNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The job contains no credential and the exception is never
                // allowed to include the decrypted request URI.
                logger.LogError(ex, "Discord announcement dispatch failed before an outcome could be recorded.");
            }

            if (!found)
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
