using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class TankTactics
{
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
            var vehicle = __instance.veh;
            if (vehicle == null || vehicle.GetComponent<VehicleTank>() == null ||
                !vehicle.IsLocalAIDriving())
                return true;

            // Never let the native enemy-facing routine steer the hull while a
            // tactical retreat is active. The turret remains free to track.
            if (__instance.going_in_retro)
                return false;

            if (!TankTactics.TryGetCloseArmoredThreat(
                    __instance, out vehicle, out _, out var signedHullAngle) ||
                TankTactics.HullFacesThreat(signedHullAngle))
                return true;

            // The native routine pivots the hull in place. Under direct armored
            // contact that rotates the side/rear through the enemy's firing line.
            TankTactics.BeginStraightReverse(__instance, vehicle);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Tank pivot protection failed: {ex.Message}");
            return true;
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
                vehicle.Brake();
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
                vehicle.Brake();
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
