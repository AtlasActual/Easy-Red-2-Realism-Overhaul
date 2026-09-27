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
            var friendly = Il2CppTypeTests.As<Soldier>(creature);
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
            var friendly = Il2CppTypeTests.As<Soldier>(creature);
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
    private static bool _loggedFailure;

    [HarmonyPrefix]
    private static bool Prefix(GenericGun __instance, Creature user, ref bool __result)
    {
        try
        {
            if (!MultiplayerAuthority.CanMutateGameplay())
            {
                return true;
            }

            var shooter = Il2CppTypeTests.As<Soldier>(user);
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
        catch (Exception ex)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Plugin.LogSource.LogWarning(
                    $"Handheld fire safety check failed open so the native shot can continue: {ex.GetType().Name}: {ex.Message}");
            }

            return true;
        }
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

        var shooter = Il2CppTypeTests.As<Soldier>(user);
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
    private static bool Prefix(Soldier __instance, VirtualItem item, Spottable aimTarget,
        out bool __state)
    {
        __state = false;
        if (!Settings.SafeAiGrenadeThrowsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            !AiOwnership.IsAutonomous(__instance) ||
            AntiTankGrenadeThrows.OwnsThrow(__instance))
        {
            return true;
        }

        // Smoke is support rather than a fragmentation hazard and keeps its native
        // behavior. `item` arrives wrapped as VirtualItem, so its class is asked of
        // the runtime: a C# type test here was false for every grenade ever thrown.
        if (Il2CppTypeTests.Is<VirtualSmokeGrenade>(item))
            return true;

        var id = __instance.GetInstanceID();
        if (Il2CppTypeTests.Is<VirtualATGrenade>(item))
            return AuthorizeNativeAntiTankThrow(__instance, id, aimTarget);
        if (!Il2CppTypeTests.Is<VirtualGrenade>(item))
            return true;

        // Native Throw rejects busy hands/reloads before starting the animation.
        // Do not stop movement or charge a cooldown for those rejected attempts.
        if (__instance.IsThrowing || !__instance.CanPlaySpecialAnim())
            return false;

        var rememberedThrow = RememberedGrenadeThrows.TryAuthorizeOwnedThrow(
            __instance, id, item, out var targetPosition);
        var target = rememberedThrow
            ? null
            : TargetAcquisition.IsUsableTarget(aimTarget)
                ? aimTarget
                : __instance.GetCurrentBestVisibleEnemy();
        if (!rememberedThrow && target == null)
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "Grenade rejected: no visible target");
            return false;
        }

        var isProne = __instance.m_pose == SoldierPose.Prone;
        var squad = __instance.joinedSquad;
        var assaulting = squad != null &&
                         (squad.order == Order.charge || squad.order == Order.attackFromSide);
        var moving = rememberedThrow
            ? __instance.IsMoving(0.2f)
            : __instance.IsMoving();
        if ((rememberedThrow && moving) ||
            !FragGrenadeDecisionCore.CanUsePlatform(moving, assaulting, isProne))
        {
            // Defenders and prone crawlers wait until stationary. An assaulting
            // soldier instead makes a short crouched throwing halt; the native
            // squad order remains intact and resumes after the throw animation.
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "Grenade rejected: unstable moving throw platform");
            return false;
        }

        var now = Time.time;
        if (!AiState.CooldownReady(AiState.NextGrenadeThrow, id, now))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "Grenade rejected: throw cooldown active");
            return false;
        }

        if (!rememberedThrow)
            targetPosition = target!.GetCenterOfUnit();
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
        __state = true;
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

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance, bool __state)
    {
        // Prefix approval alone does not mean the game accepted the inventory
        // item or another mod allowed the call. Only an actual throw spends it.
        if (__state && __instance.IsThrowing)
            AiState.NextGrenadeThrow[__instance.GetInstanceID()] =
                Time.time + AiBehaviorTuning.GrenadeCooldownSeconds;
    }

    /// <summary>
    /// The game's own anti-tank throw has already required a vehicle target between
    /// 6 m and the grenade's ballistic range. The fragmentation rules below would veto
    /// most of that window (a 9 m floor, an 11 m friendly radius sized for fragments,
    /// a cooldown shared with frags), so an AT grenade answers only the two questions
    /// that apply to it: can this man stand up to throw, and is a friendly at the tank.
    /// </summary>
    private static bool AuthorizeNativeAntiTankThrow(Soldier soldier, int id, Spottable aimTarget)
    {
        if (ContactResponse.IsPinned(id))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "AT grenade rejected: pinned");
            return false;
        }

        if (TargetAcquisition.IsUsableTarget(aimTarget) &&
            CombatSafety.FriendlyNear(
                aimTarget.GetCenterOfUnit(), AiState.FactionOf(soldier),
                AntiTankGrenadeDecisionCore.FriendlyClearanceMeters, soldier))
        {
            AiDebugTelemetry.RecordDecision(AiDebugCategory.Danger, id,
                "AT grenade rejected: friendly beside the vehicle");
            return false;
        }

        return true;
    }
}
