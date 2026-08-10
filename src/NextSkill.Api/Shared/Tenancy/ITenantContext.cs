namespace NextSkill.Api.Shared.Tenancy;

/// <summary>
/// Which organiser the current request belongs to (decision 037).
///
/// NEXTSKILL is multi-tenant: many organisers share one instance and one
/// database, separated by a column rather than by walls. A missing scope check
/// is therefore <b>not a bug — it is a data breach</b>, exposing one organiser's
/// tournaments, rosters or server credentials to another.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The current organiser, or <c>null</c> for genuinely global work
    /// (background jobs, platform staff pages, public cross-organiser data).
    /// </summary>
    Guid? CurrentOrganiserId { get; }

    /// <summary>
    /// The current organiser, or <b>throws</b> if there is none.
    ///
    /// This is the one the query filters use. It fails closed on purpose: if a
    /// null tenant silently disabled the filter, every query would quietly
    /// return every organiser's rows. Loud failure beats silent disclosure.
    /// </summary>
    Guid RequireOrganiserId();
}

/// <summary>
/// Resolves the tenant per request. Registered as scoped.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? CurrentOrganiserId { get; private set; }

    /// <summary>Set once, by middleware, before any handler runs.</summary>
    public void Set(Guid? organiserId) => CurrentOrganiserId = organiserId;

    public Guid RequireOrganiserId() =>
        CurrentOrganiserId
        ?? throw new InvalidOperationException(
            "No organiser in scope. A tenant-scoped query ran outside an organiser context. " +
            "This fails closed deliberately (decision 036): returning unscoped rows here " +
            "would leak one organiser's data to another.");
}

/// <summary>
/// Marks an entity as belonging to exactly one organiser. Every such entity gets
/// a global query filter in <c>NextSkillDb</c>, so scoping is the default rather
/// than something each query must remember.
/// </summary>
public interface ITenantOwned
{
    Guid OrganiserId { get; }
}
