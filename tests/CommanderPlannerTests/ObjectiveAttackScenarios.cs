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

    internal static void CaptureKeepsProportionalGarrisonWhileOthersAdvance()
    {
        var objectives = new[] { Target(10), Target(11), Target(12) };
        var before = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
        Require(before.Values.All(count => count == 2),
            "The initial assault did not spread across three unfinished objectives.");

        objectives[0] = Target(10, held: true);
        var after = ObjectiveAttackPlanCore.BuildCapacities(6, Select(6, objectives));
        Equal(1, after[10], "Capturing A did not retain the rounded-down 20% garrison.");
        Equal(3, after[11], "Rounding down did not release the surplus guard to the assault.");
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
            "Scarce squads failed to cover captured points while retaining an assault squad.");
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

    internal static void GarrisonScalesWithAvailableSquads()
    {
        var objectives = new[] { Target(10, held: true), Target(11), Target(12) };
        foreach (var (squads, guards) in new[]
                 { (10, 2), (20, 4), (50, 10), (15, 3), (14, 2), (11, 2), (9, 1), (6, 1), (5, 1), (4, 1), (1, 0) })
        {
            var capacities = ObjectiveAttackPlanCore.BuildCapacities(squads, Select(squads, objectives));
            Equal(guards, capacities.GetValueOrDefault(10),
                $"The garrison failed to scale after the eligible force changed to {squads} squads.");
            Equal(squads - guards, capacities.Where(pair => pair.Key != 10).Sum(pair => pair.Value),
                "Percentage allocation lost assault strength.");
        }
    }

    internal static void CapturesShareOneGarrisonBudget()
    {
        var objectives = new[] { Target(10, held: true), Target(11), Target(12), Target(13) };
        for (var captured = 1; captured <= 3; captured++)
        {
            objectives[captured - 1] = Target(9 + captured, held: true);
            var selected = Select(20, objectives);
            var capacities = ObjectiveAttackPlanCore.BuildCapacities(20, selected);
            var guards = selected.Where(target => target.FriendlySecured)
                .Select(target => capacities[target.Id]).ToArray();
            Equal(4, guards.Sum(), "Each additional capture multiplied the shared 20% budget.");
            Require(guards.Max() - guards.Min() <= 1,
                "The garrison budget was concentrated at one captured objective.");
            Equal(16, selected.Where(target => !target.FriendlySecured).Sum(target => capacities[target.Id]),
                "Capturing another point reduced the 80% assault force.");
        }
    }

    internal static void ConfiguredPercentageControlsBothSelectionAndCapacity()
    {
        var objectives = new[] { Target(10, held: true), Target(11, held: true), Target(12) };
        foreach (var (percent, guards) in new[] { (0, 0), (10, 2), (20, 2), (35, 3), (50, 5), (100, 9) })
        {
            var selected = Select(10, objectives, percent);
            var capacities = ObjectiveAttackPlanCore.BuildCapacities(10, selected, percent);
            Equal(guards, selected.Where(target => target.FriendlySecured).Sum(target => capacities[target.Id]),
                $"The configured {percent}% was ignored or applied separately to each captured point.");
            Equal(10 - guards, capacities[12], "The configured garrison budget stopped the assault.");
        }
        Equal(0, ObjectiveAttackPlanCore.CalculateGarrisonBudget(10, -10),
            "A negative percentage created garrison squads.");
        Equal(9, ObjectiveAttackPlanCore.CalculateGarrisonBudget(10, 110),
            "An excessive percentage stopped the unfinished phase.");
    }

    internal static void MinimumGarrisonsCoverCapturedPointsWithoutStoppingAssault()
    {
        var objectives = new[] { Target(10, held: true), Target(11, held: true), Target(12) };
        var capacities = ObjectiveAttackPlanCore.BuildCapacities(5, Select(5, objectives));
        Equal(1, capacities[10], "The first captured objective lost minimum coverage.");
        Equal(1, capacities[11], "A 20% budget of one squad left the second captured objective empty.");
        Equal(3, capacities[12], "Minimum garrisons took more than necessary from the assault.");

        var onlyOne = Select(1, objectives);
        EqualIds(new[] { 12 }, onlyOne, "Minimum garrisons stopped the last assault squad.");
        var disabled = Select(5, objectives, percent: 0);
        EqualIds(new[] { 12 }, disabled, "The minimum overruled the explicit zero-percent opt-out.");
        Equal(5, ObjectiveAttackPlanCore.BuildCapacities(5, disabled, 0)[12],
            "Disabling garrisons stranded squads at captured points.");
    }

    internal static void PressurePrioritizesSurplusGuardsWithoutBorrowingAttackers()
    {
        var objectives = new[]
        {
            Target(10, held: true), Target(11, held: true, pressured: true), Target(12), Target(13)
        };
        var pressured = ObjectiveAttackPlanCore.BuildCapacities(20, Select(20, objectives));
        Equal(1, pressured[10], "Pressure stripped the quiet point's minimum garrison.");
        Equal(3, pressured[11], "The threatened point did not receive surplus guards.");
        Equal(8, pressured[12], "Pressure borrowed squads from the first ongoing assault.");
        Equal(8, pressured[13], "Pressure borrowed squads from the second ongoing assault.");

        var scarce = ObjectiveAttackPlanCore.BuildCapacities(5, Select(5, objectives));
        Equal(1, scarce[10], "Pressure stole the last guard from a quiet point.");
        Equal(1, scarce[11], "A threatened point lost minimum coverage.");
        Equal(3, scarce[12] + scarce[13], "Pressure increased a garrison without surplus guards.");

        objectives[1] = Target(11, held: true);
        var quiet = ObjectiveAttackPlanCore.BuildCapacities(20, Select(20, objectives));
        Equal(2, quiet[10], "Cleared pressure did not restore balanced garrisons.");
        Equal(2, quiet[11], "Cleared pressure retained excess guards indefinitely.");
        Equal(pressured[12], quiet[12], "Pressure clearing changed ongoing assault capacity.");
        Equal(pressured[13], quiet[13], "Pressure clearing changed ongoing assault capacity.");
    }

    internal static void ScarceGarrisonsPrioritizePressureBeforeDistance()
    {
        var objectives = new[]
        {
            Target(10, held: true), Target(11), Target(12, held: true, pressured: true)
        };
        var selected = Select(2, objectives);
        EqualIds(new[] { 11, 12 }, selected,
            "A nearer quiet captured point took the only guard from a threatened point.");
        var capacities = ObjectiveAttackPlanCore.BuildCapacities(2, selected);
        Equal(1, capacities[11], "Threat prioritization stopped the assault.");
        Equal(1, capacities[12], "The scarce guard was not assigned to the threatened point.");
    }

    internal static void MultipleThreatsShareSurplusAndIgnoreDistanceReordering()
    {
        var objectives = new[]
        {
            Target(10, held: true), Target(11, held: true, pressured: true),
            Target(12, held: true, pressured: true), Target(13), Target(14)
        };
        var expected = ObjectiveAttackPlanCore.BuildCapacities(30, Select(30, objectives));
        Equal(1, expected[10], "The quiet objective took surplus from threatened points.");
        Equal(3, expected[11], "The first threatened objective lost its fair surplus share.");
        Equal(2, expected[12], "The second threatened objective lost its fair surplus share.");
        Equal(24, expected[13] + expected[14], "Multiple threats took squads beyond the reserve budget.");

        // Movement can reorder the same objectives relative to the faction centroid.
        // It must not swap surplus capacities and force otherwise valid squads to move.
        for (var rotation = 0; rotation < objectives.Length; rotation++)
        {
            var reordered = objectives.Skip(rotation).Concat(objectives.Take(rotation)).Reverse().ToArray();
            var actual = ObjectiveAttackPlanCore.BuildCapacities(30, Select(30, reordered));
            Require(expected.OrderBy(pair => pair.Key).SequenceEqual(actual.OrderBy(pair => pair.Key)),
                "A distance-ranking change reshuffled capacity between unchanged objectives.");
        }

        var allHeld = objectives.Select(target => target with { FriendlySecured = true }).ToArray();
        var secured = ObjectiveAttackPlanCore.BuildCapacities(10, Select(10, allHeld));
        Equal(1, secured[10], "An all-held phase lost the quiet point's minimum guard.");
        Equal(10, secured.Values.Sum(), "An all-held pressure response lost squads.");
        Require(secured[11] + secured[12] > secured[10] + secured[13] + secured[14],
            "An all-held phase did not prioritize threatened points for surplus guards.");
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
        for (var squadCount = 1; squadCount <= 60; squadCount++)
        foreach (var percent in new[] { 0, 10, 20, 35, 50, 100 })
        {
            var objectives = Enumerable.Range(0, objectiveCount)
                .Select(index => Target(100 + index, held: (ownership & (1 << index)) != 0))
                .ToArray();
            var ids = ObjectiveAttackPlanCore.SelectTargets(squadCount, objectives, percent);
            var context = $"objectives={objectiveCount}, ownership={ownership}, squads={squadCount}, percent={percent}";
            var heldCount = objectives.Count(target => target.FriendlySecured);
            var unfinishedCount = objectiveCount - heldCount;
            var budget = unfinishedCount == 0 ? squadCount : heldCount == 0 ? 0
                : percent == 0 ? 0
                : Math.Min(squadCount - 1, Math.Max(heldCount, squadCount * percent / 100));
            Equal(Math.Min(heldCount, budget) + Math.Min(unfinishedCount, squadCount - budget), ids.Count,
                $"Incorrect coverage within each percentage budget: {context}.");
            Equal(ids.Count, ids.Distinct().Count(), $"An objective was selected twice: {context}.");
            Require(ids.All(id => objectives.Any(target => target.Id == id)),
                $"An objective outside this phase was selected: {context}.");
            var selected = objectives.Where(target => ids.Contains(target.Id)).ToArray();
            if (objectives.Any(target => !target.FriendlySecured))
            {
                Require(selected.Any(target => !target.FriendlySecured),
                    $"Every squad stayed on held points while the phase was unfinished: {context}.");
                Equal(Math.Min(heldCount, budget),
                    selected.Count(target => target.FriendlySecured),
                    $"A holdable point lost its available garrison: {context}.");
            }

            var capacities = ObjectiveAttackPlanCore.BuildCapacities(squadCount, selected, percent);
            EqualIds(ids, capacities.Keys, $"Capacity was assigned outside selected objectives: {context}.");
            Equal(squadCount, capacities.Values.Sum(), $"Squads were lost or duplicated: {context}.");
            Require(capacities.Values.All(count => count >= 1),
                $"A selected objective received no squad: {context}.");

            var unfinished = selected.Where(target => !target.FriendlySecured).ToArray();
            if (unfinished.Length > 0)
            {
                var guardLoads = selected.Where(target => target.FriendlySecured)
                    .Select(target => capacities[target.Id]).ToArray();
                Equal(budget, guardLoads.Sum(), $"The shared percentage budget was not respected: {context}.");
                Require(guardLoads.Length == 0 || guardLoads.Max() - guardLoads.Min() <= 1,
                    $"Garrison strength concentrated on one captured point: {context}.");
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

    private static ObjectiveAttackTarget Target(int id, bool held = false, bool pressured = false)
        => new(id, held, pressured);

    private static ObjectiveAttackTarget[] Select(int squadCount, IReadOnlyList<ObjectiveAttackTarget> objectives,
        int percent = ObjectiveAttackPlanCore.DefaultGarrisonPercent)
    {
        var ids = ObjectiveAttackPlanCore.SelectTargets(squadCount, objectives, percent);
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
