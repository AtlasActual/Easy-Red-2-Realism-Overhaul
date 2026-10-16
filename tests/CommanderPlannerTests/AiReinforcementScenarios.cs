using ER2RealismOverhaul;

internal static class AiReinforcementScenarios
{
    internal static void LargeRepeatedWavesServiceEverySoldier()
    {
        const int capacity = 12;
        for (var count = 1; count <= 512; count++)
        {
            var budget = new RotatingFrameBudgetCore(capacity);
            var serviced = new bool[count];
            var frames = (count + capacity - 1) / capacity;
            for (var frame = 0; frame < frames; frame++)
            {
                var used = 0;
                // Reproduce the native iteration order repeating each wave.
                for (var soldier = 0; soldier < count; soldier++)
                {
                    if (!budget.TryTake(frame))
                        continue;
                    serviced[soldier] = true;
                    used++;
                }
                Require(used == Math.Min(capacity, count),
                    $"Wave of {count} soldiers used {used} slots on frame {frame}.");
            }
            Require(serviced.All(value => value),
                $"The tail of a repeated {count}-soldier wave was starved.");
        }
    }

    internal static void ChangingWavesKeepTheFrameLimitAndRecoverAfterBursts()
    {
        var budget = new RotatingFrameBudgetCore(12);
        var counts = new[] { 500, 3, 3, 600, 80, 1, 1, 24, 24, 5, 5 };
        for (var frame = 0; frame < counts.Length; frame++)
        {
            var used = 0;
            for (var index = 0; index < counts[frame]; index++)
                if (budget.TryTake(frame * 20))
                    used++;
            Require(used <= 12, "Changing wave size bypassed the per-frame work limit.");
            if (frame > 0 && counts[frame] <= 12 && counts[frame - 1] <= 12)
                Require(used == counts[frame], "A completed burst kept blocking the small rolling schedule.");
        }
    }

    internal static void PhaseResetRestartsBudgetWithoutOldWaveOffsets()
    {
        var budget = new RotatingFrameBudgetCore(12);
        for (var frame = 0; frame < 3; frame++)
            for (var index = 0; index < 200; index++)
                budget.TryTake(frame);

        budget.Clear();
        for (var index = 0; index < 12; index++)
            Require(budget.TryTake(2), "A phase reset kept an old wave's admission offset or spent slots.");
        Require(!budget.TryTake(2), "A phase reset removed the frame limit.");
    }

    internal static void RedirectedDefendOrdersCannotHideBehindTheSameOrderKind()
    {
        var redirectedHoldMatches = SquadOrderContinuityCore.NativeOrderMatches(
            orderKindMatches: true, isDefendOrder: true, holdAreaMatches: false);
        Require(SquadOrderContinuityCore.ShouldReissue(true, redirectedHoldMatches),
            "A HoldArea redirected to another point or spawn prevented reinforcement recovery.");
        Require(SquadOrderContinuityCore.NativeOrderMatches(true, true, true),
            "An unchanged hold area churned the native order.");
        Require(SquadOrderContinuityCore.NativeOrderMatches(true, false, false),
            "A native attack waypoint advance restarted the coordinated attack.");
        Require(!SquadOrderContinuityCore.NativeOrderMatches(false, false, true),
            "A changed native order kind kept the old assignment guard.");
    }

    internal static void NewDefensiveAreaReleasesSpawnAnchorBeforeReinforcement()
    {
        var spawn = new MapPoint(10f, 20f);
        var objective = new MapPoint(300f, 220f);
        var sameArea = DefensivePositionOwnershipCore.SameAssignedArea(spawn, 30f, objective, 30f);
        Require(!DefensivePositionOwnershipCore.ShouldOwn(new DefensivePositionOwnershipInput(
                AlreadyOwned: true, EligibleDefender: true, InsideAssignedArea: false,
                SameSquad: true, SameObjectiveRevision: true, SameAssignedArea: sameArea)),
            "A spawn cover anchor survived a reinforcement order with the same constant revision.");
        Require(!DefensivePositionOwnershipCore.ShouldOwn(new DefensivePositionOwnershipInput(
                false, true, false, true, true)),
            "The reinforcement acquired a defensive hold before reaching its new area.");
        Require(DefensivePositionOwnershipCore.ShouldOwn(new DefensivePositionOwnershipInput(
                false, true, true, true, true)),
            "Arriving reinforcements could not acquire the new objective's defensive area.");
    }

    internal static void DefensiveAreaIdentityToleratesJitterButRejectsChangedBounds()
    {
        var center = new MapPoint(10f, 20f);
        Require(DefensivePositionOwnershipCore.SameAssignedArea(center, 30f, new MapPoint(10.5f, 20f), 30.25f),
            "Small native area jitter released a stable defensive anchor.");
        Require(!DefensivePositionOwnershipCore.SameAssignedArea(center, 30f, center, 15f),
            "An anchor survived a materially changed hold radius.");
        Require(!DefensivePositionOwnershipCore.SameAssignedArea(center, 30f, new MapPoint(float.NaN, 20f), 30f),
            "An invalid defensive area retained ownership.");
        Require(!DefensivePositionOwnershipCore.SameAssignedArea(center, 30f, center, float.PositiveInfinity),
            "An invalid hold radius retained ownership.");
        Require(DefensivePositionOwnershipCore.ShouldOwn(new DefensivePositionOwnershipInput(
                true, true, false, true, true, SameAssignedArea: true)),
            "Incidental soldier displacement released an unchanged defensive area.");
    }

    internal static void DefendersKeepAllThreeHeldPointsAcrossRankingsAndPressure()
    {
        var rankings = new[]
        {
            new[] { 10, 11, 12 }, new[] { 10, 12, 11 }, new[] { 11, 10, 12 },
            new[] { 11, 12, 10 }, new[] { 12, 10, 11 }, new[] { 12, 11, 10 }
        };
        foreach (var ranking in rankings)
        {
            for (var count = 3; count <= 60; count++)
            {
                foreach (var pressured in ranking)
                {
                    var targets = ranking.Select(id => new ObjectiveCapacityInput(
                        id, Pressured: id == pressured, EnemySecured: false)).ToArray();
                    var capacities = ObjectiveCounterAttackPlanCore.BuildCapacities(count, targets, attacking: false);
                    Require(capacities.Count == 3 && capacities.Values.All(value => value >= 1),
                        "Distance ranking or pressure abandoned one of three held points.");
                    Require(capacities.Values.Sum() == count, "Defensive allocation stranded reinforcements.");
                    var selected = ObjectiveCounterAttackPlanCore.SelectCounterAttackTargets(
                        ranking.Select(id => new CounterAttackCandidate(id, false, false)).ToArray());
                    Require(selected.Count == 3, "The recapture gate removed a held defensive point.");
                }
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
