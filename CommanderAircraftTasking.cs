using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class CommanderMvp
{
    private static void RefreshAircraftTasks(SideState side)
    {
        foreach (var pair in side.AircraftTasks.ToArray())
        {
            var stamp = pair.Value;
            if (AircraftStillCommanderSafe(stamp.Ai, stamp.Plane))
                continue;

            CancelAircraftTask(side, pair.Key, stamp);
        }
    }

    private static void CancelAircraftForCrew(SideState side, int squadId)
    {
        foreach (var pair in side.AircraftTasks
                     .Where(pair => pair.Value.CrewSquadId == squadId)
                     .ToArray())
        {
            CancelAircraftTask(side, pair.Key, pair.Value);
        }
    }

    private static void CancelAircraftTasks(SideState side)
    {
        foreach (var pair in side.AircraftTasks.ToArray())
            CancelAircraftTask(side, pair.Key, pair.Value);
    }

    private static void CancelAircraftTask(
        SideState side,
        int aircraftId,
        AircraftTaskStamp stamp)
    {
        side.AircraftTasks.Remove(aircraftId);
        try
        {
            if (stamp.Ai != null && stamp.Plane != null)
            {
                GroundAiDirector.ExecuteCommanderAircraftOrder(
                    stamp.Ai, stamp.Plane, stamp.Ai.CancelAttackingTarget);
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // The aircraft already despawned.
        }
        GroundAiDirector.ReleaseCommanderAircraft(aircraftId);
    }

    internal static bool CommanderAircraftUnsafeImpact(
        VehiclePlane? plane,
        Vector3 impact,
        float radius)
    {
        if (plane == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || !IsFinite(impact))
        {
            return false;
        }

        try
        {
            var aircraftId = plane.GetInstanceID();
            var side = Invaders.AircraftTasks.ContainsKey(aircraftId)
                ? Invaders
                : Defenders.AircraftTasks.ContainsKey(aircraftId)
                    ? Defenders
                    : null;
            var battle = BattleManager.GetCurrentBattleData();
            return side != null && battle != null &&
                   FriendlyNearBattleSide(impact, battle, side.InvaderSide, radius);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return true;
        }
    }

    private static void TaskAircraft(
        BattleData battle,
        SideState side,
        IReadOnlyList<AircraftInfo> aircraft,
        IReadOnlyList<ContactReport> reports,
        Vector3 objectivePosition,
        float objectiveRadius,
        float now,
        Dictionary<int, Soldier> soldiersById,
        Dictionary<int, Vehicle> vehiclesById)
    {
        var liveAircraftIds = new HashSet<int>(aircraft.Select(entry => entry.Id));
        foreach (var stale in side.AircraftTasks.Keys
                     .Where(id => !liveAircraftIds.Contains(id))
                     .ToArray())
        {
            CancelAircraftTask(side, stale, side.AircraftTasks[stale]);
        }

        var usedTargets = new HashSet<int>();
        foreach (var plane in aircraft.OrderBy(entry => entry.Id))
        {
            if (side.AircraftTasks.TryGetValue(plane.Id, out var existing))
            {
                if (!AircraftStillCommanderSafe(plane.Ai, plane.Plane))
                {
                    CancelAircraftTask(side, plane.Id, existing);
                    continue;
                }

                if (!GroundAiDirector.LeaseCommanderAircraft(
                        plane.Plane, existing.Position, now))
                {
                    side.AircraftTasks.Remove(plane.Id);
                    continue;
                }

                if (now - existing.AssignedAt < AircraftRetaskSeconds ||
                    !SafeIdleAircraft(plane.Ai, plane.Plane))
                {
                    usedTargets.Add(existing.TargetId);
                    continue;
                }

                side.AircraftTasks.Remove(plane.Id);
            }

            if (!SafeIdleAircraft(plane.Ai, plane.Plane))
                continue;

            ContactReport? selectedReport = null;
            Spottable? selectedTarget = null;
            var selectedPosition = Vector3.zero;
            var bestScore = float.MinValue;
            foreach (var report in reports)
            {
                if (usedTargets.Contains(report.TargetId) ||
                    !AircraftReportEligible(report, now) ||
                    !AircraftReportRelevantToObjective(
                        report, objectivePosition, objectiveRadius) ||
                    !TryResolveReportTarget(
                        report, battle, side.InvaderSide, soldiersById, vehiclesById,
                        out var target, out var targetPosition))
                {
                    continue;
                }

                if (report.Kind != ContactKind.Aircraft)
                {
                    var clearance = plane.HasBombs
                        ? Settings.AircraftBombFriendlyRadius.Value
                        : Settings.AircraftFriendlyAttackRadius.Value;
                    if (FriendlyNearBattleSide(
                            targetPosition, battle, side.InvaderSide, clearance))
                    {
                        continue;
                    }
                }

                var score = AircraftReportScore(plane, report, targetPosition, now);
                if (score <= bestScore)
                    continue;
                selectedReport = report;
                selectedTarget = target;
                selectedPosition = targetPosition;
                bestScore = score;
            }

            if (selectedReport == null || selectedTarget == null)
                continue;

            if (!GroundAiDirector.LeaseCommanderAircraft(plane.Plane, selectedPosition, now))
                continue;

            try
            {
                if (!GroundAiDirector.ExecuteCommanderAircraftOrder(
                        plane.Ai, plane.Plane, () => plane.Ai.DoAttackTarget(selectedTarget)))
                {
                    continue;
                }
                if (plane.Ai.targetToAttack == null)
                {
                    GroundAiDirector.ReleaseCommanderAircraft(plane.Id);
                    continue;
                }

                var crew = plane.Ai.squadInside ?? plane.Plane.GetSquadInside();
                var crewSquadId = crew != null ? ContactKnowledge.GetSquadId(crew) : 0;
                side.AircraftTasks[plane.Id] = new AircraftTaskStamp(
                    selectedReport.TargetId, selectedPosition, now,
                    plane.Ai, plane.Plane, crewSquadId);
                usedTargets.Add(selectedReport.TargetId);
                AiState.Trace($"Commander {side.Name}: aircraft {plane.Id} tasked from fresh " +
                              $"{selectedReport.Kind.ToString().ToLowerInvariant()} report");
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // The report target despawned between resolution and tasking.
                GroundAiDirector.ReleaseCommanderAircraft(plane.Id);
            }
        }
    }

    private static bool AircraftReportRelevantToObjective(
        ContactReport report,
        Vector3 objectivePosition,
        float objectiveRadius)
    {
        // Interceptors may meet aircraft away from the ground operation. Bombing
        // and strafing targets must remain part of the current objective fight.
        return report.Kind == ContactKind.Aircraft ||
               IsFinite(report.LastKnownPosition) &&
               HorizontalDistance(report.LastKnownPosition, objectivePosition) <=
               Mathf.Max(10f, objectiveRadius) + 160f;
    }

    private static bool SafeIdleAircraft(AIPlane ai, VehiclePlane plane)
    {
        if (!AircraftStillCommanderSafe(ai, plane))
            return false;

        try
        {
            return ai.planeState == AIPlane.PlaneAIState.flyingAroundArea &&
                   ai.targetToAttack == null && !ai.hasEnemy && !ai.hasEnemyTank;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool AircraftStillCommanderSafe(AIPlane ai, VehiclePlane plane)
        => Settings.CommanderEnabled.Value && AircraftControlSafe(ai, plane);

    private static bool AircraftHasCommanderOrder(VehiclePlane plane)
    {
        try
        {
            var aircraftId = plane.GetInstanceID();
            return Invaders.AircraftTasks.ContainsKey(aircraftId) ||
                   Defenders.AircraftTasks.ContainsKey(aircraftId);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // Never take autonomous control when commander ownership is uncertain.
            return true;
        }
    }

    private static bool AircraftControlSafe(AIPlane ai, VehiclePlane plane)
    {
        try
        {
            if (ai == null || plane == null || ai.veh == null ||
                !MultiplayerAuthority.CanMutateGameplay() ||
                !ai.HasAIDriver || !ai.isPlaneActive || !plane.IsActive() ||
                !plane.IsOperative() || !plane.CanFight() || plane.life <= 0 ||
                plane.IsDisabled() || !plane.IsLocalAIDriving() || plane.PlayerIsInside() ||
                plane.PlayerIsDriving() || VehicleHasPlayerOccupant(plane) ||
                VehicleHasScriptedOccupant(plane))
            {
                return false;
            }

            var pilot = plane.GetDriver();
            if (pilot == null || !AiOwnership.IsAutonomous(pilot) || !pilot.CanFight())
                return false;
            var sync = pilot.GetComponent<SyncSoldier>();
            if (sync != null && sync.IsControlledByAPlayer())
                return false;

            var crew = ai.squadInside ?? plane.GetSquadInside();
            return crew != null && !HasPlayerMember(crew) && !HasScriptAssignedMember(crew) &&
                   !ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(crew));
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool AircraftReportEligible(ContactReport report, float now)
    {
        if (report.TargetId == 0 || string.IsNullOrEmpty(report.TargetFaction) ||
            !IsFinite(report.LastKnownPosition) ||
            !float.IsFinite(report.Confidence))
        {
            return false;
        }

        var age = now - report.ObservedAt;
        var maximumAge = report.Kind == ContactKind.Aircraft ? 8f : 12f;
        var minimumConfidence = report.Kind switch
        {
            ContactKind.Aircraft => 0.30f,
            ContactKind.GroundVehicle => 0.35f,
            _ => 0.55f
        };
        return float.IsFinite(age) && age >= 0f && age <= maximumAge &&
               report.Confidence >= minimumConfidence;
    }

    private static float AircraftReportScore(
        AircraftInfo plane,
        ContactReport report,
        Vector3 targetPosition,
        float now)
    {
        var typeScore = report.Kind switch
        {
            ContactKind.Aircraft => plane.HasBombs ? 2.6f : 4f,
            ContactKind.GroundVehicle => plane.HasBombs ? 4f : 2.4f,
            _ => plane.HasBombs ? 2.2f : 1.2f
        };
        return typeScore + Mathf.Clamp01(report.Confidence) * 2f -
               Mathf.Max(0f, now - report.ObservedAt) * 0.08f -
               HorizontalDistance(plane.Position, targetPosition) / 3000f;
    }

    /// <summary>
    /// Builds an id-keyed lookup of live soldiers and vehicles once per planning
    /// tick so every report resolves against a dictionary instead of rescanning
    /// Creature.aliveCreatures / Vehicle.allVehicles per report.
    /// </summary>
    private static void BuildReportTargetIndex(
        out Dictionary<int, Soldier> soldiersById,
        out Dictionary<int, Vehicle> vehiclesById)
    {
        soldiersById = new Dictionary<int, Soldier>();
        var alive = Creature.aliveCreatures;
        if (alive != null)
        {
            foreach (var creature in alive)
            {
                try
                {
                    var soldier = creature as Soldier;
                    if (soldier != null)
                        soldiersById[soldier.GetInstanceID()] = soldier;
                }
                catch (Il2CppInterop.Runtime.ObjectCollectedException)
                {
                    // A despawning creature cannot be a valid report target this tick.
                }
            }
        }

        vehiclesById = new Dictionary<int, Vehicle>();
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return;
        foreach (var vehicle in vehicles)
        {
            try
            {
                if (vehicle != null)
                    vehiclesById[vehicle.GetInstanceID()] = vehicle;
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // A despawning vehicle cannot be a valid report target this tick.
            }
        }
    }

    private static bool TryResolveReportTarget(
        ContactReport report,
        BattleData battle,
        bool invaderSide,
        Dictionary<int, Soldier> soldiersById,
        Dictionary<int, Vehicle> vehiclesById,
        out Spottable target,
        out Vector3 targetPosition)
    {
        target = null!;
        targetPosition = Vector3.zero;
        if (string.IsNullOrEmpty(report.TargetFaction) ||
            FactionBelongsToSide(battle, invaderSide, report.TargetFaction))
            return false;
        try
        {
            if (report.Kind == ContactKind.Infantry)
            {
                if (!soldiersById.TryGetValue(report.TargetId, out var soldier) ||
                    !soldier.IsAlive || !soldier.CanFight() ||
                    string.IsNullOrEmpty(soldier.faction) ||
                    !ResourcesManager.IsSameFaction(soldier.faction, report.TargetFaction) ||
                    !FactionBelongsToSide(battle, !invaderSide, soldier.faction))
                {
                    return false;
                }

                var position = soldier.GetCenterOfUnit();
                if (!TargetNearReport(report, position))
                    return false;
                target = soldier.Cast<Spottable>();
                targetPosition = position;
                return true;
            }

            if (!vehiclesById.TryGetValue(report.TargetId, out var vehicle) ||
                !vehicle.IsActive() || !vehicle.IsOperative() || vehicle.life <= 0 ||
                string.IsNullOrEmpty(vehicle.GetVehicleFaction()) ||
                !ResourcesManager.IsSameFaction(vehicle.GetVehicleFaction(), report.TargetFaction) ||
                !FactionBelongsToSide(battle, !invaderSide, vehicle.GetVehicleFaction()))
            {
                return false;
            }

            var isAircraft = vehicle.GetComponent<VehiclePlane>() != null;
            if (isAircraft != (report.Kind == ContactKind.Aircraft))
                return false;
            var vehiclePosition = vehicle.GetCenterOfUnit();
            if (!TargetNearReport(report, vehiclePosition))
                return false;
            target = vehicle.Cast<Spottable>();
            targetPosition = vehiclePosition;
            return true;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool TargetNearReport(ContactReport report, Vector3 currentPosition)
    {
        if (!IsFinite(currentPosition))
            return false;
        var maximumDrift = report.Kind switch
        {
            ContactKind.Aircraft => 650f,
            ContactKind.GroundVehicle => 90f,
            _ => 45f
        };
        return HorizontalDistance(currentPosition, report.LastKnownPosition) <= maximumDrift;
    }
}
