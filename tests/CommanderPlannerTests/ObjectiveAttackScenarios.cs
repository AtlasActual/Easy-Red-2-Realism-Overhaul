using ER2RealismOverhaul;

internal static class ObjectiveAttackScenarios
{
    internal static void AttackersCoverAllActiveObjectives()
    {
        var objectives = new[] { Target(10), Target(11), Target(12) };
        var selected = Select(5, objectives);
        EqualIds(new[] { 10, 11, 12 }, selected,
            "Attackers concentrated on one point despite enough squads to attack every active objective.");
        var capacities = ObjectiveAttackPlanCore.BuildCapacities(5, selected);
        Equal(2, capacities[10], "The nearest unfinished objective lost its share of surplus squads.");
        Equal(2, capacities[11], "The second unfinished objective lost its share of surplus squads.");
        Equal(1, capacities[12], "The third unfinished objective was left uncovered.");
    }

    internal static void CaptureKeepsOneGarrisonWhileOthersAdvance()
    {
        var objectives = new[] { Target(10), Target(11), Target(12) };
        var before = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
        Require(before.Values.All(count => count == 2),
            "The initial assault did not spread across three unfinished objectives.");

        objectives[0] = Target(10, held: true);
        var after = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
        Equal(1, after[10], "Capturing A sent its entire group onward or retained excessive strength.");
        Equal(3, after[11], "The first unfinished objective did not receive the freed assault squad.");
        Equal(2, after[12], "Capturing A withdrew the assault from C.");
        Require(!ObjectiveCounterAttackPlanCore.ShouldIssueAttackOrder(true, false, true),
            "The squad left at A kept an assault order instead of a hold order.");

        for (var refresh = 0; refresh < 4; refresh++)
        {
            var repeated = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
            Require(after.OrderBy(pair => pair.Key).SequenceEqual(repeated.OrderBy(pair => pair.Key)),
                "An unchanged planning refresh released A's garrison or reshuffled assault capacity.");
        }
    }

    internal static void RecaptureKeepsOtherGarrisonsAndRestoresAttack()
    {
        var objectives = new[] { Target(10, held: true), Target(11, held: true), Target(12, held: true) };
        // The enemy retakes A before the phase advances. B and C still need guards.
        objectives[0] = Target(10);
        var capacities = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
        Equal(4, capacities[10], "Recapturing A did not receive all available assault strength.");
        Equal(1, capacities[11], "Recapturing A stripped or overstaffed B's garrison.");
        Equal(1, capacities[12], "Recapturing A stripped or overstaffed C's garrison.");
        Require(ObjectiveCounterAttackPlanCore.ShouldIssueAttackOrder(true, true, false),
            "An enemy recapture did not restore an attacker assault order.");
        Require(ObjectiveCounterAttackPlanCore.ShouldIssueAttackOrder(true, false, false),
            "A contested point stopped receiving an assault before it was secured.");

        objectives[0] = Target(10, held: true);
        capacities = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
        Require(capacities.Values.All(count => count == 2),
            "Retaking A left all reserve strength concentrated there after every point was held.");
    }

    internal static void PhaseTransitionReleasesCompletedObjectives()
    {
        var phaseOne = new[] { Target(10, held: true), Target(11) };
        Require(ObjectiveEligibilityCore.ShouldInclude(false, true, false),
            "A held capture point disappeared while another point in its phase remained unfinished.");
        var capacities = ObjectiveAttackPlanCore.BuildCapacities(4, Select(4, phaseOne));
        Equal(1, capacities[10], "The completed point lost its garrison before the phase advanced.");
        Equal(3, capacities[11], "The unfinished point did not retain the remaining assault strength.");

        Require(!ObjectiveEligibilityCore.ShouldInclude(false, false, false),
            "An inactive point from the completed phase retained a garrison allocation.");
        var phaseTwo = new[] { Target(20), Target(21), Target(22) };
        capacities = ObjectiveAttackPlanCore.BuildCapacities(4, Select(4, phaseTwo));
        Require(!capacities.ContainsKey(10) && !capacities.ContainsKey(11),
            "The new phase retained assignments to completed objectives.");
        Require(capacities.Keys.OrderBy(id => id).SequenceEqual(new[] { 20, 21, 22 }),
            "The next phase failed to cover every new objective.");
        Equal(4, capacities.Values.Sum(), "The phase transition stranded squads at old objectives.");
    }

    internal static void ScarceSquadsBalanceHoldingAndProgress()
    {
        // Input is distance ranked, with unfinished points nearer than held points.
        var objectives = new[]
        {
            Target(10), Target(11), Target(12, held: true),
            Target(13, held: true), Target(14, held: true)
        };
        EqualIds(new[] { 10, 12, 13 }, Select(3, objectives),
            "Scarce squads did not preserve nearby held points while reserving one assault squad.");
        EqualIds(new[] { 10, 12 }, Select(2, objectives),
            "Two available squads failed to retain one guard and one assault squad.");
        EqualIds(new[] { 10 }, Select(1, objectives),
            "The only available squad stayed behind instead of advancing the unfinished phase.");

        var heldFirst = new[] { Target(20, held: true), Target(21, held: true), Target(22) };
        EqualIds(new[] { 20, 22 }, Select(2, heldFirst),
            "Nearby held points consumed every squad and permanently stalled the phase.");
    }

    internal static void UniformOwnershipUsesNearestAvailableTargets()
    {
        EqualIds(new[] { 10, 11 }, Select(2, new[] { Target(10), Target(11), Target(12) }),
            "A scarce assault ignored the supplied distance ranking.");
        EqualIds(new[] { 20, 21 }, Select(2, new[]
        {
            Target(20, held: true), Target(21, held: true), Target(22, held: true)
        }), "A scarce all-held plan ignored the supplied distance ranking.");
    }

    internal static void FinishedPhaseSpreadsAcrossHeldObjectives()
    {
        var selected = Select(8, new[] { Target(10, held: true), Target(11, held: true), Target(12, held: true) });
        var capacities = ObjectiveAttackPlanCore.BuildCapacities(8, selected);
        Equal(3, capacities[10], "Surplus squads did not reinforce the first held point.");
        Equal(3, capacities[11], "Surplus squads did not reinforce the second held point.");
        Equal(2, capacities[12], "Surplus squads left the last held point understrength.");
    }

    internal static void EmptyPlansDoNotCreateAssignments()
    {
        foreach (var squadCount in new[] { -2, 0 })
        {
            Equal(0, ObjectiveAttackPlanCore.SelectTargets(squadCount, new[] { Target(10) }).Count,
                "A plan without squads selected an objective.");
            Equal(0, ObjectiveAttackPlanCore.BuildCapacities(squadCount, new[] { Target(10) }).Count,
                "A plan without squads created a garrison capacity.");
        }
        Equal(0, ObjectiveAttackPlanCore.SelectTargets(5, Array.Empty<ObjectiveAttackTarget>()).Count,
            "An empty phase selected an objective.");
        Equal(0, ObjectiveAttackPlanCore.BuildCapacities(5, Array.Empty<ObjectiveAttackTarget>()).Count,
            "An empty phase created objective capacity.");
    }

    internal static void AttackAllocationPreservesCoverageAndSquadCount()
    {
        // Exercise every ownership pattern, including several held points
        // before the first unfinished point in the distance ranking.
        for (var objectiveCount = 1; objectiveCount <= 6; objectiveCount++)
        for (var ownership = 0; ownership < (1 << objectiveCount); ownership++)
        for (var squadCount = 1; squadCount <= 9; squadCount++)
        {
            var objectives = Enumerable.Range(0, objectiveCount)
                .Select(index => Target(100 + index, held: (ownership & (1 << index)) != 0))
                .ToArray();
            var ids = ObjectiveAttackPlanCore.SelectTargets(squadCount, objectives);
            var context = $"objectives={objectiveCount}, ownership={ownership}, squads={squadCount}";
            Equal(Math.Min(squadCount, objectiveCount), ids.Count, $"Incorrect coverage: {context}.");
            Equal(ids.Count, ids.Distinct().Count(), $"An objective was selected twice: {context}.");
            Require(ids.All(id => objectives.Any(target => target.Id == id)),
                $"An objective outside this phase was selected: {context}.");
            var selected = objectives.Where(target => ids.Contains(target.Id)).ToArray();
            if (objectives.Any(target => !target.FriendlySecured))
            {
                Require(selected.Any(target => !target.FriendlySecured),
                    $"Every squad stayed on held points while the phase was unfinished: {context}.");
                Equal(Math.Min(objectives.Count(target => target.FriendlySecured), squadCount - 1),
                    selected.Count(target => target.FriendlySecured),
                    $"A holdable point lost its available garrison: {context}.");
            }

            var capacities = ObjectiveAttackPlanCore.BuildCapacities(squadCount, selected);
            EqualIds(ids, capacities.Keys, $"Capacity was assigned outside selected objectives: {context}.");
            Equal(squadCount, capacities.Values.Sum(), $"Squads were lost or duplicated: {context}.");
            Require(capacities.Values.All(count => count >= 1),
                $"A selected objective received no squad: {context}.");

            var unfinished = selected.Where(target => !target.FriendlySecured).ToArray();
            if (unfinished.Length > 0)
            {
                Require(selected.Where(target => target.FriendlySecured)
                        .All(target => capacities[target.Id] == 1),
                    $"A held point took surplus assault squads: {context}.");
                var assaultLoads = unfinished.Select(target => capacities[target.Id]).ToArray();
                Require(assaultLoads.Max() - assaultLoads.Min() <= 1,
                    $"Assault strength concentrated on one unfinished point: {context}.");
            }
            else
            {
                Require(capacities.Values.Max() - capacities.Values.Min() <= 1,
                    $"An all-held phase concentrated its reserves: {context}.");
            }
        }
    }

    private static ObjectiveAttackTarget Target(int id, bool held = false) => new(id, held);

    private static ObjectiveAttackTarget[] Select(int squadCount, IReadOnlyList<ObjectiveAttackTarget> objectives)
    {
        var ids = ObjectiveAttackPlanCore.SelectTargets(squadCount, objectives);
        return objectives.Where(target => ids.Contains(target.Id)).ToArray();
    }

    private static void EqualIds(IEnumerable<int> expected, IEnumerable<ObjectiveAttackTarget> actual, string message)
        => EqualIds(expected, actual.Select(target => target.Id), message);

    private static void EqualIds(IEnumerable<int> expected, IEnumerable<int> actual, string message)
        => Require(expected.OrderBy(id => id).SequenceEqual(actual.OrderBy(id => id)), message);

    private static void Equal(int expected, int actual, string message)
        => Require(expected == actual, $"{message} Expected={expected}; Actual={actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
