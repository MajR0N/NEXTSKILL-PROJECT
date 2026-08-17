using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Api.Shared.Tenancy;
using NextSkill.Domain.Matches;
using NextSkill.Domain.Ratings;

namespace NextSkill.Api.Features.Matches;

/// <summary>
/// Turns final results into ratings (decision 050 §§3–4).
///
/// <b>Nothing in this application had ever written a <c>Rating</c> row.</b>
/// <c>db.Ratings</c> appeared only in <c>SELECT</c>s, so the ranking pages were
/// live data over a table with no writer. This is that writer.
///
/// Split from its <see cref="RatingSweepWorker"/> on purpose: the sweep is a
/// scoped method returning "did I do anything", so the checks can drive it one
/// step at a time instead of racing a timer.
/// </summary>
public sealed class RatingSweeper(NextSkillDb db, TenantContext tenant, TimeProvider clock)
{
    /// <summary>
    /// How many of the oldest unrated matches to consider per pass.
    ///
    /// The settled/not-settled rule lives in exactly one place —
    /// <see cref="MatchSettlement"/> — so it is evaluated in C#, not
    /// re-expressed as a second copy in LINQ that can drift from the first.
    /// Bounded rather than unbounded, and correct because the ordering is
    /// oldest-first: an <i>unsettled</i> match is by definition a recent one, so
    /// if anything is ready, it is inside this window.
    /// </summary>
    private const int Batch = 50;

    /// <summary>
    /// Settles at most one match. Returns false when there is nothing to do, so
    /// the worker knows to idle instead of spinning.
    /// </summary>
    public async Task<bool> SweepNextAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        // IgnoreQueryFilters because rating is inherently cross-tenant — it is
        // the one thing on this platform that is not an organiser's private
        // business (036). The tenant is set below, before anything is written.
        var candidates = await db.Matches
            .IgnoreQueryFilters()
            .Where(m =>
                m.RatedAt == null &&
                m.EndedAt != null &&
                // A dispute PAUSES the sweep. It is not a reason to write an
                // unrated row: the claim is unresolved, not resolved-as-no (050).
                m.DisputedAt == null &&
                (m.Status == MatchStatus.Completed ||
                 m.Status == MatchStatus.Forfeited ||
                 m.Status == MatchStatus.Cancelled))
            // Glicko-2 is rating-period based, so applying results out of order
            // gives different numbers. This ordering is what makes recomputation
            // after a referee reversal trustworthy (025, 050).
            .OrderBy(m => m.EndedAt)
            .ThenBy(m => m.Id)
            .Take(Batch)
            .Select(m => new
            {
                m.Id,
                m.OrganiserId,
                m.EndedAt,
                m.IsBye,
                m.Status,
                m.TeamAConfirmedAt,
                m.TeamBConfirmedAt,
                Trusted = db.Organisers.Any(o => o.Id == m.OrganiserId && o.Trusted),
            })
            .ToListAsync(ct);

        var due = candidates.FirstOrDefault(m => MatchSettlement.WindowClosed(
            m.EndedAt!.Value,
            now,
            bothCaptainsConfirmed: m.TeamAConfirmedAt is not null && m.TeamBConfirmedAt is not null,
            organiserTrusted: m.Trusted,
            // A team that did not turn up cannot click a confirm button, so
            // waiting on one would wait forever (050 §5).
            nobodyCanConfirm: m.IsBye || m.Status is MatchStatus.Forfeited or MatchStatus.Cancelled));

        if (due is null) return false;

        tenant.Set(due.OrganiserId);
        var match = await db.Matches.FirstAsync(m => m.Id == due.Id, ct);

        var facts = new MatchFacts(
            IsBye: match.IsBye,
            Integrity: Mirror(match.Integrity),
            ChallengeWindowClosed: true,   // established above, or we would not be here
            IsForfeit: match.Status == MatchStatus.Forfeited);

        var participants = await db.MatchParticipants
            .Where(p => p.MatchId == match.Id)
            // Deterministic, and it has to be: OpenSkill rates a roster as an
            // ordered pair of lists, so an unstable order is an unstable rating.
            .OrderBy(p => p.AccountId)
            .ToListAsync(ct);

        var excluded = RatingPolicy.ExcludedBecause(facts)
            // Not a policy question, so not in RatingPolicy: a cancelled match
            // and a drawn map both end without anyone to credit. Recorded rather
            // than skipped, so the match stops being rescanned.
            ?? (match.WinnerTeamId is null ? "no winner recorded — nothing to rate" : null);

        if (excluded is not null)
            WriteExcluded(match, participants, excluded, now);
        else
            await ApplyAsync(match, participants, now, ct);

        match.RatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// An unrated settlement still writes a ledger row per subject, carrying the
    /// reason verbatim.
    ///
    /// This is the fourth job 050 gives one table: <i>"why didn't this match
    /// move my rating?"</i> answers itself, which is exactly why
    /// <see cref="RatingPolicy.ExcludedBecause"/> returns a sentence and not a
    /// bool.
    /// </summary>
    private void WriteExcluded(
        Match match, List<MatchParticipant> participants, string reason, DateTimeOffset now)
    {
        foreach (var teamId in new[] { match.TeamAId, match.TeamBId })
            if (teamId is { } team)
                db.RatingChanges.Add(Row(match, team, isTeam: true, reason, now));

        foreach (var participant in participants)
            db.RatingChanges.Add(Row(match, participant.AccountId, isTeam: false, reason, now));
    }

    /// <summary>
    /// The rated path: Glicko-2 for the two teams, OpenSkill for everyone who
    /// played (025).
    ///
    /// Both sides are computed from the <b>same before-snapshot</b> and written
    /// afterwards. Updating team A first and then feeding its new rating into
    /// team B's calculation would make the result depend on which team we
    /// happened to load first.
    /// </summary>
    private async Task ApplyAsync(
        Match match, List<MatchParticipant> participants, DateTimeOffset now, CancellationToken ct)
    {
        var teamA = match.TeamAId!.Value;
        var teamB = match.TeamBId!.Value;
        var winnerIsA = match.WinnerTeamId == teamA;

        var ratingA = await TeamRatingAsync(teamA, match.Game, ct);
        var ratingB = await TeamRatingAsync(teamB, match.Game, ct);

        var beforeA = Snapshot(ratingA);
        var beforeB = Snapshot(ratingB);

        var afterA = Glicko2.Apply(beforeA, [new Outcome(beforeB, winnerIsA ? 1 : 0)]);
        var afterB = Glicko2.Apply(beforeB, [new Outcome(beforeA, winnerIsA ? 0 : 1)]);

        Store(ratingA, afterA, now);
        Store(ratingB, afterB, now);
        db.RatingChanges.Add(Row(match, teamA, isTeam: true, beforeA, afterA, now));
        db.RatingChanges.Add(Row(match, teamB, isTeam: true, beforeB, afterB, now));

        // Players. A match with no participant rows — nobody ever loaded it, or
        // it predates this slice — rates the teams and stops, rather than
        // inventing a lineup from today's roster (029).
        var winners = participants.Where(p => p.TeamId == (winnerIsA ? teamA : teamB)).ToList();
        var losers = participants.Where(p => p.TeamId == (winnerIsA ? teamB : teamA)).ToList();
        if (winners.Count == 0 || losers.Count == 0) return;

        var winnerRatings = new List<Rating>();
        var loserRatings = new List<Rating>();
        foreach (var p in winners) winnerRatings.Add(await PlayerRatingAsync(p.AccountId, match.Game, ct));
        foreach (var p in losers) loserRatings.Add(await PlayerRatingAsync(p.AccountId, match.Game, ct));

        var winnersBefore = winnerRatings.Select(PlayerSnapshot).ToArray();
        var losersBefore = loserRatings.Select(PlayerSnapshot).ToArray();
        var (winnersAfter, losersAfter) = OpenSkill.Rate(winnersBefore, losersBefore);

        for (var i = 0; i < winnerRatings.Count; i++)
        {
            StorePlayer(winnerRatings[i], winnersAfter[i], now);
            db.RatingChanges.Add(Row(match, winners[i].AccountId, isTeam: false, winnersBefore[i], winnersAfter[i], now));
        }

        for (var i = 0; i < loserRatings.Count; i++)
        {
            StorePlayer(loserRatings[i], losersAfter[i], now);
            db.RatingChanges.Add(Row(match, losers[i].AccountId, isTeam: false, losersBefore[i], losersAfter[i], now));
        }
    }

    private async Task<Rating> TeamRatingAsync(Guid teamId, Game game, CancellationToken ct) =>
        await db.Ratings.FirstOrDefaultAsync(r => r.TeamId == teamId && r.Game == game, ct)
        ?? New(new Rating
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            Game = game,
            Value = Glicko2Rating.Unrated.Rating,
            Deviation = Glicko2Rating.Unrated.Deviation,
            Volatility = Glicko2Rating.Unrated.Volatility,
        });

    private async Task<Rating> PlayerRatingAsync(Guid accountId, Game game, CancellationToken ct) =>
        await db.Ratings.FirstOrDefaultAsync(r => r.AccountId == accountId && r.Game == game, ct)
        ?? New(new Rating
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Game = game,
            Value = PlayerRating.Unrated.Mu,

            // OpenSkill's sigma reuses the deviation column. The two systems
            // never share a row — one has a TeamId, the other an AccountId —
            // and Volatility is simply unused for players (025).
            Deviation = PlayerRating.Unrated.Sigma,
        });

    private Rating New(Rating rating)
    {
        db.Ratings.Add(rating);
        return rating;
    }

    private static Glicko2Rating Snapshot(Rating r) => new(r.Value, r.Deviation, r.Volatility);
    private static PlayerRating PlayerSnapshot(Rating r) => new(r.Value, r.Deviation);

    private static void Store(Rating row, Glicko2Rating after, DateTimeOffset now)
    {
        row.Value = after.Rating;
        row.Deviation = after.Deviation;
        row.Volatility = after.Volatility;
        row.MatchesPlayed++;
        row.UpdatedAt = now;
    }

    private static void StorePlayer(Rating row, PlayerRating after, DateTimeOffset now)
    {
        row.Value = after.Mu;
        row.Deviation = after.Sigma;
        row.MatchesPlayed++;
        row.UpdatedAt = now;
    }

    private static RatingChange Row(
        Match match, Guid subject, bool isTeam, string reason, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = match.Id,
        SubjectId = subject,
        SubjectIsTeam = isTeam,
        Game = match.Game,
        ExcludedBecause = reason,
        AppliedAt = now,
    };

    private static RatingChange Row(
        Match match, Guid subject, bool isTeam, Glicko2Rating before, Glicko2Rating after, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = match.Id,
        SubjectId = subject,
        SubjectIsTeam = isTeam,
        Game = match.Game,
        ValueBefore = before.Rating,
        ValueAfter = after.Rating,
        DeviationBefore = before.Deviation,
        DeviationAfter = after.Deviation,
        VolatilityBefore = before.Volatility,
        VolatilityAfter = after.Volatility,
        AppliedAt = now,
    };

    private static RatingChange Row(
        Match match, Guid subject, bool isTeam, PlayerRating before, PlayerRating after, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = match.Id,
        SubjectId = subject,
        SubjectIsTeam = isTeam,
        Game = match.Game,
        ValueBefore = before.Mu,
        ValueAfter = after.Mu,
        DeviationBefore = before.Sigma,
        DeviationAfter = after.Sigma,
        AppliedAt = now,
    };

    /// <summary>
    /// Maps the persisted integrity state onto the one the rating policy speaks.
    ///
    /// Explicit and <b>failing closed</b>: an integrity state added to one enum
    /// and not the other must refuse to rate, never fall through to
    /// <see cref="ResultIntegrity.Confirmed"/>. This is the same hazard 035
    /// flagged when it insisted on one enum for the bracket gate.
    /// </summary>
    private static ResultIntegrity Mirror(MatchIntegrity integrity) => integrity switch
    {
        MatchIntegrity.Confirmed => ResultIntegrity.Confirmed,
        MatchIntegrity.Suspect => ResultIntegrity.Suspect,
        _ => ResultIntegrity.Disputed,
    };
}

/// <summary>
/// Drives <see cref="RatingSweeper"/>. Same shape as
/// <c>DiscordAnnouncementWorker</c>: claim one, do it, idle. No scheduler and no
/// new dependency (050 §4).
/// </summary>
public sealed class RatingSweepWorker(
    IServiceScopeFactory scopes,
    ILogger<RatingSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var did = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                did = await scope.ServiceProvider
                    .GetRequiredService<RatingSweeper>()
                    .SweepNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One bad match must not stop the queue behind it. The ledger's
                // unique index means a partially applied sweep cannot be
                // double-counted on the retry.
                logger.LogError(ex, "Rating sweep failed before a match could be settled.");
            }

            if (!did)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
