using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class CombatSafety
{
    internal static bool FriendlyInFiringLane(
        Soldier shooter,
        Vector3 origin,
        Vector3 direction,
        float laneRadius,
        float fallbackDistance)
    {
        if (shooter == null || direction.sqrMagnitude < 0.001f)
            return false;

        try
        {
            return FriendlyInFiringLaneCore(
                shooter, origin, direction, laneRadius, fallbackDistance);
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            // Vehicle targets and occupants can remain in the game's live lists
            // briefly while their native backing objects are being dismantled.
            return false;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool FriendlyInFiringLaneCore(
        Soldier shooter,
        Vector3 origin,
        Vector3 direction,
        float laneRadius,
        float fallbackDistance)
    {

        direction.Normalize();
        var target = shooter.GetCurrentBestVisibleEnemy();
        var targetDistance = fallbackDistance;
        if (target != null && TryGetCenterOfUnit(target, out var targetCenter))
        {
            var projected = Vector3.Dot(targetCenter - origin, direction);
            if (projected > 0f)
                targetDistance = Mathf.Min(fallbackDistance, projected + 2f);
        }

        var creatures = Creature.aliveCreatures;
        if (creatures == null)
            return false;

        var shootersVehicle = shooter.GetCurrentVehicle();
        var radiusSqr = laneRadius * laneRadius;
        foreach (var creature in creatures)
        {
            var friendly = creature as Soldier;
            if (friendly == null || friendly == shooter || !friendly.IsAlive ||
                !SameFaction(friendly.faction, shooter.faction))
            {
                continue;
            }

            // A turret muzzle can be ahead of passengers in the same vehicle; those
            // passengers are not actually occupying the external firing lane.
            if (shootersVehicle != null && friendly.GetCurrentVehicle() == shootersVehicle)
                continue;

            if (!TryGetCenterOfUnit(friendly, out var friendlyCenter))
                continue;

            var offset = friendlyCenter - origin;
            var along = Vector3.Dot(offset, direction);
            if (along <= 0.35f || along >= targetDistance)
                continue;

            var lateral = offset - direction * along;
            if (lateral.sqrMagnitude <= radiusSqr)
                return true;
        }

        return false;
    }

    internal static bool FriendlyNear(Vector3 position, string faction, float radius, Soldier? ignore = null)
    {
        if (string.IsNullOrEmpty(faction))
            return false;

        var creatures = Creature.aliveCreatures;
        if (creatures == null)
            return false;

        var radiusSqr = radius * radius;
        foreach (var creature in creatures)
        {
            var friendly = creature as Soldier;
            if (friendly == null || friendly == ignore || !friendly.IsAlive ||
                !SameFaction(friendly.faction, faction))
            {
                continue;
            }

            if (TryGetCenterOfUnit(friendly, out var friendlyCenter) &&
                (friendlyCenter - position).sqrMagnitude <= radiusSqr)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCenterOfUnit(Spottable unit, out Vector3 center)
    {
        try
        {
            center = unit.GetCenterOfUnit();
            return true;
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            center = default;
            return false;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            center = default;
            return false;
        }
    }

    private static bool TryGetCenterOfUnit(Soldier unit, out Vector3 center)
    {
        try
        {
            center = unit.GetCenterOfUnit();
            return true;
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            center = default;
            return false;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            center = default;
            return false;
        }
    }

    internal static bool SameFaction(string? a, string? b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
           string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

[HarmonyPatch(typeof(GenericGun), nameof(GenericGun.Fire))]
internal static class HandheldFriendlyFirePatch
{
    [HarmonyPrefix]
    private static bool Prefix(GenericGun __instance, Creature user, ref bool __result)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
        {
            return true;
        }

        var shooter = user as Soldier;
        if (shooter == null || !shooter.IsAI() || shooter.IsFPSPlayer())
            return true;

        if (InfantryAntiArmorFireDiscipline.ShouldWithhold(shooter, __instance))
        {
            __result = false;
            return false;
        }

        var origin = __instance.GetBulletGenerationPosition();
        if (ContactResponse.TryPreventBlockedCoverShot(
                shooter, origin, shooter.GetFireDir()))
        {
            __result = false;
            return false;
        }

        if (!Settings.FriendlyFireChecksEnabled.Value)
            return true;

        if (!CombatSafety.FriendlyInFiringLane(
                shooter, origin, shooter.GetFireDir(),
                Settings.FriendlyFireLaneRadius.Value, 350f))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(TurretGun), nameof(TurretGun.Shoot))]
internal static class MountedFriendlyFirePatch
{
    [HarmonyPrefix]
    private static bool Prefix(TurretGun __instance, Creature user)
    {
        if (!Settings.FriendlyFireChecksEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return true;
        }

        var shooter = user as Soldier;
        if (shooter == null || !shooter.IsAI() || shooter.IsFPSPlayer())
            return true;

        var isAircraft = shooter.GetCurrentVehicle() is VehiclePlane;
        var maxDistance = isAircraft ? 1200f : 700f;
        if (!CombatSafety.FriendlyInFiringLane(
                shooter, __instance.GetFirePos(), __instance.GetFireDir(),
                Settings.MountedFriendlyFireLaneRadius.Value, maxDistance))
        {
            return true;
        }

        __instance.StopFire();
        return false;
    }
}

[HarmonyPatch(typeof(TurretGun), nameof(TurretGun.StartFire))]
internal static class InvalidTurretFireStatePatch
{
    private static bool _loggedFailure;

    [HarmonyFinalizer]
    private static Exception? Finalizer(TurretGun __instance, Exception? __exception)
    {
        if (__exception == null)
            return null;

        if (__exception is not NullReferenceException &&
            !__exception.Message.Contains("NullReferenceException", StringComparison.Ordinal))
        {
            return __exception;
        }

        try
        {
            __instance?.StopFire();
        }
        catch
        {
            // A partially destroyed turret may also reject StopFire. The original
            // null reference is still safe to suppress for this one fire request.
        }

        if (!_loggedFailure)
        {
            _loggedFailure = true;
            Plugin.LogSource.LogWarning(
                "Prevented a turret with incomplete native state from starting fire; further repeats will be suppressed.");
        }

        return null;
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Throw))]
internal static class SafeAiGrenadeThrowPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance, VirtualItem item)
    {
        if (!Settings.SafeAiGrenadeThrowsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            __instance == null || !__instance.IsAI() || __instance.IsFPSPlayer())
        {
            return true;
        }

        // Smoke is support rather than a fragmentation hazard and keeps its native
        // behavior. AT and ordinary/phosphorus grenades use the safety gate.
        if (item is VirtualSmokeGrenade)
            return true;
        if (item is not VirtualGrenade && item is not VirtualATGrenade)
            return true;

        var target = __instance.GetCurrentBestVisibleEnemy();
        if (target == null)
            return false;

        var isProne = __instance.m_pose == SoldierPose.Prone;
        var squad = __instance.joinedSquad;
        var assaulting = squad != null &&
                         (squad.order == Order.charge || squad.order == Order.attackFromSide);
        var moving = __instance.IsMoving();
        if (moving && (!assaulting || isProne))
        {
            // Defenders and prone crawlers wait until stationary. An assaulting
            // soldier instead makes a short crouched throwing halt; the native
            // squad order remains intact and resumes after the throw animation.
            return false;
        }

        var now = Time.time;
        var id = __instance.GetInstanceID();
        if (!AiState.CooldownReady(AiState.NextGrenadeThrow, id, now))
            return false;

        var targetPosition = target.GetCenterOfUnit();
        var distance = Vector3.Distance(__instance.GetCenterOfUnit(), targetPosition);
        if (distance < Settings.GrenadeMinimumRange.Value ||
            distance > Settings.GrenadeMaximumRange.Value ||
            CombatSafety.FriendlyNear(
                targetPosition, __instance.faction,
                Settings.GrenadeFriendlySafetyRadius.Value, __instance))
        {
            return false;
        }

        // A stationary soldier already has a plausible throwing platform, whether
        // it is native cover, a trench, or open ground. Only an active assault gets
        // permission to halt movement for the throw.
        if (moving)
        {
            __instance.StopMove(SoldierPose.Crouch, Time.deltaTime);
            __instance.SetPose(SoldierPose.Crouch);
        }

        __instance.StopFire();
        AiState.NextGrenadeThrow[id] = now + Settings.GrenadeCooldownSeconds.Value;
        var throwContext = moving
            ? "assault halt"
            : isProne
                ? "prone"
                : __instance.IsOnCover()
                    ? "cover"
                    : "stationary";
        AiState.Trace($"Grenade safety: soldier {id} throwing from {throwContext} at {distance:0}m");
        return true;
    }
}
