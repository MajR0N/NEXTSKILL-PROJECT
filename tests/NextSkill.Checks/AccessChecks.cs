using NextSkill.Domain.Access;

namespace NextSkill.Checks;

/// <summary>
/// Authorization policy — decision 036.
///
/// Every combination is enumerable because the policy is a pure function. That
/// matters more here than anywhere: an authorization bug does not throw, it
/// simply lets the wrong person do something.
/// </summary>
public static class AccessChecks
{
    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();

        Actor Player() => new(Guid.NewGuid());
        Actor Captain(Guid team) => new(Guid.NewGuid(), Teams: new Dictionary<Guid, TeamRole> { [team] = TeamRole.Captain });
        Actor FormerCaptain(Guid team) => new(Guid.NewGuid(), RestorableTeams: new HashSet<Guid> { team });
        Actor Member(Guid team) => new(Guid.NewGuid(), Teams: new Dictionary<Guid, TeamRole> { [team] = TeamRole.Member });
        Actor OrgOwner(Guid org) => new(Guid.NewGuid(), Organisers: new Dictionary<Guid, OrganiserRole> { [org] = OrganiserRole.Owner });
        Actor OrgAdmin(Guid org) => new(Guid.NewGuid(), Organisers: new Dictionary<Guid, OrganiserRole> { [org] = OrganiserRole.Admin });
        Actor Referee(Guid org) => new(Guid.NewGuid(), Organisers: new Dictionary<Guid, OrganiserRole> { [org] = OrganiserRole.Referee });
        Actor Staff() => new(Guid.NewGuid(), Platform: PlatformRole.Staff);
        Actor Support() => new(Guid.NewGuid(), Platform: PlatformRole.Support);
        Actor Owner() => new(Guid.NewGuid(), Platform: PlatformRole.Owner);

        // ── the rule nothing overrides ───────────────────────────────────
        check("NOBODY can read a secret — not staff, not the platform owner", () =>
        {
            // RCON passwords, setinfo tokens, webhook URLs, server signing
            // secrets. The panel shows STATUS, never secrets (007, 012).
            foreach (var actor in new[] { Player(), Captain(teamA), OrgOwner(orgA), Referee(orgA), Staff(), Support(), Owner() })
                that(!AccessPolicy.Can(actor, Operation.ReadSecret, new ResourceRef(orgA, teamA)),
                    "ReadSecret must be denied to every role without exception");
        });

        // ── organiser scope ──────────────────────────────────────────────
        check("an organiser role does NOT carry across organisers", () =>
        {
            var ownerOfA = OrgOwner(orgA);
            that(AccessPolicy.Can(ownerOfA, Operation.ManageTournament, new ResourceRef(orgA)),
                "owner of A manages A");
            that(!AccessPolicy.Can(ownerOfA, Operation.ManageTournament, new ResourceRef(orgB)),
                "owner of A must NOT manage B — this is the tenancy boundary in policy form");
        });

        check("a referee runs matches but cannot change tournament structure", () =>
        {
            var ref_ = Referee(orgA);
            that(AccessPolicy.Can(ref_, Operation.AdvanceBracket, new ResourceRef(orgA)), "referee advances brackets");
            that(AccessPolicy.Can(ref_, Operation.ResolveDispute, new ResourceRef(orgA)), "referee resolves disputes");
            that(!AccessPolicy.Can(ref_, Operation.ManageTournament, new ResourceRef(orgA)),
                "a referee must not create or delete tournaments");
            that(!AccessPolicy.Can(ref_, Operation.RegisterServer, new ResourceRef(orgA)),
                "a referee must not register servers");
        });

        check("only an organiser OWNER manages staff or deletes the organiser", () =>
        {
            that(AccessPolicy.Can(OrgOwner(orgA), Operation.ManageOrganiserStaff, new ResourceRef(orgA)), "owner may");
            that(!AccessPolicy.Can(OrgAdmin(orgA), Operation.ManageOrganiserStaff, new ResourceRef(orgA)),
                "an admin must not grant themselves more power");
            that(!AccessPolicy.Can(OrgAdmin(orgA), Operation.DeleteOrganiser, new ResourceRef(orgA)), "nor delete the organiser");
        });

        // ── platform scope ───────────────────────────────────────────────
        check("maintenance mode and the trusted flag are OWNER only", () =>
        {
            that(AccessPolicy.Can(Owner(), Operation.SetMaintenanceMode), "owner may");
            that(!AccessPolicy.Can(Staff(), Operation.SetMaintenanceMode), "staff may not take the platform down");
            that(!AccessPolicy.Can(Staff(), Operation.SetOrganiserTrusted),
                "staff must not be able to grant trust, which bypasses the result-confirmation gate (007)");
        });

        check("staff moderate people but do not run other people's tournaments", () =>
        {
            var staff = Staff();
            that(AccessPolicy.Can(staff, Operation.ModerateUsers), "staff moderate users");
            that(AccessPolicy.Can(staff, Operation.ViewAuditLog), "staff read the audit log");
            that(AccessPolicy.Can(staff, Operation.ResolveDispute, new ResourceRef(orgA)),
                "staff resolve CROSS-ORGANISER disputes — the one organiser-scoped thing they reach");
            that(!AccessPolicy.Can(staff, Operation.ManageTournament, new ResourceRef(orgA)),
                "but staff must not edit an organiser's tournaments");
            that(!AccessPolicy.Can(staff, Operation.RegisterServer, new ResourceRef(orgA)),
                "nor register servers on their behalf");
        });

        check("support is read-only", () =>
        {
            var s = Support();
            that(AccessPolicy.Can(s, Operation.ViewServerHealth, new ResourceRef(orgA)), "support sees diagnostics");
            that(!AccessPolicy.Can(s, Operation.ModerateUsers), "support does not moderate");
            that(!AccessPolicy.Can(s, Operation.ResolveDispute, new ResourceRef(orgA)), "support does not decide disputes");
            that(!AccessPolicy.Can(s, Operation.ViewAuditLog), "support does not read the audit log");
        });

        // ── team scope ───────────────────────────────────────────────────
        check("only a CAPTAIN manages a roster, and only their own team", () =>
        {
            var cap = Captain(teamA);
            that(AccessPolicy.Can(cap, Operation.ManageRoster, new ResourceRef(TeamId: teamA)), "own team");
            that(!AccessPolicy.Can(cap, Operation.ManageRoster, new ResourceRef(TeamId: teamB)), "not another team");
            that(!AccessPolicy.Can(Member(teamA), Operation.ManageRoster, new ResourceRef(TeamId: teamA)),
                "a member must not change the roster");
        });

        check("even the platform OWNER cannot manage someone else's roster", () =>
        {
            // Deliberate: that is not moderation, it is playing someone else's
            // game for them. Bans and removals are ModerateUsers instead.
            that(!AccessPolicy.Can(Owner(), Operation.ManageRoster, new ResourceRef(TeamId: teamA)),
                "roster management is not a moderation power");
        });

        check("only a captain present at disband may restore that exact team", () =>
        {
            var former = FormerCaptain(teamA);
            that(AccessPolicy.Can(former, Operation.RestoreTeam, new ResourceRef(TeamId: teamA)),
                "a captain whose tenure ended at disband may restore that team");
            that(!AccessPolicy.Can(former, Operation.RestoreTeam, new ResourceRef(TeamId: teamB)),
                "the historical entitlement must not carry across teams");
            that(!AccessPolicy.Can(Captain(teamA), Operation.RestoreTeam, new ResourceRef(TeamId: teamA)),
                "a current captain role alone is not proof of presence at disband");
            that(!AccessPolicy.Can(Owner(), Operation.RestoreTeam, new ResourceRef(TeamId: teamA)),
                "the platform owner must not revive someone else's team");
        });

        check("confirming and disputing a result belong to the captain who played", () =>
        {
            // These are the corroboration that anti-match-fixing rests on (007).
            that(AccessPolicy.Can(Captain(teamA), Operation.ConfirmResult, new ResourceRef(TeamId: teamA)), "captain confirms");
            that(AccessPolicy.Can(Captain(teamA), Operation.DisputeResult, new ResourceRef(TeamId: teamA)), "captain disputes");
            that(!AccessPolicy.Can(Member(teamA), Operation.ConfirmResult, new ResourceRef(TeamId: teamA)),
                "a member must not confirm on the team's behalf");
            that(!AccessPolicy.Can(Captain(teamA), Operation.DisputeResult, new ResourceRef(TeamId: teamB)),
                "a captain must not dispute a match they did not play");
        });

        // ── the default ──────────────────────────────────────────────────
        check("an ordinary player with no roles can do nothing privileged", () =>
        {
            var p = Player();
            foreach (var op in Enum.GetValues<Operation>())
                that(!AccessPolicy.Can(p, op, new ResourceRef(orgA, teamA)),
                    $"a roleless player must not be able to {op}");
        });

        check("Require() throws rather than returning false", () =>
        {
            var threw = false;
            try { AccessPolicy.Require(Player(), Operation.SetMaintenanceMode); }
            catch (UnauthorizedAccessException) { threw = true; }
            that(threw, "Require must throw so a missed check cannot silently continue");
        });
    }
}
