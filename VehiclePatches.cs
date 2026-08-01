using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class TankTactics
{
    private const float MaximumAdvancingSteer = 0.65f;
    private const float MinimumTurningThrottle = 0.55f;
    private const float TurningSteerThreshold = 0.45f;

    // Rear-safety probe geometry (checklist: "single cheap probe when a reverse is
    // about to begin"). The cast starts behind the hull's own collider so the tank
    // does not immediately hit itself, then reaches out to ~10m from hull center.
    private const float RearProbeStartOffset = 2.5f;
    private const float RearProbeDistance = 10f;
    private const float RearProbeRadius = 2f;
    private const float RearFriendlySearchDistance = 12f;

    private const float WatchdogRecoverySeconds = 2f;

    // Component checks never change for a live vehicle, so a stale cache entry is
    // harmless; the alternative is a GetComponent/turret lookup per vehicle per
    // frame, which is the actual cost these caches remove.
    private static readonly Dictionary<int, bool> ArmoredVehicleCache = new();
    private static readonly Dictionary<int, bool> RotatingTurretCache = new();

    internal static bool TryGetLocalAiTank(AIVehicle ai, out Vehicle vehicle)
    {
        vehicle = ai.veh;
        return vehicle != null &&
               vehicle.GetComponent<VehicleTank>() != null &&
               vehicle.IsLocalAIDriving() &&
               // A player riding as gunner or commander leaves the AI driving, but the
               // crew squad is the player's squad and the tank answers to them alone.
               !AiOwnership.IsInPlayerSquad(vehicle.GetDriver());
    }

    internal static bool IsArmoredVehicle(Vehicle vehicle)
    {
        var id = vehicle.GetInstanceID();
        if (ArmoredVehicleCache.TryGetValue(id, out var isArmored))
            return isArmored;

        isArmored = vehicle.GetComponent<VehicleTank>() != null;
        ArmoredVehicleCache[id] = isArmored;
        return isArmored;
    }

    internal static bool IsArmoredVehicle(Spottable? target)
    {
        var vehicle = target?.TryCast<Vehicle>();
        return vehicle != null && IsArmoredVehicle(vehicle);
    }

    internal static bool HasRotatingTurret(Vehicle vehicle)
    {
        var id = vehicle.GetInstanceID();
        if (RotatingTurretCache.TryGetValue(id, out var hasTurret))
            return hasTurret;

        var turret = vehicle.GetMainTurret(false);
        hasTurret = turret != null && turret.IsRotatingTurret;
        RotatingTurretCache[id] = hasTurret;
        return hasTurret;
    }

    internal static bool TryGetVisibleArmoredThreat(
        AIVehicle ai,
        out Vehicle vehicle,
        out float distance,
        out float signedHullAngle)
    {
        vehicle = ai.veh;
        var target = vehicle?.CurrentVisibleTarget;
        distance = float.MaxValue;
        signedHullAngle = 0f;

        if (vehicle == null || vehicle.GetComponent<VehicleTank>() == null ||
            !vehicle.IsLocalAIDriving() || !ai.hasEnemy || target == null ||
            !IsArmoredVehicle(target))
            return false;

        var towardEnemy = target.GetCenterOfUnit() - vehicle.GetCenterOfUnit();
        towardEnemy.y = 0f;
        if (towardEnemy.sqrMagnitude < 0.01f)
            return false;

        distance = towardEnemy.magnitude;
        signedHullAngle = Vector3.SignedAngle(vehicle.transform.forward, towardEnemy, Vector3.up);
        return true;
    }

    internal static bool HullFacesThreat(float signedHullAngle)
        => Mathf.Abs(signedHullAngle) <= Settings.TankMaximumHullFacingAngle.Value;

    internal static bool HasForwardAttackOrder(Vehicle vehicle)
    {
        try
        {
            var squad = vehicle.GetDriver()?.joinedSquad;
            return squad != null &&
                   (squad.order == Order.attackFromSide || squad.order == Order.charge);
        }
        catch
        {
            return false;
        }
    }

    internal static void BeginStraightReverse(AIVehicle ai, Vehicle vehicle)
    {
        var id = vehicle.GetInstanceID();
        ai.retroBehaviour = AIVehicle.RetroBehaviour.backward;
        if (!ai.going_in_retro)
            ai.ForceRetroFor(Settings.TankReverseSeconds.Value, AIVehicle.RetroBehaviour.backward);

        AiState.Trace($"Tank tactics: vehicle {id} retreating straight backward");
    }

    internal static void StopWithoutHullTurn(Vehicle vehicle)
    {
        // Brake() does not clear Vehicle.movingDir. Explicitly neutralize the
        // previous drive command so a stale steering value cannot keep applying
        // differential track torque while the tank is meant to hold position.
        vehicle.Move(Vector2.zero);
        vehicle.Brake();
    }

    internal static void PreserveForwardMotionWhileTurning(AIVehicle ai, Vehicle vehicle)
    {
        if (ai.going_in_retro)
            return;

        var drive = vehicle.movingDir;
        var steeringMagnitude = Mathf.Abs(drive.x);

        // Negative throttle is the native close-node backing maneuver. Keep it
        // intact; this guard is only for forward navigation degenerating into a
        // stationary track pivot.
        if (drive.y < 0f || steeringMagnitude < TurningSteerThreshold)
            return;

        if (steeringMagnitude <= MaximumAdvancingSteer && drive.y >= MinimumTurningThrottle)
            return;

        var correctedDrive = new Vector2(
            Mathf.Clamp(drive.x, -MaximumAdvancingSteer, MaximumAdvancingSteer),
            Mathf.Max(drive.y, MinimumTurningThrottle));

        vehicle.Move(correctedDrive);
        ai.lastDriveThrottle = correctedDrive.y;
    }

    internal static float FlattenedDistance(Vector3 a, Vector3 b)
    {
        var offset = b - a;
        offset.y = 0f;
        return offset.magnitude;
    }

    /// <summary>
    /// A single cheap probe evaluated only when a reverse is about to begin:
    /// a short sphere-cast behind the hull, plus a scan of the tank's own crew
    /// squad for a dismounted friendly standing in the reverse lane. Never a
    /// global soldier scan.
    /// </summary>
    internal static bool IsRearBlocked(AIVehicle ai, Vehicle vehicle)
    {
        var hullCenter = vehicle.GetCenterOfUnit();
        var backward = -vehicle.transform.forward;
        var origin = hullCenter + backward * RearProbeStartOffset;
        if (Physics.SphereCast(
                origin,
                RearProbeRadius,
                backward,
                out _,
                RearProbeDistance - RearProbeStartOffset,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        var squad = vehicle.GetDriver()?.joinedSquad;
        if (squad == null)
            return false;

        var count = squad.CountMembers;
        for (var index = 0; index < count; index++)
        {
            var member = squad.GetMember(index);
            if (member == null || !member.IsAlive || member.IsOnVehicle())
                continue;

            var offset = member.transform.position - hullCenter;
            offset.y = 0f;
            if (offset.sqrMagnitude > RearFriendlySearchDistance * RearFriendlySearchDistance)
                continue;

            if (Vector3.Dot(offset, backward) > 0f)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Evaluates the persistent engagement state machine for a vehicle in (or
    /// recently in) armored contact and acts only on transitions: brake once on
    /// entering Hold, force straight reverse once on entering Reverse. This is
    /// what stops the previous per-frame brake fighting per-frame path
    /// resumption (the reported yo-yo around the reverse line).
    /// </summary>
    internal static void UpdateArmoredEngagement(
        AIVehicle ai,
        Vehicle vehicle,
        Vehicle? visibleTarget,
        TankEngagementRuntimeState runtime,
        int id,
        float now)
    {
        var visibleNow = false;
        var distance = runtime.LastKnownDistance;
        var hullFacesThreat = runtime.LastKnownHullFacesThreat;

        if (visibleTarget != null)
        {
            var towardEnemy = visibleTarget.GetCenterOfUnit() - vehicle.GetCenterOfUnit();
            towardEnemy.y = 0f;
            if (towardEnemy.sqrMagnitude >= 0.01f)
            {
                distance = towardEnemy.magnitude;
                var signedAngle = Vector3.SignedAngle(vehicle.transform.forward, towardEnemy, Vector3.up);
                hullFacesThreat = HullFacesThreat(signedAngle);
                runtime.LastArmoredTargetSeenAt = now;
                runtime.LastKnownDistance = distance;
                runtime.LastKnownHullFacesThreat = hullFacesThreat;
                visibleNow = true;
            }
        }

        var timeSinceVisible = visibleNow ? 0f : now - runtime.LastArmoredTargetSeenAt;
        var previousState = runtime.State;
        var hasArmoredTarget = visibleNow || previousState != TankEngagementState.Follow;
        if (!hasArmoredTarget)
        {
            runtime.State = TankEngagementState.Follow;
            return;
        }

        var lifeFraction = vehicle.Maxlife > 0
            ? Mathf.Clamp01((float)vehicle.life / vehicle.Maxlife)
            : 1f;

        // The rear probe only runs right at the point a reverse is being
        // considered: entering Reverse from Hold, or re-arming a lapsed reverse
        // window while already in Reverse. It is never a per-Hold-frame cost.
        var retroLapsed = !ai.going_in_retro;
        var wouldConsiderReverse =
            (previousState == TankEngagementState.Hold &&
                hullFacesThreat &&
                (distance <= Settings.TankReverseDistance.Value ||
                 lifeFraction <= Settings.TankDamagedThreshold.Value)) ||
            (previousState == TankEngagementState.Reverse && retroLapsed);
        var rearBlocked = wouldConsiderReverse && IsRearBlocked(ai, vehicle);

        var input = new TankEngagementInput(
            hasArmoredTarget,
            distance,
            timeSinceVisible,
            lifeFraction,
            hullFacesThreat,
            ai.ForcedRetroAvailable,
            rearBlocked,
            !ai.going_in_retro,
            Settings.TankStandoffDistance.Value,
            Settings.TankReverseDistance.Value,
            Settings.TankDamagedThreshold.Value);

        var nextState = TankEngagementDecisionCore.NextState(previousState, input);
        runtime.State = nextState;

        if (nextState != previousState)
            runtime.ResetWatchdog();

        if (nextState == TankEngagementState.Reverse)
        {
            if (previousState != TankEngagementState.Reverse)
            {
                BeginStraightReverse(ai, vehicle);
                AiState.Trace(
                    $"Tank tactics: vehicle {id} reversing; enemy={distance:0}m, life={lifeFraction:P0}");
            }
            else
            {
                // Force any active tactical retreat to remain a straight reverse,
                // correcting a curved retro mode the native AI may have selected.
                ai.retroBehaviour = AIVehicle.RetroBehaviour.backward;

                // The native forced-retro window (TankReverseSeconds) can lapse
                // while the FSM still wants to reverse (enemy holding inside the
                // reverse band). Re-arm it so a Reverse state always means the tank
                // is actually backing up rather than coasting to a stationary halt
                // that neither moves nor brakes. The rear was re-probed this same
                // evaluation (see wouldConsiderReverse), so this is not a blind
                // re-entry; it is a bounded, rear-checked continuation.
                if (retroLapsed && ai.ForcedRetroAvailable)
                    ai.ForceRetroFor(
                        Settings.TankReverseSeconds.Value, AIVehicle.RetroBehaviour.backward);
            }
        }
        else if (nextState == TankEngagementState.Hold && previousState != TankEngagementState.Hold)
        {
            StopWithoutHullTurn(vehicle);
            AiState.Trace($"Tank tactics: vehicle {id} holding to engage at {distance:0}m");
        }
        else if (nextState == TankEngagementState.Follow && previousState != TankEngagementState.Follow)
        {
            AiState.Trace($"Tank tactics: vehicle {id} resuming path");
        }
    }

    /// <summary>
    /// While following an active native destination with no engagement, sample
    /// progress every few seconds. A tank making no real progress gets one forced
    /// straight reverse to clear the obstruction.
    /// </summary>
    internal static void UpdateStallWatchdog(
        AIVehicle ai,
        Vehicle vehicle,
        TankEngagementRuntimeState runtime,
        int id,
        float now)
    {
        if (!ai.destinationActive || ai.DestinationReached)
        {
            runtime.ResetWatchdog();
            return;
        }

        if (now < runtime.WatchdogNextSampleAt)
            return;
        runtime.WatchdogNextSampleAt = now + TankStallWatchdogCore.SampleIntervalSeconds;

        var position = vehicle.GetCenterOfUnit();
        if (!runtime.WatchdogProgressAnchorSet)
        {
            runtime.WatchdogProgressAnchorPosition = position;
            runtime.WatchdogProgressAnchorSet = true;
        }
        else if (TankStallWatchdogCore.HasNetProgress(
                     FlattenedDistance(runtime.WatchdogProgressAnchorPosition, position)))
        {
            runtime.WatchdogProgressAnchorPosition = position;
            runtime.WatchdogFailedRecoveries = 0;
        }

        if (!runtime.WatchdogWindowActive)
        {
            runtime.WatchdogWindowStartAt = now;
            runtime.WatchdogWindowStartPosition = position;
            runtime.WatchdogWindowActive = true;
            return;
        }

        var secondsSinceWindowStart = now - runtime.WatchdogWindowStartAt;
        if (secondsSinceWindowStart < TankStallWatchdogCore.StallWindowSeconds)
            return;

        var displacement = FlattenedDistance(runtime.WatchdogWindowStartPosition, position);
        runtime.WatchdogWindowStartAt = now;
        runtime.WatchdogWindowStartPosition = position;

        if (!TankStallWatchdogCore.IsStalled(displacement, secondsSinceWindowStart))
            return;

        runtime.WatchdogFailedRecoveries++;
        if (TankStallWatchdogCore.ShouldGiveUp(runtime.WatchdogFailedRecoveries))
        {
            AiState.Trace(
                $"Tank tactics: vehicle {id} watchdog giving up after repeated stalls");
            runtime.WatchdogFailedRecoveries = 0;
            return;
        }

        AiState.Trace($"Tank tactics: vehicle {id} watchdog recovery: forcing reverse to clear a stall");
        if (ai.ForcedRetroAvailable)
            ai.ForceRetroFor(WatchdogRecoverySeconds, AIVehicle.RetroBehaviour.backward);
    }
}

[HarmonyPatch(typeof(AIVehicle), "RotateVehicleTowardEnemy")]
internal static class AiTankPivotProtectionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(AIVehicle __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            if (!Settings.TankTacticsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
                return true;

            try
            {
                if (!TankTactics.TryGetLocalAiTank(__instance, out var vehicle))
                    return true;

                if (!TankTactics.HasRotatingTurret(vehicle))
                {
                    // Hull rotation is this vehicle's only means of aiming; never
                    // withhold it from the native routine.
                    return true;
                }

                var id = vehicle.GetInstanceID();
                var runtime = AiState.GetTankEngagementState(id);
                var state = runtime.State;

                if (state == TankEngagementState.Reverse)
                {
                    // Never let the native enemy-facing routine steer the hull while a
                    // tactical retreat is active. The turret remains free to track.
                    __instance.retroBehaviour = AIVehicle.RetroBehaviour.backward;
                    return false;
                }

                var hasVisibleArmoredTarget = TankTactics.TryGetVisibleArmoredThreat(
                    __instance, out _, out _, out var signedHullAngle);
                var hullFacesTarget = hasVisibleArmoredTarget &&
                                      TankTactics.HullFacesThreat(signedHullAngle);

                if (TankEngagementDecisionCore.ShouldOrientHull(
                        hasVisibleArmoredTarget, hullFacesTarget))
                {
                    // The base routine supplies the actual track steering. It owns a
                    // stationary hull turn only while the tank it is currently seeing
                    // remains outside the configured frontal arc.
                    return true;
                }

                if (hasVisibleArmoredTarget || state == TankEngagementState.Hold)
                {
                    // The spotted tank is already inside the frontal arc, or a recent
                    // armored contact is still inside the FSM's short hold grace.
                    // Clear steering instead of letting target tracking keep pivoting.
                    TankTactics.StopWithoutHullTurn(vehicle);
                    return false;
                }

                // Follow: no currently visible armored target owns a stationary hull
                // turn. Infantry tracking stays with the turret.
                if (__instance.destinationActive && !__instance.DestinationReached)
                {
                    // stopToShoot routes tanks here even with a valid path. Continue
                    // that path and leave target tracking to the turret instead of
                    // exchanging the movement order for a stationary hull pivot.
                    __instance.MoveTowardCurrentNodeTank();
                    return false;
                }

                // No movement order exists. Clear both throttle and steering before
                // braking; Brake() alone leaves the last steering input latched.
                TankTactics.StopWithoutHullTurn(vehicle);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Tank pivot protection failed: {ex.Message}");
                return true;
            }
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.VehicleAi, __t);
        }
    }
}

[HarmonyPatch(typeof(AIVehicle), "MoveTowardCurrentNodeTank")]
internal static class AiTankForwardTurnCommitmentPatch
{
    [HarmonyPostfix]
    private static void Postfix(AIVehicle __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            if (!Settings.TankTacticsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
                return;

            try
            {
                if (!TankTactics.TryGetLocalAiTank(__instance, out var vehicle))
                    return;

                // Native path following can request almost pure steering at low
                // throttle. A tracked vehicle then spins indefinitely without making
                // progress toward the node. Bound steering and commit enough forward
                // throttle to turn as an arc instead.
                TankTactics.PreserveForwardMotionWhileTurning(__instance, vehicle);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Tank forward-turn commitment failed: {ex.Message}");
            }
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.VehicleAi, __t);
        }
    }
}

[HarmonyPatch(typeof(AIVehicle), "Update")]
internal static class AiVehicleUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(AIVehicle __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            if (!Settings.TankTacticsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
                return;

            try
            {
                var vehicle = __instance.veh;
                if (vehicle == null || !SoldierSequentialUpdatePatch.IsTankCached(vehicle) ||
                    !vehicle.IsLocalAIDriving())
                {
                    return;
                }

                var id = vehicle.GetInstanceID();
                var runtime = AiState.GetTankEngagementState(id);
                var now = Time.time;
                var target = __instance.hasEnemy ? vehicle.CurrentVisibleTarget : null;
                var armoredVehicleTarget = target?.TryCast<Vehicle>();
                var armoredTarget = armoredVehicleTarget != null && TankTactics.IsArmoredVehicle(armoredVehicleTarget);

                if (armoredTarget || runtime.State != TankEngagementState.Follow)
                {
                    TankTactics.UpdateArmoredEngagement(
                        __instance, vehicle, armoredTarget ? armoredVehicleTarget : null, runtime, id, now);
                }
                else
                {
                    runtime.State = TankEngagementState.Follow;
                    if (target != null)
                        HandleInfantryContact(__instance, vehicle, target);
                }

                if (runtime.State == TankEngagementState.Follow)
                    TankTactics.UpdateStallWatchdog(__instance, vehicle, runtime, id, now);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Tank tactical update failed: {ex.Message}");
            }
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.VehicleAi, __t);
        }
    }

    private static void HandleInfantryContact(AIVehicle ai, Vehicle vehicle, Spottable target)
    {
        var distance = TankTactics.FlattenedDistance(vehicle.GetCenterOfUnit(), target.GetCenterOfUnit());
        if (distance > Settings.TankInfantryHoldDistance.Value)
            return;

        // Infantry contact must not become a permanent movement veto for a tank
        // with a live commander order. Only a tank with no active destination
        // (already parked) halts to fight; one en route keeps driving.
        if (ai.destinationActive && !ai.DestinationReached)
            return;

        // The turret may continue tracking infantry while defending or
        // uncommitted armor exploits its current firing position, and an
        // assault-committed tank keeps moving instead of braking every frame.
        if (TankTactics.HasForwardAttackOrder(vehicle))
            return;

        TankTactics.StopWithoutHullTurn(vehicle);
    }
}

[HarmonyPatch(typeof(VehicleTank), "FixedUpdate")]
internal static class TankAccelerationPatch
{
    [HarmonyPrefix]
    private static void Prefix(VehicleTank __instance, out float __state)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            __state = __instance.accelerationSpeed;
            __instance.accelerationSpeed = __state * Settings.TankAccelerationMultiplier.Value;
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.VehicleAi, __t);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(VehicleTank __instance, float __state)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            __instance.accelerationSpeed = __state;
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.VehicleAi, __t);
        }
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(VehicleTank __instance, Exception? __exception, float __state)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            // Keep the prefab's native value intact even when FixedUpdate throws so
            // live slider edits never compound across physics frames.
            __instance.accelerationSpeed = __state;
            return __exception;
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.VehicleAi, __t);
        }
    }
}
