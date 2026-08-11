namespace NextSkill.Domain.Tournaments;

/// <summary>
/// The shape of the event (decision 022). Defined here, not in persistence, so
/// the rules that reason about it are pure functions.
/// </summary>
public enum TournamentFormat { SingleElimination, DoubleElimination, RoundRobin, GroupStage, League }

/// <summary>Where a tournament is in its life (decisions 021, 043, 046).</summary>
public enum TournamentStatus { Draft, RegistrationOpen, RegistrationClosed, Running, Completed, Cancelled }

/// <summary>
/// How many players a side fields (decision 045).
///
/// The numeric value <b>is</b> the player count, so <c>(int)TeamSize.Squad</c>
/// is 5. Stored as a string in the database like every other enum, so the value
/// is a convenience rather than something a migration depends on.
/// </summary>
public enum TeamSize { Solo = 1, Duo = 2, Squad = 5 }

public enum TournamentStartBlock
{
    None,
    WrongStatus,
    ClosedSeason,
    TooFewTeams,
    UncheckedTeams,
    IncompleteRosters,
}

public readonly record struct TournamentStartEntry(bool CheckedIn, bool Fieldable);

public readonly record struct TournamentStartDecision(TournamentStartBlock Block, int Count = 0)
{
    public bool Allowed => Block == TournamentStartBlock.None;
}

/// <summary>
/// The tournament state machine (decision 046).
///
/// A pure function for the same reason the bracket and the ratings are: an
/// illegal transition does not throw or look wrong — it produces a tournament in
/// a state nobody designed for, and the damage shows up later as a bracket
/// generated from a roster that was still changing.
/// </summary>
public static class TournamentLifecycle
{
    /// <summary>
    /// Which moves are legal. <b>Everything not listed is refused</b>, so a new
    /// status added later cannot slip into a transition nobody decided.
    /// </summary>
    public static bool CanTransition(TournamentStatus from, TournamentStatus to) => (from, to) switch
    {
        (TournamentStatus.Draft, TournamentStatus.RegistrationOpen) => true,

        (TournamentStatus.RegistrationOpen, TournamentStatus.RegistrationClosed) => true,

        // Reopening is allowed on purpose. Organisers close registration, find
        // they have seven teams, and want an eighth. Forbidding it would push
        // them to cancel and recreate the event — losing the URL that has
        // already been pasted into Discord (018).
        (TournamentStatus.RegistrationClosed, TournamentStatus.RegistrationOpen) => true,

        (TournamentStatus.RegistrationClosed, TournamentStatus.Running) => true,
        (TournamentStatus.Running, TournamentStatus.Completed) => true,

        // Cancelling is possible right up to completion. A finished tournament
        // is history and is not cancellable — results have already moved
        // ratings, and unwinding them is a different operation entirely (025).
        (TournamentStatus.Draft or TournamentStatus.RegistrationOpen
            or TournamentStatus.RegistrationClosed or TournamentStatus.Running,
         TournamentStatus.Cancelled) => true,

        _ => false,
    };

    /// <summary>
    /// Whether strangers may see it at all.
    ///
    /// <b>Draft is private.</b> An organiser sketching next season's event must
    /// not have it appear in a public list, and a leaked draft is exactly the
    /// kind of thing that erodes an organiser's trust in the platform (037).
    /// </summary>
    public static bool IsPublic(TournamentStatus status) => status != TournamentStatus.Draft;

    /// <summary>Only an open tournament takes registrations. Nothing else, including Draft.</summary>
    public static bool AcceptsRegistrations(TournamentStatus status) =>
        status == TournamentStatus.RegistrationOpen;

    /// <summary>
    /// Whether the roster is still allowed to move.
    ///
    /// Once a tournament is running, its participant list is <b>frozen</b>: a
    /// bracket has been generated from it, and a team appearing or vanishing
    /// afterwards would leave matches pointing at teams that are not in the
    /// event (023).
    /// </summary>
    public static bool ParticipantsAreFrozen(TournamentStatus status) =>
        status is TournamentStatus.Running or TournamentStatus.Completed;

    /// <summary>
    /// Descriptive setup may change while the event is still being assembled.
    /// Once it is running (or terminal), its published setup is history.
    /// </summary>
    public static bool MayEditDetails(TournamentStatus status) => status is
        TournamentStatus.Draft or
        TournamentStatus.RegistrationOpen or
        TournamentStatus.RegistrationClosed;

    /// <summary>
    /// Game, format and team size define what a team is agreeing to enter.
    /// Publishing registration freezes them even before the first entry.
    /// </summary>
    public static bool MayEditStructure(TournamentStatus status) =>
        status == TournamentStatus.Draft;

    /// <summary>
    /// The database-dependent start handler projects its field into these two
    /// facts, then this pure rule decides. Check-in is confirmation, not a
    /// roster lock, so fieldability is checked again at the instant of start.
    /// </summary>
    public static TournamentStartDecision MayStart(
        TournamentStatus status,
        IReadOnlyCollection<TournamentStartEntry> entries,
        bool assignedSeasonClosed = false)
    {
        if (status != TournamentStatus.RegistrationClosed)
            return new(TournamentStartBlock.WrongStatus);

        // Closed seasons are immutable archives (021). Starting a tournament
        // inside one would add results to standings that promise not to move.
        if (assignedSeasonClosed)
            return new(TournamentStartBlock.ClosedSeason);

        if (entries.Count < 2)
            return new(TournamentStartBlock.TooFewTeams, 2 - entries.Count);

        var uncheckedCount = entries.Count(e => !e.CheckedIn);
        if (uncheckedCount > 0)
            return new(TournamentStartBlock.UncheckedTeams, uncheckedCount);

        var incompleteCount = entries.Count(e => !e.Fieldable);
        if (incompleteCount > 0)
            return new(TournamentStartBlock.IncompleteRosters, incompleteCount);

        return new(TournamentStartBlock.None);
    }
}
