using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class TankTactics
{
    private const float MaximumAdvancingSteer = 0.65f;
    private const float MinimumTurningThrottle = 0.55f;
    private const float TurningSteerThreshold = 0.45f;

    internal static bool TryGetLocalAiTank(AIVehicle ai, out Vehicle vehicle)
    {
        vehicle = ai.veh;
        return vehicle != null &&
               vehicle.GetComponent<VehicleTank>() != null &&
               vehicle.IsLocalAIDriving();
    }

    internal static bool TryGetCloseArmoredThreat(
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
            !target.IsWheeledVehicleOrTank())
            return false;

        var towardEnemy = target.GetCenterOfUnit() - vehicle.GetCenterOfUnit();
        towardEnemy.y = 0f;
        if (towardEnemy.sqrMagnitude < 0.01f)
            return false;

        distance = towardEnemy.magnitude;
        if (distance > Settings.TankStandoffDistance.Value)
            return false;

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
}

[HarmonyPatch(typeof(AIVehicle), "RotateVehicleTowardEnemy")]
internal static class AiTankPivotProtectionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(AIVehicle __instance)
    {
        if (!Settings.TankTacticsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
            return true;

        try
        {
            if (!TankTactics.TryGetLocalAiTank(__instance, out var vehicle))
                return true;

            // Never let the native enemy-facing routine steer the hull while a
            // tactical retreat is active. The turret remains free to track.
            if (__instance.going_in_retro)
            {
                __instance.retroBehaviour = AIVehicle.RetroBehaviour.backward;
                return false;
            }

            if (TankTactics.TryGetCloseArmoredThreat(
                    __instance, out vehicle, out _, out var signedHullAngle) &&
                !TankTactics.HullFacesThreat(signedHullAngle))
            {
                // Under direct armored contact, open distance without rotating
                // the side or rear through the enemy's firing line.
                TankTactics.BeginStraightReverse(__instance, vehicle);
                return false;
            }

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
}

[HarmonyPatch(typeof(AIVehicle), "MoveTowardCurrentNodeTank")]
internal static class AiTankForwardTurnCommitmentPatch
{
    [HarmonyPostfix]
    private static void Postfix(AIVehicle __instance)
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
}

[HarmonyPatch(typeof(AIVehicle), "Update")]
internal static class AiVehicleUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(AIVehicle __instance)
    {
        if (!Settings.TankTacticsEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
            return;

        try
        {
            var vehicle = __instance.veh;
            if (vehicle == null || vehicle.GetComponent<VehicleTank>() == null || !vehicle.IsLocalAIDriving())
                return;
            if (!__instance.hasEnemy || vehicle.CurrentVisibleTarget == null)
                return;

            var target = vehicle.CurrentVisibleTarget;
            var armoredTarget = target.IsWheeledVehicleOrTank();
            var distance = Vector3.Distance(vehicle.GetCenterOfUnit(), target.GetCenterOfUnit());
            var holdDistance = armoredTarget
                ? Settings.TankStandoffDistance.Value
                : Settings.TankInfantryHoldDistance.Value;
            if (distance > holdDistance)
                return;

            if (!armoredTarget)
            {
                // Infantry contact must not become a permanent movement veto for
                // armor committed to an assault. Without this exception the postfix
                // applied the brakes every frame inside the hold distance, so native
                // fire-and-move logic could never resume the advance.
                if (TankTactics.HasForwardAttackOrder(vehicle))
                    return;

                // Halt without ordering a hull turn. The turret may continue tracking
                // infantry while defending or uncommitted armor exploits its current
                // firing position.
                TankTactics.StopWithoutHullTurn(vehicle);
                return;
            }

            var towardEnemy = target.GetCenterOfUnit() - vehicle.GetCenterOfUnit();
            towardEnemy.y = 0f;
            if (towardEnemy.sqrMagnitude < 0.01f)
                return;
            towardEnemy.Normalize();

            var signedAngle = Vector3.SignedAngle(vehicle.transform.forward, towardEnemy, Vector3.up);
            var hullIsFacingThreat = TankTactics.HullFacesThreat(signedAngle);

            // Force any active tactical retreat to remain a straight reverse. This
            // also corrects a curved retro mode selected earlier by the native AI.
            if (__instance.going_in_retro)
            {
                __instance.retroBehaviour = AIVehicle.RetroBehaviour.backward;
                return;
            }

            if (!hullIsFacingThreat)
            {
                // Do not pivot or steer around under fire. Preserve the current hull
                // orientation and open distance in a straight reverse.
                TankTactics.BeginStraightReverse(__instance, vehicle);
                return;
            }

            var now = Time.time;
            var id = vehicle.GetInstanceID();
            if (!AiState.CooldownReady(AiState.NextTankTactic, id, now))
                return;
            AiState.NextTankTactic[id] = now + 4f;

            var lifeFraction = vehicle.Maxlife > 0
                ? Mathf.Clamp01((float)vehicle.life / vehicle.Maxlife)
                : 1f;
            if ((distance <= Settings.TankReverseDistance.Value ||
                 lifeFraction <= Settings.TankDamagedThreshold.Value) &&
                __instance.ForcedRetroAvailable)
            {
                TankTactics.BeginStraightReverse(__instance, vehicle);
                AiState.Trace(
                    $"Tank tactics: vehicle {id} reversing; enemy={distance:0}m, life={lifeFraction:P0}");
            }
            else
            {
                TankTactics.StopWithoutHullTurn(vehicle);
                AiState.Trace($"Tank tactics: vehicle {id} holding to engage at {distance:0}m");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Tank tactical update failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(VehicleTank), "FixedUpdate")]
internal static class TankAccelerationPatch
{
    [HarmonyPrefix]
    private static void Prefix(VehicleTank __instance, out float __state)
    {
        __state = __instance.accelerationSpeed;
        __instance.accelerationSpeed = __state * Settings.TankAccelerationMultiplier.Value;
    }

    [HarmonyPostfix]
    private static void Postfix(VehicleTank __instance, float __state)
    {
        __instance.accelerationSpeed = __state;
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(VehicleTank __instance, Exception? __exception, float __state)
    {
        // Keep the prefab's native value intact even when FixedUpdate throws so
        // live slider edits never compound across physics frames.
        __instance.accelerationSpeed = __state;
        return __exception;
    }
}
