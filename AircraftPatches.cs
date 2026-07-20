using Corvostudio.Weapons;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class AircraftBombSafety
{
    private const float MaximumTargetMissMeters = 65f;

    internal static bool UnsafeImpact(
        VehiclePlane plane,
        out Vector3 impact,
        out string reason)
    {
        impact = Vector3.zero;
        reason = string.Empty;
        if (!MultiplayerAuthority.CanMutateGameplay() || plane == null)
        {
            return false;
        }

        var pilot = plane.GetDriver();
        if (!AiOwnership.IsAutonomous(pilot))
            return false;

        impact = plane.PredictBombImpactPosition();
        if (impact == Vector3.zero)
        {
            if (!Settings.AircraftSafetyEnabled.Value)
                return false;

            reason = "no valid predicted impact";
            return true;
        }

        if (Settings.AircraftSafetyEnabled.Value &&
            !HasLiveHostileGroundTargetNearImpact(plane, impact))
        {
            reason = "no live hostile ground target near the predicted impact";
            return true;
        }

        var radius = Settings.AircraftBombFriendlyRadius.Value;
        if (CommanderMvp.CommanderAircraftUnsafeImpact(plane, impact, radius))
        {
            reason = "friendlies near the predicted impact";
            return true;
        }

        if (Settings.AircraftSafetyEnabled.Value &&
            CombatSafety.FriendlyNear(impact, plane.GetVehicleFaction(), radius))
        {
            reason = "friendlies near the predicted impact";
            return true;
        }

        return false;
    }

    private static bool HasLiveHostileGroundTargetNearImpact(
        VehiclePlane plane,
        Vector3 impact)
    {
        try
        {
            var ai = plane.GetComponent<AIPlane>();
            var target = ai?.targetToAttack;
            var usableGroundTarget = TargetAcquisition.IsUsableTarget(target) &&
                                     !target!.IsAirVehicle();
            if (!usableGroundTarget)
                return false;

            var soldier = target!.TryCast<Soldier>();
            var vehicle = soldier == null ? target.TryCast<Vehicle>() : null;
            var alive = soldier != null
                ? soldier.IsAlive && soldier.CanFight()
                : vehicle != null && vehicle.life > 0 && vehicle.IsActive() &&
                  vehicle.IsOperative() && vehicle.CanFight();
            var targetFaction = soldier?.faction ??
                                vehicle?.GetVehicleFaction() ??
                                string.Empty;
            var ownFaction = plane.GetVehicleFaction();
            var hostile = !string.IsNullOrWhiteSpace(ownFaction) &&
                          !string.IsNullOrWhiteSpace(targetFaction) &&
                          ResourcesManager.IsEnemyFaction(ownFaction, targetFaction);
            var targetPosition = target.GetCenterOfUnit();
            var deltaX = targetPosition.x - impact.x;
            var deltaZ = targetPosition.z - impact.z;
            var horizontalMiss = Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);

            return AircraftBombDecisionCore.TargetSupportsRelease(
                new AircraftBombReleaseInput(
                    HasValidPredictedImpact: true,
                    usableGroundTarget,
                    alive,
                    hostile,
                    horizontalMiss,
                    MaximumTargetMissMeters));
        }
        catch (Exception)
        {
            // If the target disappears during the release check, fail closed.
            return false;
        }
    }
}

[HarmonyPatch(typeof(AIPlane), "DoAttackTarget")]
internal static class AircraftAttackSafetyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(AIPlane __instance, Spottable target)
    {
        if (!Settings.AircraftSafetyEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            __instance == null || __instance.veh == null || target == null ||
            target.IsAirVehicle())
        {
            return true;
        }

        var pilot = __instance.veh.GetDriver();
        if (!AiOwnership.IsAutonomous(pilot))
            return true;

        var targetPosition = target.GetCenterOfUnit();
        var faction = __instance.veh.GetVehicleFaction();
        if (!CombatSafety.FriendlyNear(
                targetPosition, faction, Settings.AircraftFriendlyAttackRadius.Value))
        {
            return true;
        }

        GroundAiDirector.ExecuteAircraftSafetyCancellation(__instance, __instance.veh);
        AiState.Trace($"Aircraft safety: plane {__instance.veh.GetInstanceID()} rejected an unsafe attack target");
        return false;
    }
}

[HarmonyPatch(typeof(VehiclePlane), nameof(VehiclePlane.DropOneBomb))]
internal static class AircraftBombReleaseSafetyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(VehiclePlane __instance, ref BombBay __result)
    {
        if (!AircraftBombSafety.UnsafeImpact(__instance, out _, out var reason))
            return true;

        __result = null!;
        AiState.Trace($"Aircraft safety: plane {__instance.GetInstanceID()} withheld bomb: {reason}");
        return false;
    }
}

[HarmonyPatch(typeof(BombBay), nameof(BombBay.Drop))]
internal static class BombBayReleaseSafetyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(VehiclePlane v, ref bool __result)
    {
        if (!AircraftBombSafety.UnsafeImpact(v, out _, out var reason))
            return true;

        __result = false;
        AiState.Trace($"Aircraft safety: plane {v.GetInstanceID()} blocked bomb-bay release: {reason}");
        return false;
    }
}

[HarmonyPatch(typeof(AIPlane), "Update")]
internal static class AircraftEvasionControlPatch
{
    private const float SmallArmsNearMissRadiusMeters = 10f;
    private const float HeavyWeaponNearMissRadiusMeters = 18f;
    private const float ProjectileLookbackSeconds = 0.2f;
    private const float ScanIntervalSeconds = 0.1f;
    private const float ThreatWindowSeconds = 1.25f;
    private const float SeenProjectileSeconds = 2f;
    private const int SmallArmsBurstThreshold = 8;

    private sealed class ThreatWindow
    {
        internal float ExpiresAt;
        internal float Amount;
        internal int ShooterId;
    }

    private static readonly Dictionary<int, float> NextThreatScans = new();
    private static readonly Dictionary<int, ThreatWindow> SmallArmsThreats = new();
    private static readonly Dictionary<int, ThreatWindow> DamageThreats = new();
    private static readonly Dictionary<int, Dictionary<long, float>> SeenProjectiles = new();
    private static readonly Dictionary<int, float> LastAircraftLife = new();
    private static readonly Dictionary<int, float> PeakAircraftLife = new();

    [HarmonyPostfix]
    private static void Postfix(AIPlane __instance)
    {
        if (!Settings.AircraftEvasionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            __instance == null || __instance.veh == null || !__instance.HasAIDriver)
        {
            return;
        }

        var id = __instance.veh.GetInstanceID();
        var now = Time.time;
        TryDetectMeaningfulDamage(__instance, id, now);
        TryDetectNearMiss(__instance, id, now);

        if (!AiState.AircraftEvasionUntil.TryGetValue(id, out var until))
            return;

        if (now >= until || !AiState.AircraftEvasionDirection.TryGetValue(id, out var direction))
        {
            AiState.AircraftEvasionUntil.Remove(id);
            AiState.AircraftEvasionDirection.Remove(id);
            return;
        }

        // Applied after the native controller for only a short window. Once it
        // expires the native mission/target state resumes without being replaced.
        __instance.veh.targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        __instance.veh.throttle = 100f;
    }

    private static void TryDetectNearMiss(AIPlane ai, int id, float now)
    {
        if (AiState.AircraftEvasionUntil.TryGetValue(id, out var activeUntil) && now < activeUntil)
            return;
        if (!AiState.CooldownReady(NextThreatScans, id, now))
            return;

        NextThreatScans[id] = now + ScanIntervalSeconds;

        try
        {
            var manager = ProjectilesManager.instance;
            var bullets = manager?.activeBullets;
            if (bullets == null)
                return;

            var aircraftPosition = ai.veh.GetCenterOfUnit();
            var aircraftFaction = ai.veh.GetVehicleFaction();
            var smallArmsNearMissSqr = SmallArmsNearMissRadiusMeters * SmallArmsNearMissRadiusMeters;
            var heavyWeaponNearMissSqr = HeavyWeaponNearMissRadiusMeters * HeavyWeaponNearMissRadiusMeters;

            for (var index = bullets.Count - 1; index >= 0; index--)
            {
                var bullet = bullets[index];
                var shooter = bullet?.shooter;
                if (bullet == null || !bullet.canDamage || bullet.isMortar || shooter == null ||
                    string.Equals(shooter.faction, aircraftFaction, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var end = bullet.curPos;
                var start = bullet.previousPosition;
                var lookback = end - bullet.curVelocity * ProjectileLookbackSeconds;
                if ((lookback - end).sqrMagnitude > (start - end).sqrMagnitude)
                    start = lookback;

                var missDistanceSqr = DistanceToSegmentSquared(aircraftPosition, start, end);
                var isHeavyWeapon = IsHeavyWeaponProjectile(bullet);
                var relevantRadiusSqr = isHeavyWeapon ? heavyWeaponNearMissSqr : smallArmsNearMissSqr;
                if (missDistanceSqr > relevantRadiusSqr || !MarkProjectileSeen(id, bullet, now))
                    continue;

                var shooterId = shooter.GetInstanceID();
                if (isHeavyWeapon)
                {
                    BeginEvasion(ai, id, now, shooterId, "heavy-caliber near-miss");
                    return;
                }

                if (RegisterSmallArmsThreat(ai, id, now, shooterId))
                    return;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Aircraft near-miss scan failed: {ex.Message}");
        }
    }

    private static bool IsHeavyWeaponProjectile(BulletInstance bullet)
    {
        var data = bullet.bulletData;
        return data != null &&
               (data.IsHighCaliber() || data.IsVeryHighCaliber() || data.IsHe() || data.IsFlak());
    }

    private static bool MarkProjectileSeen(int id, BulletInstance bullet, float now)
    {
        if (!SeenProjectiles.TryGetValue(id, out var seen))
        {
            seen = new Dictionary<long, float>();
            SeenProjectiles[id] = seen;
        }

        if (seen.Count > 0)
        {
            var expired = new List<long>();
            foreach (var pair in seen)
            {
                if (pair.Value <= now)
                    expired.Add(pair.Key);
            }

            foreach (var token in expired)
                seen.Remove(token);
        }

        var projectileToken = bullet.Pointer.ToInt64();
        if (projectileToken == 0)
            projectileToken = bullet.GetHashCode();
        if (seen.ContainsKey(projectileToken))
            return false;

        seen[projectileToken] = now + SeenProjectileSeconds;
        return true;
    }

    private static bool RegisterSmallArmsThreat(AIPlane ai, int id, float now, int shooterId)
    {
        if (!SmallArmsThreats.TryGetValue(id, out var threat) || now >= threat.ExpiresAt)
        {
            threat = new ThreatWindow();
            SmallArmsThreats[id] = threat;
        }

        threat.ExpiresAt = now + ThreatWindowSeconds;
        threat.Amount += 1f;
        threat.ShooterId = shooterId;
        if (threat.Amount < SmallArmsBurstThreshold)
            return false;

        SmallArmsThreats.Remove(id);
        BeginEvasion(ai, id, now, shooterId, $"sustained small-arms burst ({SmallArmsBurstThreshold}+ rounds)");
        return true;
    }

    private static void TryDetectMeaningfulDamage(AIPlane ai, int id, float now)
    {
        var life = (float)ai.veh.life;
        if (!LastAircraftLife.TryGetValue(id, out var previousLife))
        {
            LastAircraftLife[id] = life;
            PeakAircraftLife[id] = life;
            return;
        }

        LastAircraftLife[id] = life;
        PeakAircraftLife[id] = Mathf.Max(PeakAircraftLife.GetValueOrDefault(id, life), life);
        var damage = previousLife - life;
        if (damage <= 0f ||
            AiState.AircraftEvasionUntil.TryGetValue(id, out var activeUntil) && now < activeUntil)
        {
            return;
        }

        if (!DamageThreats.TryGetValue(id, out var threat) || now >= threat.ExpiresAt)
        {
            threat = new ThreatWindow();
            DamageThreats[id] = threat;
        }

        threat.ExpiresAt = now + ThreatWindowSeconds;
        threat.Amount += damage;
        var damageThreshold = Mathf.Max(40f, PeakAircraftLife[id] * 0.06f);
        if (threat.Amount < damageThreshold)
            return;

        DamageThreats.Remove(id);
        BeginEvasion(ai, id, now, -1, "meaningful accumulated aircraft damage");
    }

    private static void BeginEvasion(AIPlane ai, int id, float now, int shooterId, string reason)
    {
        var side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        var forwardBias = UnityEngine.Random.Range(0.55f, 1f);
        var direction = ai.veh.transform.forward * forwardBias +
                        ai.veh.transform.right * side +
                        Vector3.up * Settings.AircraftEvasionClimb.Value;
        direction.Normalize();

        AiState.AircraftEvasionDirection[id] = direction;
        AiState.AircraftEvasionUntil[id] = now + Settings.AircraftEvasionSeconds.Value;
        var source = shooterId >= 0 ? $" from {shooterId}" : string.Empty;
        AiState.Trace($"Aircraft evasion: plane {id} breaking after {reason}{source}");
    }

    private static float DistanceToSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = end - start;
        var lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= 0.0001f)
            return (point - end).sqrMagnitude;

        var t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSqr);
        return (point - (start + segment * t)).sqrMagnitude;
    }

    internal static void Remove(int id)
    {
        NextThreatScans.Remove(id);
        SmallArmsThreats.Remove(id);
        DamageThreats.Remove(id);
        SeenProjectiles.Remove(id);
        LastAircraftLife.Remove(id);
        PeakAircraftLife.Remove(id);
    }
}

<<<<<<< Updated upstream
=======
[HarmonyPatch(typeof(AIPlane), "Update")]
internal static class AircraftAutonomousEngagementPatch
{
    private const float OpportunityScanIntervalSeconds = 0.8f;
    private static readonly Dictionary<int, float> NextOpportunityScans = new();

    [HarmonyPostfix]
    private static void Postfix(AIPlane __instance)
    {
        if (__instance == null || __instance.veh == null ||
            !CommanderMvp.AllowsAutonomousAircraftTargeting(__instance, __instance.veh))
        {
            return;
        }

        var id = __instance.veh.GetInstanceID();
        var now = Time.time;
        if (AiState.AircraftEvasionUntil.TryGetValue(id, out var evadingUntil) &&
            now < evadingUntil)
        {
            return;
        }

        try
        {
            // Only supplement the native idle patrol state. Commander attacks,
            // scripted flights, refuelling, takeoff, and existing native attacks
            // retain full authority over the aircraft.
            if (__instance.planeState != AIPlane.PlaneAIState.flyingAroundArea ||
                __instance.targetToAttack != null || __instance.hasEnemy ||
                __instance.hasEnemyTank ||
                !AiState.CooldownReady(NextOpportunityScans, id, now))
            {
                return;
            }

            NextOpportunityScans[id] = now + OpportunityScanIntervalSeconds;
            var target = __instance.GetBestAircraftVisibleEnemy();
            if (target == null)
                return;

            var targetKind = target.IsAirVehicle() ? "aircraft" : "ground";
            if (!GroundAiDirector.ExecuteAutonomousAircraftOrder(
                    __instance,
                    __instance.veh,
                    target.GetCenterOfUnit(),
                    () => __instance.DoAttackTarget(target),
                    now))
            {
                return;
            }
            if (__instance.targetToAttack == null)
                return;

            AiState.Trace($"Aircraft autonomy: plane {id} engaging {targetKind} target of opportunity");
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // A target despawned during the native visibility query.
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Aircraft opportunity scan failed: {ex.Message}");
        }
    }

    internal static void Remove(int id)
        => NextOpportunityScans.Remove(id);
}

>>>>>>> Stashed changes
[HarmonyPatch(typeof(Vehicle), "OnDestroy")]
internal static class AircraftStateCleanupPatch
{
    [HarmonyPrefix]
    private static void Prefix(Vehicle __instance)
    {
        if (__instance == null || __instance is not VehiclePlane)
            return;

        var id = __instance.GetInstanceID();
        AircraftFlightPhysics.Remove((VehiclePlane)__instance);
        AircraftEvasionControlPatch.Remove(id);
<<<<<<< Updated upstream
=======
        AircraftAutonomousEngagementPatch.Remove(id);
        GroundAiDirector.ReleaseAircraft(id);
>>>>>>> Stashed changes
        AiState.AircraftEvasionUntil.Remove(id);
        AiState.AircraftEvasionDirection.Remove(id);
    }
}
