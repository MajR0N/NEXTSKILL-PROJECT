using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Features.Discord;
using NextSkill.Api.Shared.Auth;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Contracts;
using NextSkill.Domain.Matches;

namespace NextSkill.Api.Features.Matches;

/// <summary>
/// The plugin-facing API (decision 009).
///
/// Every endpoint is <b>plugin-initiated</b>. There is deliberately no endpoint
/// the panel calls on the game server: organisers' servers sit behind NAT, so a
/// design that needs to dial in to them works in testing and fails at the first
/// real organiser's firewall.
/// </summary>
public static class PluginEndpoints
{
    public static void MapPluginApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/servers/{serverId:guid}")
                       .AddEndpointFilter<PluginAuthFilter>()
                       .WithTags("plugin");

        // ── the plugin PULLS its match setup ─────────────────────────────
        group.MapGet("/matches/{matchId:guid}/config", async (Guid matchId, NextSkillDb db, HttpContext http) =>
        {
            var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
            if (match is null) return Results.NotFound();

            // Veto already happened on the web, so this carries DECIDED maps and
            // sides — which is why neither plugin implements a veto state
            // machine (decision 028).
            var config = new MatchConfig
            {
                Contract = WireContract.Version,
                MatchId = match.Id.ToString(),
                Game = match.Game == Game.Cs2 ? WireGame.Cs2 : WireGame.Cs16,
                BestOf = 1,
                Maps = [new MapAssignment { Map = match.Map ?? "de_dust2", TeamAStartsAs = WireSide.Ct }],
                TeamA = new TeamConfig { TeamId = match.TeamAId?.ToString() ?? "a", Name = "Team A", Roster = [] },
                TeamB = new TeamConfig { TeamId = match.TeamBId?.ToString() ?? "b", Name = "Team B", Roster = [] },
                EventsUrl = $"{http.Request.Scheme}://{http.Request.Host}/api/v1/servers/{http.Request.RouteValues["serverId"]}/matches/{matchId}/events",
            };

            return Results.Json(config, NextSkillJson.Options);
        });

        // ── the plugin PUSHES events, and retries until we say 200 ───────
        group.MapPost("/matches/{matchId:guid}/events", async (Guid matchId, NextSkillDb db, HttpContext http) =>
        {
            var raw = (byte[])http.Items[PluginAuthFilter.RawBodyItem]!;

            MatchEvent evt;
            try
            {
                // Strict: unknown or missing fields throw rather than becoming
                // silent defaults (decision 035, layer 1).
                evt = NextSkillJson.Deserialize<MatchEvent>(System.Text.Encoding.UTF8.GetString(raw));
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.Problem(
                    title: "malformed event",
                    detail: ex.Message,
                    statusCode: 400,
                    type: "https://nextskill.example/errors/malformed-event");
            }

            if (evt.MatchId != matchId.ToString())
                return Results.Problem(title: "match mismatch", detail: "Envelope match_id does not match the route.", statusCode: 400);

            await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);

            // Serialise server events with human administrative results. A
            // plugin event read just before a forfeit must not wake up later
            // and overwrite the walkover with a score.
            var match = await db.Matches
                .FromSqlInterpolated($"SELECT * FROM matches WHERE id = {matchId} FOR UPDATE")
                .FirstOrDefaultAsync(http.RequestAborted);
            if (match is null) return Results.NotFound();

            if (match.Status is MatchStatus.Forfeited or MatchStatus.Cancelled)
                return Results.Ok(new { status = "terminal_ignored", last_seq = match.LastEventSeq });

            var outcome = MatchIntegrityRules.CheckSequence(match.LastEventSeq, evt.Seq);

            // A duplicate returns 200. It MUST: the plugin retries until
            // acknowledged, so anything else would make it retry forever
            // (decision 009).
            if (outcome == SeqOutcome.DuplicateIgnored)
                return Results.Ok(new { status = "duplicate_ignored", last_seq = match.LastEventSeq });

            if (outcome == SeqOutcome.GapDetected)
            {
                // Events were missed. Record it and keep going — refusing the
                // event would lose the rest of the match as well.
                match.Integrity = MatchIntegrity.Suspect;
            }

            match.LastEventSeq = evt.Seq;

            if (evt.Type == MatchEventType.MatchLoaded)
            {
                if (match.Status == MatchStatus.Scheduled)
                    match.Status = MatchStatus.AwaitingPlayers;
                match.StartedAt ??= DateTimeOffset.UtcNow;

                await DiscordAnnouncements.EnqueueMatchStartedAsync(
                    db, match, http.RequestAborted);
            }
            else if (evt.Type == MatchEventType.MapEnd)
            {
                var payload = evt.PayloadAs<MapEndPayload>();

                // Layer 3 — reconciliation. We do not trust our accumulated view,
                // and do not trust the authoritative block alone: we compare them.
                // One enum, so no mapping and no default case to get wrong.
                match.Integrity = MatchIntegrityRules.Reconcile(
                    accumulatedA: match.ScoreA ?? payload.FinalScoreA,
                    accumulatedB: match.ScoreB ?? payload.FinalScoreB,
                    authoritativeA: payload.FinalScoreA,
                    authoritativeB: payload.FinalScoreB,
                    sawGap: match.Integrity == MatchIntegrity.Suspect);

                match.ScoreA = payload.FinalScoreA;
                match.ScoreB = payload.FinalScoreB;
                match.Status = MatchStatus.Completed;
                match.EndedAt = DateTimeOffset.UtcNow;

                await DiscordAnnouncements.EnqueueResultConfirmedAsync(
                    db, match, http.RequestAborted);
            }
            else if (evt.Type == MatchEventType.RoundEnd)
            {
                var payload = evt.PayloadAs<RoundEndPayload>();
                match.ScoreA = payload.ScoreA;
                match.ScoreB = payload.ScoreB;
                match.Status = MatchStatus.Live;
            }

            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);

            return Results.Ok(new
            {
                status = outcome == SeqOutcome.GapDetected ? "accepted_with_gap" : "accepted",
                integrity = match.Integrity.ToString().ToLowerInvariant(),
            });
        });

        // ── liveness, and how we learn the plugin version ────────────────
        group.MapPost("/heartbeat", async (NextSkillDb db, HttpContext http) =>
        {
            var raw = (byte[])http.Items[PluginAuthFilter.RawBodyItem]!;
            var server = (GameServer)http.Items[PluginAuthFilter.ServerItem]!;

            HeartbeatRequest beat;
            try { beat = NextSkillJson.Deserialize<HeartbeatRequest>(System.Text.Encoding.UTF8.GetString(raw)); }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.Problem(title: "malformed heartbeat", detail: ex.Message, statusCode: 400);
            }

            var tracked = await db.Servers.FirstAsync(s => s.Id == server.Id);
            tracked.PluginVersion = beat.PluginVersion;
            tracked.LastHeartbeatAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { status = "ok", contract = WireContract.Version });
        });
    }
}
