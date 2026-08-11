namespace NextSkill.Domain.Teams;

/// <summary>
/// What kind of place someone occupies on a roster (decision 045).
///
/// <b>Separate from <c>TeamRole</c>, which is authority.</b> A coach may be the
/// captain; a starting player may have no powers at all. Merging the two would
/// mean promoting someone to captain silently put them in the lineup.
/// </summary>
public enum RosterSlot
{
    /// <summary>The starting five.</summary>
    Player,

    /// <summary>Bench. Fieldable — this is the whole point of a bench.</summary>
    Substitute,

    /// <summary>
    /// <b>Never plays.</b> Not fieldable, and never receives a player rating,
    /// because a rating is a claim about how someone performs in a server they
    /// were never in.
    /// </summary>
    Coach,
}

/// <summary>
/// How big a team may be, and who may play (decision 045).
///
/// Tournaments run <b>1v1, 2v2 or 5v5</b>, so five is the largest lineup any
/// event can field. A bench and a coaching staff sit on top of that.
/// </summary>
public static class RosterRules
{
    public const int MaxPlayers = 5;
    public const int MaxSubstitutes = 3;
    public const int MaxCoaches = 2;

    /// <summary>
    /// Ten. Derived, not enforced separately: the per-slot caps sum to exactly
    /// this, so a second total check could never fire and would be dead code
    /// that looks like a safeguard.
    /// </summary>
    public const int MaxTotal = MaxPlayers + MaxSubstitutes + MaxCoaches;

    /// <summary>
    /// How many places a slot has.
    ///
    /// <b>Unknown slots get zero.</b> A member added to the enum without a
    /// decided capacity cannot be filled at all, rather than inheriting whatever
    /// the last branch happened to return — the completeness check in
    /// <c>tests/</c> then fails the build until somebody decides.
    /// </summary>
    public static int Capacity(RosterSlot slot) => slot switch
    {
        RosterSlot.Player => MaxPlayers,
        RosterSlot.Substitute => MaxSubstitutes,
        RosterSlot.Coach => MaxCoaches,
        _ => 0,
    };

    /// <summary>
    /// Whether this slot may take a place in a match.
    ///
    /// This is what makes a coach a real thing rather than a label. A lineup
    /// that could include the coach would be a 5v5 played 4v5, discovered at
    /// map start (011, 035).
    /// </summary>
    public static bool IsFieldable(RosterSlot slot) =>
        slot is RosterSlot.Player or RosterSlot.Substitute;

    /// <summary>
    /// Whether one more person can take this slot.
    ///
    /// <b>Pending invites count.</b> Counting accepted members alone would leave
    /// the cap unable to stop half of what it exists to stop: a full roster plus
    /// two hundred outstanding invites would pass a members-only check while
    /// spamming two hundred people (040).
    /// </summary>
    public static bool CanAdd(RosterSlot slot, int occupied) => occupied < Capacity(slot);
}
