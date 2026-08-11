using Microsoft.EntityFrameworkCore;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Shared.Persistence;

/// <summary>
/// The only sanctioned way to read tenant-scoped rows across organisers
/// (decision 036).
///
/// <b>Why this file exists rather than making these entities global.</b> Public
/// pages have no organiser in scope, and the query filters throw when there is
/// no tenant — deliberately, because a null tenant that silently disabled the
/// filter would return every organiser's rows. So cross-organiser reads need an
/// escape hatch, and 036 permits exactly one: explicitly named methods that call
/// <c>IgnoreQueryFilters()</c> and say why.
///
/// Keeping them here, and nowhere else, means the dangerous call appears in a
/// handful of reviewable places instead of drifting into whichever page needed
/// it that week. <b>Stricter by default with visible exceptions beats looser by
/// default with invisible ones.</b>
///
/// Every method here must be safe for a stranger to call. That is the bar:
/// not "does this work", but "would I be happy for anyone on the internet to
/// see everything this returns".
/// </summary>
public static class PublicReads
{
    /// <summary>
    /// Tournaments anyone may see — <b>every status except Draft</b>.
    ///
    /// A draft is an organiser sketching next season's event. Leaking one is
    /// exactly the sort of thing that costs an organiser's trust in the
    /// platform (037), so the filter is on the status, not on the caller.
    /// </summary>
    public static IQueryable<Tournament> PublicTournaments(this NextSkillDb db) =>
        db.Tournaments
            .IgnoreQueryFilters()
            .Where(t => t.Status != TournamentStatus.Draft);

    /// <summary>
    /// Entries in one tournament, for the public participant list.
    ///
    /// Scoped to a single tournament id rather than left open: a method that
    /// could return every registration on the platform is one refactor away
    /// from being called without an id.
    /// </summary>
    public static IQueryable<TournamentRegistration> PublicRegistrations(this NextSkillDb db, Guid tournamentId) =>
        db.Registrations
            .IgnoreQueryFilters()
            .Where(r => r.TournamentId == tournamentId && r.WithdrawnAt == null);

    /// <summary>
    /// One tournament by id, or null — including which organiser runs it, which
    /// is public information and is what every manage handler needs before it
    /// can set a tenant and ask the policy anything.
    /// </summary>
    public static Task<Tournament?> FindPublicTournamentAsync(this NextSkillDb db, Guid id) =>
        db.PublicTournaments().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>
    /// A tournament <b>including drafts</b>, for handlers that are about to
    /// check authorization themselves.
    ///
    /// Separate from the public method and deliberately awkward to reach: the
    /// caller must follow it with an <c>AccessPolicy</c> question, and the name
    /// is meant to make an unguarded use obvious in review.
    /// </summary>
    public static Task<Tournament?> FindForManagementAsync(this NextSkillDb db, Guid id) =>
        db.Tournaments.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);

    /// <summary>
    /// Private template library for organisers the caller has already been
    /// authorised to manage. The required id set keeps this cross-tenant query
    /// from ever becoming an unbounded template dump.
    /// </summary>
    public static IQueryable<TournamentTemplate> TemplatesForManagement(
        this NextSkillDb db,
        IReadOnlyCollection<Guid> organiserIds) =>
        db.TournamentTemplates
            .IgnoreQueryFilters()
            .Where(t => organiserIds.Contains(t.OrganiserId));

    /// <summary>
    /// One private template for a handler that immediately checks its organiser
    /// with <see cref="NextSkill.Domain.Access.AccessPolicy"/>.
    /// </summary>
    public static Task<TournamentTemplate?> FindTemplateForManagementAsync(
        this NextSkillDb db,
        Guid id) =>
        db.TournamentTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
}
