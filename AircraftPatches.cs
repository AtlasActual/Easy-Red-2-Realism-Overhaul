using Corvostudio.Weapons;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class AircraftBombSafety
{
    internal static bool UnsafeImpact(VehiclePlane plane, out Vector3 impact)
    {
        impact = Vector3.zero;
        if (!MultiplayerAuthority.CanMutateGameplay() || plane == null)
        {
            return false;
        }

        var pilot = plane.GetDriver();
        if (pilot == null || !pilot.IsAI() || pilot.IsFPSPlayer())
            return false;

        impact = plane.PredictBombImpactPosition();
        if (impact == Vector3.zero)
            return false;

        var radius = Settings.AircraftBombFriendlyRadius.Value;
        if (CommanderMvp.CommanderAircraftUnsafeImpact(plane, impact, radius))
            return true;

        return Settings.AircraftSafetyEnabled.Value &&
               CombatSafety.FriendlyNear(impact, plane.GetVehicleFaction(), radius);
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
        if (pilot == null || !pilot.IsAI() || pilot.IsFPSPlayer())
            return true;

        var targetPosition = target.GetCenterOfUnit();
        var faction = __instance.veh.GetVehicleFaction();
        if (!CombatSafety.FriendlyNear(
                targetPosition, faction, Settings.AircraftFriendlyAttackRadius.Value))
        {
            return true;
        }

        __instance.CancelAttackingTarget();
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
        if (!AircraftBombSafety.UnsafeImpact(__instance, out _))
            return true;

        __result = null!;
        AiState.Trace($"Aircraft safety: plane {__instance.GetInstanceID()} withheld bomb over friendlies");
        return false;
    }
}

[HarmonyPatch(typeof(BombBay), nameof(BombBay.Drop))]
internal static class BombBayReleaseSafetyPatch
{
    [HarmonyPrefix]
    private static bool Prefix(VehiclePlane v, ref bool __result)
    {
        if (!AircraftBombSafety.UnsafeImpact(v, out _))
            return true;

        __result = false;
        AiState.Trace($"Aircraft safety: plane {v.GetInstanceID()} blocked an unsafe bomb-bay release");
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
        AiState.AircraftEvasionUntil.Remove(id);
        AiState.AircraftEvasionDirection.Remove(id);
    }
}
