namespace NextSkill.Domain.Teams;

/// <summary>
/// How big a team may be (decision 045).
///
/// Tournaments run <b>1v1, 2v2 or 5v5</b>, so five is the largest lineup any
/// event can field. The team roster is capped at that, and a tournament may cap
/// its own registered lineup lower — a 2v2 event does not want five names on a
/// scoresheet.
/// </summary>
public static class RosterRules
{
    /// <summary>
    /// Five: the largest format that exists. A team is the pool a captain picks
    /// from, and there is no format that can field a sixth person, so a sixth
    /// place on the roster would mean nothing.
    /// </summary>
    public const int MaxActiveMembers = 5;

    /// <summary>
    /// Whether one more person can be invited.
    ///
    /// <b>Pending invites occupy a place.</b> Counting only accepted members
    /// would leave the cap unable to stop the thing it half exists to stop:
    /// five members and two hundred outstanding invites would pass a
    /// members-only check while spamming two hundred people (040).
    ///
    /// It also means a full team must free a place before inviting — which is
    /// the honest order of events, since the person leaving has to be told
    /// either way.
    /// </summary>
    public static bool CanInvite(int activeMembers, int pendingInvites) =>
        activeMembers + pendingInvites < MaxActiveMembers;
}
