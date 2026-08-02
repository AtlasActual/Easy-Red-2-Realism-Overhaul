using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// A deliberately small objective-order layer. It spreads autonomous infantry
/// squads across active objectives, gives part of each attacking group a distinct
/// approach angle, and keeps at least one defending squad on every objective when
/// enough squads exist. It does not stage attacks, reserve units, claim command
/// channels, or suppress Easy Red 2's native squad-leader routine.
/// </summary>
internal static class ObjectiveCoordination
{
    private const float PlanningIntervalSeconds = 24f;
    private const float PressureChangeThreshold = 0.02f;
    private const float PressureMemorySeconds = 45f;
    private const float FlankAngleDegrees = 38f;
    private const int DefensiveCoverCandidateLimit = 64;
    private const float DefensiveCoverObjectiveToleranceMeters = 8f;
    private const float DefensiveCoverAnchorSpacingMeters = 22f;

    private static readonly Dictionary<int, OrderStamp> LastOrders = new();
    private static readonly Dictionary<int, float> ObjectiveProgress = new();
    private static readonly Dictionary<int, float> ObjectivePressureUntil = new();
    private static readonly Dictionary<string, int> LastPlanSignatures =
        new(StringComparer.Ordinal);

    private static float _nextPlanAt;
    private static bool _failedThisBattle;

    internal static void Update(BattleManager manager, float now)
    {
        if (!Settings.ObjectiveCoordinationEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            !BattleManager.IsBattleActive() ||
            _failedThisBattle ||
            now < _nextPlanAt)
        {
            return;
        }

        _nextPlanAt = now + PlanningIntervalSeconds;
        try
        {
            Plan(manager, now);
        }
        catch (Exception ex)
        {
            _failedThisBattle = true;
            Plugin.LogSource.LogWarning(
                $"Objective coordination disabled for this battle after an unexpected error: {ex.Message}");
        }
    }

    internal static void ResetBattle()
    {
        LastOrders.Clear();
        ObjectiveProgress.Clear();
        ObjectivePressureUntil.Clear();
        LastPlanSignatures.Clear();
        _nextPlanAt = 0f;
        _failedThisBattle = false;
    }

    private static void Plan(BattleManager manager, float now)
    {
        var battle = BattleManager.GetCurrentBattleData();
        if (battle == null)
            return;

        var squadsByFaction = CollectSquadsByFaction(battle);
        var activeSquadIds = new HashSet<int>();

        foreach (var pair in squadsByFaction)
        {
            foreach (var squad in pair.Value)
                activeSquadIds.Add(squad.Id);

            var objectives = CollectObjectives(manager, battle, pair.Key, now);
            if (objectives.Count == 0)
                continue;

            var attacking = battle.IsInvaderFaction(pair.Key);
            if (attacking)
                objectives.RemoveAll(objective => objective.FriendlySecured);

            if (objectives.Count == 0)
                continue;

            PlanFaction(pair.Key, pair.Value, objectives, attacking, now);
        }

        foreach (var staleId in LastOrders.Keys.Where(id => !activeSquadIds.Contains(id)).ToArray())
            LastOrders.Remove(staleId);
    }

    private static Dictionary<string, List<SquadInfo>> CollectSquadsByFaction(BattleData battle)
    {
        var result = new Dictionary<string, List<SquadInfo>>(StringComparer.Ordinal);
        var allSquads = Squad.AllSquads;
        if (allSquads == null)
            return result;

        foreach (var pair in allSquads)
        {
            var squad = pair.Value;
            if (!TryDescribeSquad(squad, battle, out var info))
                continue;

            if (!result.TryGetValue(info.Faction, out var factionSquads))
            {
                factionSquads = new List<SquadInfo>();
                result.Add(info.Faction, factionSquads);
            }

            factionSquads.Add(info);
        }

        foreach (var factionSquads in result.Values)
            factionSquads.Sort((left, right) => left.Id.CompareTo(right.Id));

        return result;
    }

    private static bool TryDescribeSquad(Squad? squad, BattleData battle, out SquadInfo info)
    {
        info = default;
        try
        {
            if (squad == null || !squad.fullySpawned || squad.IsVehicleCrew ||
                !squad.HasAliveAIMembers() ||
                GroundAiDirector.IsExternallyControlledSquad(squad))
            {
                return false;
            }

            var leader = squad.Leader;
            if (leader == null || !leader.CanFight() || !AiOwnership.IsAutonomous(leader))
                return false;

            var faction = leader.faction ?? string.Empty;
            if (string.IsNullOrWhiteSpace(faction) ||
                (!battle.IsInvaderFaction(faction) && !battle.IsDefenderFaction(faction)))
            {
                return false;
            }

            var position = leader.transform.position;
            if (!IsFinite(position))
                return false;

            info = new SquadInfo(SquadIdentity.GetSquadId(squad), squad, faction, position);
            return true;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    private static List<ObjectiveInfo> CollectObjectives(
        BattleManager manager,
        BattleData battle,
        string faction,
        float now)
    {
        var result = new List<ObjectiveInfo>();
        var objectives = manager.ActiveObjectives(false).GetEnumerator();
        var iterator = objectives.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (iterator.MoveNext())
        {
            var objective = objectives.Current;
            if (objective == null || !objective.CanAttractFaction(faction))
                continue;

            var position = objective.GetTaskPosition();
            if (!IsFinite(position))
                continue;

            var id = manager.GetObjectiveUniqueId(objective);
            var friendlySecured = false;
            if (objective.IsSecured())
            {
                var securedFaction = objective.GetSecuredFaction();
                friendlySecured =
                    (!string.IsNullOrWhiteSpace(securedFaction) &&
                     battle.IsInvaderFaction(faction) &&
                     battle.IsInvaderFaction(securedFaction)) ||
                    (!string.IsNullOrWhiteSpace(securedFaction) &&
                     battle.IsDefenderFaction(faction) &&
                     battle.IsDefenderFaction(securedFaction));
            }

            var pressured = SampleObjectivePressure(objective, id, now);
            var radius = float.IsFinite(objective.objectiveRadius)
                ? Mathf.Max(12f, objective.objectiveRadius)
                : 30f;
            result.Add(new ObjectiveInfo(id, position, radius, friendlySecured, pressured));
        }

        return result;
    }

    private static bool SampleObjectivePressure(MissionObjective objective, int id, float now)
    {
        try
        {
            var progress = objective.missionTask?.GetTaskCompletationPercentage() ?? 0f;
            if (float.IsFinite(progress))
            {
                if (ObjectiveProgress.TryGetValue(id, out var previous) &&
                    Mathf.Abs(progress - previous) >= PressureChangeThreshold)
                {
                    ObjectivePressureUntil[id] = now + PressureMemorySeconds;
                }

                ObjectiveProgress[id] = progress;
            }
        }
        catch (ObjectCollectedException)
        {
            return false;
        }

        return ObjectivePressureUntil.TryGetValue(id, out var until) && now < until;
    }

    private static void PlanFaction(
        string faction,
        List<SquadInfo> squads,
        List<ObjectiveInfo> objectives,
        bool attacking,
        float now)
    {
        if (squads.Count == 0)
            return;

        var centroid = AveragePosition(squads);
        objectives.Sort((left, right) =>
        {
            var distanceComparison = HorizontalDistanceSquared(centroid, left.Position)
                .CompareTo(HorizontalDistanceSquared(centroid, right.Position));
            return distanceComparison != 0 ? distanceComparison : left.Id.CompareTo(right.Id);
        });

        var targetCount = attacking
            ? Math.Min(objectives.Count, Math.Max(1, (squads.Count + 1) / 2))
            : Math.Min(objectives.Count, squads.Count);
        var targets = objectives.Take(targetCount).ToList();
        var capacities = BuildCapacities(squads.Count, targets, attacking);
        var assignments = AssignSquads(squads, targets, capacities);

        var flankCount = 0;
        foreach (var target in targets)
        {
            if (!assignments.TryGetValue(target.Id, out var assigned))
                continue;

            assigned.Sort((left, right) => left.Id.CompareTo(right.Id));
            var defenseAnchors = new List<Vector3>();
            for (var index = 0; index < assigned.Count; index++)
            {
                if (attacking)
                {
                    var flank = assigned.Count >= 2 && index % 2 == 1;
                    IssueAttack(assigned[index], target, flank, index / 2);
                    if (flank)
                        flankCount++;
                }
                else
                {
                    IssueDefense(
                        assigned[index], target, index, assigned.Count, defenseAnchors);
                }
            }
        }

        TracePlanChange(faction, attacking, squads.Count, targets.Count, flankCount, assignments);
    }

    private static Dictionary<int, int> BuildCapacities(
        int squadCount,
        List<ObjectiveInfo> targets,
        bool attacking)
    {
        var capacities = targets.ToDictionary(objective => objective.Id, _ => 1);
        var remaining = squadCount - targets.Count;
        for (var index = 0; index < remaining; index++)
            capacities[targets[index % targets.Count].Id]++;

        if (attacking)
            return capacities;

        ObjectiveInfo? threatened = targets
            .Where(objective => objective.Pressured)
            .OrderBy(objective => objective.Id)
            .Select(objective => (ObjectiveInfo?)objective)
            .FirstOrDefault();
        if (!threatened.HasValue)
            return capacities;

        ObjectiveInfo? donor = targets
            .Where(objective => objective.Id != threatened.Value.Id && capacities[objective.Id] > 1)
            .OrderByDescending(objective => capacities[objective.Id])
            .ThenBy(objective => objective.Id)
            .Select(objective => (ObjectiveInfo?)objective)
            .FirstOrDefault();
        if (!donor.HasValue)
            return capacities;

        capacities[donor.Value.Id]--;
        capacities[threatened.Value.Id]++;
        return capacities;
    }

    private static Dictionary<int, List<SquadInfo>> AssignSquads(
        List<SquadInfo> squads,
        List<ObjectiveInfo> targets,
        Dictionary<int, int> capacities)
    {
        var assignments = targets.ToDictionary(
            objective => objective.Id,
            _ => new List<SquadInfo>());
        var assignedSquads = new HashSet<int>();

        // Keep a still-valid assignment first. Position changes should not make
        // squads trade objectives every planning tick.
        foreach (var squad in squads)
        {
            if (!LastOrders.TryGetValue(squad.Id, out var previous) ||
                !assignments.TryGetValue(previous.ObjectiveId, out var destination) ||
                destination.Count >= capacities[previous.ObjectiveId])
            {
                continue;
            }

            destination.Add(squad);
            assignedSquads.Add(squad.Id);
        }

        foreach (var squad in squads)
        {
            if (assignedSquads.Contains(squad.Id))
                continue;

            var target = targets
                .Where(candidate => assignments[candidate.Id].Count < capacities[candidate.Id])
                .OrderBy(candidate => HorizontalDistanceSquared(squad.Position, candidate.Position))
                .ThenBy(candidate => candidate.Id)
                .First();
            assignments[target.Id].Add(squad);
        }

        return assignments;
    }

    private static void IssueAttack(
        SquadInfo squad,
        ObjectiveInfo objective,
        bool flank,
        int flankOrdinal)
    {
        var direction = Flatten(objective.Position - squad.Position);
        if (direction.sqrMagnitude < 1f)
            direction = Vector3.forward;
        else
            direction.Normalize();

        if (flank)
        {
            var side = flankOrdinal % 2 == 0 ? -1f : 1f;
            direction = RotateHorizontal(direction, side * FlankAngleDegrees);
        }

        var proposed = new OrderStamp(
            objective.Id,
            flank ? CoordinatedOrder.Flank : CoordinatedOrder.DirectAttack,
            objective.Position,
            objective.Radius,
            Vector3.zero);
        if (!NeedsOrder(squad.Squad, proposed))
            return;

        try
        {
            squad.Squad.AttackFromSide(direction, objective.Position, objective.Radius);
            LastOrders[squad.Id] = proposed with
            {
                NativeDestination = squad.Squad.moveOrderPosition
            };
            AiState.Trace(
                $"Objective order: squad {squad.Id} {(flank ? "flank" : "attack")} -> {objective.Id}");
        }
        catch (ObjectCollectedException)
        {
            LastOrders.Remove(squad.Id);
        }
    }

    private static void IssueDefense(
        SquadInfo squad,
        ObjectiveInfo objective,
        int sectorIndex,
        int sectorCount,
        List<Vector3> existingAnchors)
    {
        var destination = objective.Position;
        if (sectorCount > 1)
        {
            var ringRadius = Mathf.Min(55f, objective.Radius * 0.45f);
            var angle = (2f * Mathf.PI * sectorIndex / sectorCount) +
                        StableAngleOffset(objective.Id);
            destination += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
        }

        var holdRadius = Mathf.Clamp(objective.Radius * 0.45f, 16f, 35f);
        var towardExpectedThreat = Flatten(destination - objective.Position);
        if (towardExpectedThreat.sqrMagnitude < 1f)
        {
            var angle = StableAngleOffset(objective.Id);
            towardExpectedThreat = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }
        else
        {
            towardExpectedThreat.Normalize();
        }

        var coverAnchored = TryFindDefensiveCoverAnchor(
                squad.Faction,
                objective,
                destination,
                towardExpectedThreat,
                holdRadius,
                existingAnchors,
                out var coverAnchor);
        if (coverAnchored)
        {
            destination = coverAnchor;
        }

        existingAnchors.Add(destination);
        var proposed = new OrderStamp(
            objective.Id,
            CoordinatedOrder.Defend,
            destination,
            holdRadius,
            Vector3.zero);
        if (!NeedsOrder(squad.Squad, proposed))
            return;

        try
        {
            // HoldArea synchronously calls the game's SendUnitsToCovers routine.
            // Authorize that one squad-level handoff so the individual CoverPosition
            // writes survive our exclusive-cover guard. Once HoldArea returns, the
            // ordinary guard owns the positions again and prevents cover churn.
            ContactResponse.ExecuteOwnedSquadCoverWrites(
                squad.Squad,
                () => squad.Squad.HoldArea(destination, holdRadius, false));
            LastOrders[squad.Id] = proposed with
            {
                NativeDestination = squad.Squad.moveOrderPosition
            };
            AiState.Trace(
                $"Objective order: squad {squad.Id} defend -> {objective.Id} " +
                (coverAnchored ? "cover-cluster" : "sector-fallback"));
        }
        catch (ObjectCollectedException)
        {
            LastOrders.Remove(squad.Id);
        }
    }

    private static bool TryFindDefensiveCoverAnchor(
        string faction,
        ObjectiveInfo objective,
        Vector3 idealPosition,
        Vector3 towardExpectedThreat,
        float holdRadius,
        List<Vector3> existingAnchors,
        out Vector3 anchor)
    {
        anchor = idealPosition;
        try
        {
            var searchRadius = Mathf.Clamp(objective.Radius * 0.6f, 24f, 50f);
            var covers = CoverManager.GetCovers(
                idealPosition,
                searchRadius,
                faction,
                towardExpectedThreat,
                true);
            if (covers == null)
                return false;

            var objectiveLimit =
                objective.Radius + DefensiveCoverObjectiveToleranceMeters;
            var objectiveLimitSqr = objectiveLimit * objectiveLimit;
            var candidates = new List<DefensiveCoverCandidate>();
            var examined = 0;
            foreach (var rawCover in covers)
            {
                if (++examined > DefensiveCoverCandidateLimit)
                    break;

                try
                {
                    var cover = rawCover.TryCast<AiDestination>();
                    if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero ||
                        cover.IsVehicle() || cover.IsCoverDestroyed() || cover.IsUnsafeCover() ||
                        !cover.IsCoverAvailable(towardExpectedThreat, faction) ||
                        !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(
                            cover, out var position) ||
                        !IsFinite(position) ||
                        HorizontalDistanceSquared(position, objective.Position) >
                        objectiveLimitSqr)
                    {
                        continue;
                    }

                    candidates.Add(new DefensiveCoverCandidate(
                        cover.Pointer,
                        position,
                        cover.GetCoverPose() != SoldierPose.Idle));
                }
                catch (NullReferenceException)
                {
                }
                catch (Il2CppException)
                {
                }
                catch (ObjectCollectedException)
                {
                }
            }

            if (candidates.Count == 0)
                return false;

            var clusterRadius = Mathf.Clamp(holdRadius, 16f, 28f);
            var clusterRadiusSqr = clusterRadius * clusterRadius;
            DefensiveCoverCandidate? best = null;
            var bestScore = float.MaxValue;
            foreach (var candidate in candidates)
            {
                var nearbySlots = 0;
                foreach (var other in candidates)
                {
                    if (HorizontalDistanceSquared(candidate.Position, other.Position) <=
                        clusterRadiusSqr)
                    {
                        nearbySlots++;
                    }
                }

                var score =
                    Mathf.Sqrt(HorizontalDistanceSquared(candidate.Position, idealPosition)) *
                    8f;
                score -= Mathf.Min(nearbySlots, 8) * 55f;
                if (!candidate.HasAuthoredPose)
                    score += 100f;

                foreach (var existing in existingAnchors)
                {
                    var separation = Mathf.Sqrt(
                        HorizontalDistanceSquared(candidate.Position, existing));
                    if (separation < DefensiveCoverAnchorSpacingMeters)
                    {
                        score +=
                            (DefensiveCoverAnchorSpacingMeters - separation) * 30f;
                    }
                }

                if (score < bestScore - 0.01f ||
                    (Mathf.Abs(score - bestScore) <= 0.01f &&
                     (!best.HasValue || candidate.Id.ToInt64() < best.Value.Id.ToInt64())))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (!best.HasValue)
                return false;

            anchor = best.Value.Position;
            return true;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool NeedsOrder(Squad squad, OrderStamp proposed)
    {
        var squadId = SquadIdentity.GetSquadId(squad);
        if (!LastOrders.TryGetValue(squadId, out var existing) ||
            existing.ObjectiveId != proposed.ObjectiveId ||
            existing.Order != proposed.Order ||
            HorizontalDistanceSquared(existing.Destination, proposed.Destination) > 1f ||
            Mathf.Abs(existing.Radius - proposed.Radius) > 0.5f)
        {
            return true;
        }

        var expectedOrder = proposed.Order == CoordinatedOrder.Defend
            ? Order.defend
            : Order.attackFromSide;
        return SquadOrderContinuityCore.ShouldReissue(
            planStampMatches: true,
            nativeOrderMatches: squad.order == expectedOrder);
    }

    private static void TracePlanChange(
        string faction,
        bool attacking,
        int squadCount,
        int objectiveCount,
        int flankCount,
        Dictionary<int, List<SquadInfo>> assignments)
    {
        unchecked
        {
            var signature = squadCount * 397 ^ objectiveCount;
            foreach (var pair in assignments.OrderBy(pair => pair.Key))
                signature = signature * 31 + pair.Key * 17 + pair.Value.Count;

            if (LastPlanSignatures.TryGetValue(faction, out var previous) && previous == signature)
                return;

            LastPlanSignatures[faction] = signature;
            AiState.Trace(
                $"Objective plan: {faction} {(attacking ? "attack" : "defense")} " +
                $"squads={squadCount} objectives={objectiveCount} flanks={flankCount}");
        }
    }

    private static Vector3 AveragePosition(List<SquadInfo> squads)
    {
        var total = Vector3.zero;
        foreach (var squad in squads)
            total += squad.Position;
        return total / squads.Count;
    }

    private static Vector3 RotateHorizontal(Vector3 direction, float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var cosine = Mathf.Cos(radians);
        var sine = Mathf.Sin(radians);
        return new Vector3(
            direction.x * cosine - direction.z * sine,
            0f,
            direction.x * sine + direction.z * cosine).normalized;
    }

    private static float StableAngleOffset(int objectiveId)
    {
        unchecked
        {
            var hash = (uint)objectiveId * 2654435761u;
            return (hash % 360u) * Mathf.Deg2Rad;
        }
    }

    private static Vector3 Flatten(Vector3 value) => new(value.x, 0f, value.z);

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        var delta = Flatten(first - second);
        return delta.sqrMagnitude;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private readonly record struct SquadInfo(
        int Id,
        Squad Squad,
        string Faction,
        Vector3 Position);

    private readonly record struct ObjectiveInfo(
        int Id,
        Vector3 Position,
        float Radius,
        bool FriendlySecured,
        bool Pressured);

    private readonly record struct DefensiveCoverCandidate(
        IntPtr Id,
        Vector3 Position,
        bool HasAuthoredPose);

    private readonly record struct OrderStamp(
        int ObjectiveId,
        CoordinatedOrder Order,
        Vector3 Destination,
        float Radius,
        Vector3 NativeDestination);

    private enum CoordinatedOrder
    {
        DirectAttack,
        Flank,
        Defend
    }
}
