using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using NextSkill.Api.Shared.Persistence;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Features.Tournaments;

/// <summary>
/// The setup shared by tournament creation, editing and templates (decision
/// 015). Keeping one form and one mapper means a template cannot silently omit
/// a setting that the ordinary editor knows about.
/// </summary>
public sealed class TournamentSetupForm
{
    [Required(ErrorMessage = "Podaj nazwę turnieju.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Nazwa musi mieć od 3 do 120 znaków.")]
    public string Name { get; set; } = "";

    public Guid OrganiserId { get; set; }
    public Game Game { get; set; } = Game.Cs2;
    public TeamSize TeamSize { get; set; } = TeamSize.Squad;
    public TournamentFormat Format { get; set; } = TournamentFormat.SingleElimination;

    [Range(2, 128, ErrorMessage = "Liczba drużyn musi być między 2 a 128.")]
    public int MaxTeams { get; set; } = 16;

    [StringLength(4000, ErrorMessage = "Opis może mieć najwyżej 4000 znaków.")]
    public string? Description { get; set; }

    [DataType(DataType.Date)]
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>
    /// Optional because community cups may be independent of the platform's
    /// seasonal competition. A season is event context, not template setup.
    /// </summary>
    public Guid? SeasonId { get; set; }
}

public sealed record SeasonChoice(
    Guid Id,
    int Number,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);

public static class TournamentSetup
{
    /// <summary>Only formats whose bracket engine is implemented (022).</summary>
    public static readonly TournamentFormat[] BuildableFormats =
        [TournamentFormat.SingleElimination, TournamentFormat.DoubleElimination];

    /// <summary>Reject undefined numeric enum values submitted by hand.</summary>
    public static void Validate(TournamentSetupForm input, ModelStateDictionary state)
    {
        if (!Enum.IsDefined(input.Game))
            state.AddModelError("Input.Game", "Nieznana gra.");

        if (!Enum.IsDefined(input.TeamSize))
            state.AddModelError("Input.TeamSize", "Nieznany rozmiar składu.");

        if (!BuildableFormats.Contains(input.Format))
            state.AddModelError("Input.Format", "Ten format nie jest jeszcze dostępny.");
    }

    public static TournamentSetupForm From(Tournament tournament) => new()
    {
        Name = tournament.Name,
        OrganiserId = tournament.OrganiserId,
        Game = tournament.Game,
        TeamSize = tournament.TeamSize,
        Format = tournament.Format,
        MaxTeams = tournament.MaxTeams,
        Description = tournament.Description,
        StartsAt = tournament.StartsAt,
        SeasonId = tournament.SeasonId,
    };

    /// <summary>
    /// A template copies repeatable setup, not event identity or an absolute
    /// date. The organiser must name and schedule the new draft deliberately.
    /// </summary>
    public static TournamentSetupForm From(TournamentTemplate template) => new()
    {
        OrganiserId = template.OrganiserId,
        Game = template.Game,
        TeamSize = template.TeamSize,
        Format = template.Format,
        MaxTeams = template.MaxTeams,
        Description = template.Description,
    };

    public static void ApplyDetails(TournamentSetupForm input, Tournament tournament)
    {
        tournament.Name = input.Name.Trim();
        tournament.MaxTeams = input.MaxTeams;
        tournament.Description = string.IsNullOrWhiteSpace(input.Description)
            ? null
            : input.Description.Trim();
        // The UI collects a calendar date. ASP.NET binds it with the server's
        // local offset, while PostgreSQL timestamptz accepts UTC only. Preserve
        // the chosen date and store its midnight explicitly as UTC.
        tournament.StartsAt = input.StartsAt is { } startsAt
            ? new DateTimeOffset(startsAt.Year, startsAt.Month, startsAt.Day, 0, 0, 0, TimeSpan.Zero)
            : null;
        tournament.SeasonId = input.SeasonId;
    }

    public static void ApplyStructure(TournamentSetupForm input, Tournament tournament)
    {
        tournament.Game = input.Game;
        tournament.TeamSize = input.TeamSize;
        tournament.Format = input.Format;
    }

    /// <summary>
    /// Seasons are global platform periods, so every organiser sees the same
    /// open choices. Closed seasons remain queryable for archives but cannot
    /// receive another tournament.
    /// </summary>
    public static Task<List<SeasonChoice>> AssignableSeasonsAsync(
        NextSkillDb db,
        CancellationToken cancellationToken = default) =>
        db.Seasons
            .Where(s => !s.Closed)
            .OrderByDescending(s => s.Number)
            .Select(s => new SeasonChoice(s.Id, s.Number, s.Name, s.StartsAt, s.EndsAt))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public static Guid? DefaultSeasonId(
        IReadOnlyCollection<SeasonChoice> seasons,
        DateTimeOffset now) =>
        seasons
            .Where(s => s.StartsAt <= now && (s.EndsAt == null || s.EndsAt > now))
            .OrderByDescending(s => s.StartsAt)
            .ThenByDescending(s => s.Number)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefault();

    /// <summary>
    /// Validates a submitted id against live rows rather than trusting the
    /// select options. If a season was closed after assignment, its membership
    /// is frozen as archive history: it may be retained, never changed.
    /// </summary>
    public static async Task ValidateSeasonAsync(
        NextSkillDb db,
        TournamentSetupForm input,
        Tournament? existing,
        ModelStateDictionary state,
        CancellationToken cancellationToken = default)
    {
        Season? current = null;
        if (existing?.SeasonId is { } currentId)
            current = await db.Seasons.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == currentId, cancellationToken);

        if (current?.Closed == true && input.SeasonId != current.Id)
        {
            state.AddModelError("Input.SeasonId",
                "Przypisanie do zamkniętego sezonu jest częścią zamrożonego archiwum i nie można go zmienić.");
            return;
        }

        if (input.SeasonId is not { } seasonId) return;

        var selected = current?.Id == seasonId
            ? current
            : await db.Seasons.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == seasonId, cancellationToken);

        if (selected is null)
        {
            state.AddModelError("Input.SeasonId", "Wybrany sezon nie istnieje.");
            return;
        }

        if (selected.Closed && current?.Id != selected.Id)
        {
            state.AddModelError("Input.SeasonId", "Nie można przypisać turnieju do zamkniętego sezonu.");
            return;
        }

        if (input.StartsAt is not { } startsAt || selected.Closed) return;

        var tournamentDate = new DateTimeOffset(
            startsAt.Year, startsAt.Month, startsAt.Day, 0, 0, 0, TimeSpan.Zero);

        var seasonStartDate = new DateTimeOffset(
            selected.StartsAt.Year, selected.StartsAt.Month, selected.StartsAt.Day, 0, 0, 0, TimeSpan.Zero);

        if (tournamentDate < seasonStartDate)
            state.AddModelError("Input.StartsAt",
                $"Turniej nie może rozpocząć się przed sezonem „{selected.Name}”.");

        if (selected.EndsAt is { } endsAt && tournamentDate >= endsAt)
            state.AddModelError("Input.StartsAt",
                $"Turniej musi rozpocząć się przed końcem sezonu „{selected.Name}”.");
    }
}
