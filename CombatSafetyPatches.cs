using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class CombatSafety
{
    private const float FiringLaneCacheSeconds = 0.06f;
    private static readonly Dictionary<int, FiringLaneSample> FiringLaneSamples = new();
    private static float _nextFiringLaneCacheClearAt;

    private readonly record struct FiringLaneSample(
        Vector3 Origin,
        Vector3 Direction,
        float LaneRadius,
        float FallbackDistance,
        float ExpiresAt,
        bool FriendlyPresent);

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
            var now = Time.time;
            if (now >= _nextFiringLaneCacheClearAt)
            {
                FiringLaneSamples.Clear();
                _nextFiringLaneCacheClearAt = now + 10f;
            }

            var normalizedDirection = direction.normalized;
            var shooterId = shooter.GetInstanceID();
            if (FiringLaneSamples.TryGetValue(shooterId, out var cached) &&
                now < cached.ExpiresAt &&
                (cached.Origin - origin).sqrMagnitude <= 0.0625f &&
                Vector3.Dot(cached.Direction, normalizedDirection) >= 0.9995f &&
                Mathf.Approximately(cached.LaneRadius, laneRadius) &&
                Mathf.Approximately(cached.FallbackDistance, fallbackDistance))
            {
                return cached.FriendlyPresent;
            }

            var friendlyPresent = FriendlyInFiringLaneCore(
                shooter, origin, direction, laneRadius, fallbackDistance);
            FiringLaneSamples[shooterId] = new FiringLaneSample(
                origin,
                normalizedDirection,
                laneRadius,
                fallbackDistance,
                now + FiringLaneCacheSeconds,
                friendlyPresent);
            return friendlyPresent;
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

    internal static bool TryGetDebugFiringLane(
        int soldierId,
        out Vector3 origin,
        out Vector3 direction,
        out float radius,
        out float distance,
        out bool friendlyPresent)
    {
        if (FiringLaneSamples.TryGetValue(soldierId, out var sample) &&
            Time.time < sample.ExpiresAt)
        {
            origin = sample.Origin;
            direction = sample.Direction;
            radius = sample.LaneRadius;
            distance = sample.FallbackDistance;
            friendlyPresent = sample.FriendlyPresent;
            return true;
        }

        origin = default;
        direction = default;
        radius = 0f;
        distance = 0f;
        friendlyPresent = false;
        return false;
    }
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
        if (!AiOwnership.IsAutonomous(shooter))
            return true;

        if (InfantryAntiArmorFireDiscipline.ShouldWithhold(shooter, __instance))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Combat, shooter.GetInstanceID(),
                "Fire withheld: ineffective handheld weapon against armored target");
            __result = false;
            return false;
        }

        var origin = __instance.GetBulletGenerationPosition();
        if (ContactResponse.TryPreventBlockedCoverShot(
                shooter, origin, shooter.GetFireDir()))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Combat, shooter.GetInstanceID(),
                "Fire withheld: selected cover blocks muzzle clearance");
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

        AiDebugTelemetry.RecordDecision(AiDebugCategory.Combat, shooter.GetInstanceID(),
            "Fire withheld: friendly occupies handheld firing lane");
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
        if (!AiOwnership.IsAutonomous(shooter))
            return true;

        var isAircraft = shooter.GetCurrentVehicle() is VehiclePlane;
        var maxDistance = isAircraft ? 1200f : 700f;
        if (!CombatSafety.FriendlyInFiringLane(
                shooter, __instance.GetFirePos(), __instance.GetFireDir(),
                Settings.MountedFriendlyFireLaneRadius.Value, maxDistance))
        {
            return true;
        }

        AiDebugTelemetry.RecordDecision(AiDebugCategory.Combat, shooter.GetInstanceID(),
            "Fire withheld: friendly occupies mounted firing lane");
        MountedGunnerSuppression.StopTurretGun(__instance);
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
            MountedGunnerSuppression.StopTurretGun(__instance);
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
            !AiOwnership.IsAutonomous(__instance))
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
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, __instance.GetInstanceID(),
                "Grenade rejected: no visible target");
            return false;
        }

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
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, __instance.GetInstanceID(),
                "Grenade rejected: unstable moving throw platform");
            return false;
        }

        var now = Time.time;
        var id = __instance.GetInstanceID();
        if (!AiState.CooldownReady(AiState.NextGrenadeThrow, id, now))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "Grenade rejected: throw cooldown active");
            return false;
        }

        var targetPosition = target.GetCenterOfUnit();
        var distance = Vector3.Distance(__instance.GetCenterOfUnit(), targetPosition);
        if (distance < Settings.GrenadeMinimumRange.Value ||
            distance > Settings.GrenadeMaximumRange.Value)
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                $"Grenade rejected: target range {distance:0}m outside safe envelope");
            return false;
        }

        if (CombatSafety.FriendlyNear(
                targetPosition, __instance.faction,
                Settings.GrenadeFriendlySafetyRadius.Value, __instance))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "Grenade rejected: friendly inside blast safety radius");
            return false;
        }

        // A stationary soldier already has a plausible throwing platform, whether
        // it is native cover, a trench, or open ground. Only an active assault gets
        // permission to halt movement for the throw.
        GroundAiDirector.ExecuteGrenadeSafetyHalt(__instance, moving);
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
