using NextSkill.Domain.Tournaments;

namespace NextSkill.Checks;

/// <summary>
/// The tournament lifecycle and seeding, as pure functions (decision 046).
///
/// An illegal transition does not throw or look wrong — it produces a
/// tournament in a state nobody designed for, and the damage surfaces later as
/// a bracket generated from a roster that was still changing.
/// </summary>
public static class TournamentChecks
{
    public static void RunPure(Action<string, Action> check, Action<bool, string> that)
    {
        check("lifecycle: the happy path is legal end to end", () =>
        {
            var path = new[]
            {
                TournamentStatus.Draft,
                TournamentStatus.RegistrationOpen,
                TournamentStatus.RegistrationClosed,
                TournamentStatus.Running,
                TournamentStatus.Completed,
            };

            for (var i = 0; i < path.Length - 1; i++)
                that(TournamentLifecycle.CanTransition(path[i], path[i + 1]),
                    $"{path[i]} → {path[i + 1]} must be legal");
        });

        check("lifecycle: SKIPPING registration is refused", () =>
        {
            // The one that matters: Draft → Running would generate a bracket
            // from a field that was never assembled or seeded (023, 046).
            that(!TournamentLifecycle.CanTransition(TournamentStatus.Draft, TournamentStatus.Running),
                "a draft must not become a running tournament in one step");
            that(!TournamentLifecycle.CanTransition(TournamentStatus.RegistrationOpen, TournamentStatus.Running),
                "registration must close before the bracket is built");
            that(!TournamentLifecycle.CanTransition(TournamentStatus.Draft, TournamentStatus.Completed),
                "a tournament cannot finish without happening");
        });

        check("lifecycle: registration can be REOPENED, because organisers do that", () =>
        {
            that(TournamentLifecycle.CanTransition(TournamentStatus.RegistrationClosed, TournamentStatus.RegistrationOpen),
                "closing at seven teams and wanting an eighth is normal, and forbidding it would " +
                "push organisers to recreate the event and lose the URL already shared (018)");
        });

        check("lifecycle: a FINISHED tournament is history — no transitions out", () =>
        {
            foreach (var to in Enum.GetValues<TournamentStatus>())
                that(!TournamentLifecycle.CanTransition(TournamentStatus.Completed, to),
                    $"Completed → {to} must be refused: results have already moved ratings (025)");

            // Cancelled is equally terminal.
            foreach (var to in Enum.GetValues<TournamentStatus>())
                that(!TournamentLifecycle.CanTransition(TournamentStatus.Cancelled, to),
                    $"Cancelled → {to} must be refused");
        });

        check("lifecycle: every status has a DECIDED answer for visibility, entry and freezing", () =>
        {
            // The completeness pattern: a status added later must not inherit
            // whatever the last branch returned.
            var decided = new Dictionary<TournamentStatus, (bool Public, bool Accepts, bool Frozen)>
            {
                [TournamentStatus.Draft] = (false, false, false),
                [TournamentStatus.RegistrationOpen] = (true, true, false),
                [TournamentStatus.RegistrationClosed] = (true, false, false),
                [TournamentStatus.Running] = (true, false, true),
                [TournamentStatus.Completed] = (true, false, true),
                [TournamentStatus.Cancelled] = (true, false, false),
            };

            foreach (var status in Enum.GetValues<TournamentStatus>())
            {
                that(decided.ContainsKey(status),
                    $"{status}: nobody decided whether it is public, takes entries, or freezes the field (046)");

                var (isPublic, accepts, frozen) = decided[status];
                that(TournamentLifecycle.IsPublic(status) == isPublic, $"{status}: IsPublic should be {isPublic}");
                that(TournamentLifecycle.AcceptsRegistrations(status) == accepts, $"{status}: AcceptsRegistrations should be {accepts}");
                that(TournamentLifecycle.ParticipantsAreFrozen(status) == frozen, $"{status}: ParticipantsAreFrozen should be {frozen}");
            }

            // The rule that protects an organiser's unpublished plans (037).
            that(!TournamentLifecycle.IsPublic(TournamentStatus.Draft), "a draft must never be public");
        });

        // ── seeding ──────────────────────────────────────────────────────
        check("editing: competitive structure freezes on publish and all setup freezes on start", () =>
        {
            var decided = new Dictionary<TournamentStatus, (bool Details, bool Structure)>
            {
                [TournamentStatus.Draft] = (true, true),
                [TournamentStatus.RegistrationOpen] = (true, false),
                [TournamentStatus.RegistrationClosed] = (true, false),
                [TournamentStatus.Running] = (false, false),
                [TournamentStatus.Completed] = (false, false),
                [TournamentStatus.Cancelled] = (false, false),
            };

            foreach (var status in Enum.GetValues<TournamentStatus>())
            {
                that(decided.ContainsKey(status), $"{status}: editing policy was not decided");
                var expected = decided[status];
                that(TournamentLifecycle.MayEditDetails(status) == expected.Details,
                    $"{status}: detail editing should be {expected.Details}");
                that(TournamentLifecycle.MayEditStructure(status) == expected.Structure,
                    $"{status}: structure editing should be {expected.Structure}");
            }
        });

        check("start: every entrant must check in and still have a fieldable roster", () =>
        {
            static TournamentStartEntry Entry(bool checkedIn, bool fieldable) =>
                new(checkedIn, fieldable);

            that(TournamentLifecycle.MayStart(
                    TournamentStatus.RegistrationOpen,
                    [Entry(true, true), Entry(true, true)]).Block == TournamentStartBlock.WrongStatus,
                "start is only meaningful after registration closes");

            that(TournamentLifecycle.MayStart(
                    TournamentStatus.RegistrationClosed,
                    [Entry(true, true)]).Block == TournamentStartBlock.TooFewTeams,
                "one team is not a tournament");

            that(TournamentLifecycle.MayStart(
                    TournamentStatus.RegistrationClosed,
                    [Entry(true, true), Entry(true, true)],
                    assignedSeasonClosed: true).Block == TournamentStartBlock.ClosedSeason,
                "a closed season is immutable archive history and cannot receive new results");

            var uncheckedField = TournamentLifecycle.MayStart(
                TournamentStatus.RegistrationClosed,
                [Entry(true, true), Entry(false, true), Entry(false, true)]);
            that(uncheckedField.Block == TournamentStartBlock.UncheckedTeams && uncheckedField.Count == 2,
                "every current entrant must make a fresh commitment");

            var incomplete = TournamentLifecycle.MayStart(
                TournamentStatus.RegistrationClosed,
                [Entry(true, true), Entry(true, false)]);
            that(incomplete.Block == TournamentStartBlock.IncompleteRosters && incomplete.Count == 1,
                "check-in is not a roster lock; fieldability is revalidated at start");

            that(TournamentLifecycle.MayStart(
                    TournamentStatus.RegistrationClosed,
                    [Entry(true, true), Entry(true, true)]).Allowed,
                "two checked-in, fieldable teams may start");
        });

        check("seeding: strongest first, and a PROVISIONAL team never takes a top seed", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var strong = Guid.NewGuid();
            var middling = Guid.NewGuid();
            var luckyNewcomer = Guid.NewGuid();
            var unrated = Guid.NewGuid();

            var order = Seeding.Order(
            [
                new Entrant(unrated, null, true, now),
                // Rated ABOVE everyone, but on two matches. Seeding it first
                // would buy a top seed and an easy path with two lucky wins —
                // the same problem the ranking page already refuses (025).
                new Entrant(luckyNewcomer, 2100, true, now),
                new Entrant(middling, 1500, false, now),
                new Entrant(strong, 1900, false, now),
            ]);

            that(order[0] == strong, "the highest SETTLED rating takes seed 1");
            that(order[1] == middling, "then the next settled rating");
            that(order.ToList().IndexOf(luckyNewcomer) >= 2, "a provisional team must sit behind every settled one");
        });

        check("seeding: DETERMINISTIC — the same field always seeds the same way", () =>
        {
            // Re-seeding must never silently reshuffle a bracket an organiser
            // has already shown people.
            var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var b = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var at = DateTimeOffset.UnixEpoch;

            // Identical in every seedable respect, so only the tie-break decides.
            Entrant[] field = [new(a, 1500, false, at), new(b, 1500, false, at)];

            var first = Seeding.Order(field);
            var second = Seeding.Order(field.Reverse().ToArray());

            that(first.SequenceEqual(second),
                "two orderings of the same field must produce the same seeding");
        });

        check("seeding: earlier registration breaks a rating tie", () =>
        {
            var early = Guid.NewGuid();
            var late = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var order = Seeding.Order(
            [
                new Entrant(late, 1600, false, now),
                new Entrant(early, 1600, false, now.AddHours(-3)),
            ]);

            that(order[0] == early, "same rating, earlier entry seeds higher");
        });

        check("seeding: every entrant gets exactly one seed", () =>
        {
            var teams = Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()).ToArray();
            var now = DateTimeOffset.UtcNow;

            var order = Seeding.Order(teams.Select((id, i) =>
                new Entrant(id, i % 3 == 0 ? null : 1400 + i * 10, i % 4 == 0, now.AddMinutes(i))));

            that(order.Count == teams.Length, $"expected {teams.Length} seeds, got {order.Count}");
            that(order.Distinct().Count() == teams.Length, "a team must not appear twice");
            that(teams.All(order.Contains), "no team may be dropped from the seeding");
        });
    }
}
