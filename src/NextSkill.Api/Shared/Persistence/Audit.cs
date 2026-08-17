namespace NextSkill.Api.Shared.Persistence;

/// <summary>
/// Records a privileged decision (decisions 012 §7, 015, 036, 051 §7).
///
/// <b>It only ever adds to the change tracker; it never saves.</b> That is the
/// whole design: the audit row commits in the same transaction as the decision
/// it describes, so a forfeit that succeeded cannot end up without a record,
/// and a record cannot survive a decision that rolled back. A helper that saved
/// for itself would quietly reintroduce both failures.
/// </summary>
public static class Audit
{
    /// <summary>
    /// One decision, allowed or refused.
    ///
    /// <paramref name="outcome"/> defaults to <see cref="AuditOutcome.Allowed"/>
    /// because that is the common call, but the refusal path is the reason this
    /// table exists — a denied attempt changes no row, so nothing else in the
    /// schema can represent it.
    /// </summary>
    public static void Record(
        NextSkillDb db,
        Guid actorAccountId,
        AuditAction action,
        DateTimeOffset occurredAt,
        AuditOutcome outcome = AuditOutcome.Allowed,
        Guid? organiserId = null,
        Guid? resourceId = null,
        string? reason = null,
        AuditRetention retention = AuditRetention.Decision) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ActorAccountId = actorAccountId,
            OrganiserId = organiserId,
            Action = action,
            Outcome = outcome,
            ResourceId = resourceId,

            // Truncated rather than rejected. A reason is context for a human
            // reading the log later; losing the decision because someone pasted
            // an essay into the box would be the wrong trade entirely.
            Reason = reason is { Length: > 500 } ? reason[..500] : reason,
            Retention = retention,
            OccurredAt = occurredAt,
        });
}
