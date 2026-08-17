using Microsoft.EntityFrameworkCore;
using NextSkill.Domain.Teams;
using NextSkill.Domain.Tournaments;

namespace NextSkill.Api.Shared.Persistence;

/// <summary>
/// A match participant as the whole internet may see them: who played, for
/// whom, in what slot, and how confidently.
///
/// <b>A projection rather than the entity</b>, and that is the point:
/// <see cref="MatchParticipant.LinkCode"/> is one person's live secret, and a
/// public read that happened to carry it would satisfy every type in this file
/// while quietly publishing factor 2 of everyone's binding.
/// </summary>
public sealed record PublicParticipant(
    Guid AccountId,
    Guid TeamId,
    RosterSlot Slot,
    ParticipantProvenance Provenance,
    DateTimeOffset CreatedAt);

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
    /// One match whose tournament is public. Match rows are tenant-owned for
    /// write safety, but their schedule and results are global public history
    /// (036); the tournament visibility predicate keeps private drafts sealed.
    /// </summary>
    public static Task<Match?> FindPublicMatchAsync(this NextSkillDb db, Guid id) =>
        db.Matches
            .IgnoreQueryFilters()
            .Where(m => m.Id == id && db.PublicTournaments().Any(t => t.Id == m.TournamentId))
            .AsNoTracking()
            .FirstOrDefaultAsync();

    /// <summary>
    /// Every match belonging to a publicly visible tournament.
    ///
    /// <b>The one unbounded read in this file, and it is only safe because the
    /// caller must always narrow and page it.</b> Matches are tenant-owned for
    /// write safety while their schedule and results are global public history
    /// (036) — the home page's live and recent panels are exactly that, and both
    /// take six rows.
    /// </summary>
    public static IQueryable<Match> PublicMatches(this NextSkillDb db) =>
        db.Matches
            .IgnoreQueryFilters()
            .Where(m => db.PublicTournaments().Any(t => t.Id == m.TournamentId));

    /// <summary>
    /// Decided results for a known set of teams, for form strips and match
    /// history.
    ///
    /// Byes are excluded rather than rendered as wins: a free win is not form,
    /// and 022/025 have refused to let one count anywhere else either. The
    /// required id set keeps this from becoming a scan of every result on the
    /// platform.
    /// </summary>
    public static IQueryable<Match> PublicTeamResults(
        this NextSkillDb db,
        IReadOnlyCollection<Guid> teamIds) =>
        db.Matches
            .IgnoreQueryFilters()
            .Where(m => m.WinnerTeamId != null &&
                        !m.IsBye &&
                        m.TeamAId != null && m.TeamBId != null &&
                        (teamIds.Contains(m.TeamAId.Value) || teamIds.Contains(m.TeamBId.Value)) &&
                        db.PublicTournaments().Any(t => t.Id == m.TournamentId));

    /// <summary>
    /// Live (non-superseded) rating movements for a known set of subjects.
    ///
    /// Superseded rows are excluded because a referee reversal replaced them —
    /// showing an arrow derived from a corrected-away result would be worse than
    /// showing none.
    /// </summary>
    public static IQueryable<RatingChange> PublicRatingMovements(
        this NextSkillDb db,
        IReadOnlyCollection<Guid> subjectIds,
        bool subjectIsTeam,
        Game game) =>
        db.RatingChanges
            .Where(r => subjectIds.Contains(r.SubjectId) &&
                        r.SubjectIsTeam == subjectIsTeam &&
                        r.Game == game &&
                        r.SupersededAt == null &&
                        r.ValueAfter != null &&
                        r.ValueBefore != null);

    /// <summary>
    /// Matches one account is recorded as having played, newest first.
    ///
    /// <b>This is what 029 was waiting for.</b> That decision refused to infer
    /// an old match's participants from current team membership — a roster read
    /// today says nothing about a roster last March — so a profile could show no
    /// match history at all rather than a confident fabrication. It now reads
    /// <see cref="MatchParticipant"/>, which is a statement frozen at the time
    /// the match loaded, and carries the provenance so a captain's claim and a
    /// server's proof stay distinguishable.
    /// </summary>
    public static IQueryable<MatchParticipant> PublicMatchesOf(this NextSkillDb db, Guid accountId) =>
        db.MatchParticipants
            .IgnoreQueryFilters()
            .Where(p => p.AccountId == accountId &&
                        db.Matches.IgnoreQueryFilters().Any(m =>
                            m.Id == p.MatchId &&
                            m.EndedAt != null &&
                            db.PublicTournaments().Any(t => t.Id == m.TournamentId)));

    /// <summary>Public lobby state for one public match; never an unbounded veto scan.</summary>
    public static Task<MatchVeto?> FindPublicMatchVetoAsync(this NextSkillDb db, Guid matchId) =>
        db.MatchVetos
            .IgnoreQueryFilters()
            .Where(v => v.MatchId == matchId &&
                db.Matches.IgnoreQueryFilters().Any(m =>
                    m.Id == matchId && db.PublicTournaments().Any(t => t.Id == m.TournamentId)))
            .AsNoTracking()
            .FirstOrDefaultAsync();

    /// <summary>
    /// Who played in one public match. Lineups are public competition history —
    /// that is the entire product — but the link code is not, so it is dropped
    /// at the projection rather than trusted to every caller's select list.
    /// </summary>
    public static IQueryable<PublicParticipant> PublicMatchParticipants(this NextSkillDb db, Guid matchId) =>
        db.MatchParticipants
            .IgnoreQueryFilters()
            .Where(p => p.MatchId == matchId &&
                db.Matches.IgnoreQueryFilters().Any(m =>
                    m.Id == matchId && db.PublicTournaments().Any(t => t.Id == m.TournamentId)))
            .Select(p => new PublicParticipant(
                p.AccountId, p.TeamId, p.Slot, p.Provenance, p.CreatedAt));

    /// <summary>The ordered public veto strip for one public match.</summary>
    public static IQueryable<MatchVetoAction> PublicMatchVetoActions(this NextSkillDb db, Guid matchId) =>
        db.MatchVetoActions
            .IgnoreQueryFilters()
            .Where(a => db.MatchVetos.IgnoreQueryFilters().Any(v =>
                v.Id == a.MatchVetoId && v.MatchId == matchId) &&
                db.Matches.IgnoreQueryFilters().Any(m =>
                    m.Id == matchId && db.PublicTournaments().Any(t => t.Id == m.TournamentId)));

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
    /// Matches needing a referee's attention, for organisers the caller has
    /// already been authorised to act in.
    ///
    /// The required id set is what keeps this from becoming an unbounded dump of
    /// every contested result on the platform — the same shape, and the same
    /// reasoning, as <see cref="TemplatesForManagement"/>.
    /// </summary>
    public static IQueryable<Match> DisputesForManagement(
        this NextSkillDb db,
        IReadOnlyCollection<Guid> organiserIds) =>
        db.Matches
            .IgnoreQueryFilters()
            .Where(m => organiserIds.Contains(m.OrganiserId) &&
                        (m.DisputedAt != null ||
                         m.Integrity != NextSkill.Domain.Matches.MatchIntegrity.Confirmed));

    /// <summary>
    /// One private template for a handler that immediately checks its organiser
    /// with <see cref="NextSkill.Domain.Access.AccessPolicy"/>.
    /// </summary>
    public static Task<TournamentTemplate?> FindTemplateForManagementAsync(
        this NextSkillDb db,
        Guid id) =>
        db.TournamentTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
}
