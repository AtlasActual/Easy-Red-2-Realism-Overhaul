using System.Reflection;
using Corvostudio.Weapons;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Scales only the local presentation of native player suppression. The
/// underlying suppression value, duration, damage, and AI behavior are left
/// untouched.
/// </summary>
[HarmonyPatch(typeof(VolumeGUI), nameof(VolumeGUI.SetSuppression))]
internal static class PlayerSuppressionVignettePatch
{
    private static float _lastNativeSuppression;
    private static bool _hasNativeSuppression;
    private static bool _loggedApplyFailure;

    [HarmonyPrefix]
    private static void Prefix(ref float val, out float __state)
    {
        __state = Mathf.Clamp01(val);
        _lastNativeSuppression = __state;
        _hasNativeSuppression = true;
        val = ScaleSuppression(__state);
    }

    [HarmonyPostfix]
    private static void Postfix(float __state)
    {
        // Writing the final parameter makes the setting reliable even if an
        // IL2CPP update stops propagating a ref argument through the prefix.
        ApplyToNativeVignette(__state);
    }

    internal static void RefreshLastSuppression()
    {
        if (_hasNativeSuppression)
            ApplyToNativeVignette(_lastNativeSuppression);
    }

    private static float ScaleSuppression(float nativeSuppression) =>
        Mathf.Clamp01(
            nativeSuppression * Settings.PlayerSuppressionVignetteMultiplier.Value);

    private static void ApplyToNativeVignette(float nativeSuppression)
    {
        try
        {
            var vignette = VolumeGUI.vignette;
            if (vignette == null)
                return;

            vignette.intensity.value = Mathf.Lerp(
                VolumeGUI.minSuppressionIntensityVal,
                VolumeGUI.maxSuppressionIntensityVal,
                ScaleSuppression(nativeSuppression));
        }
        catch (Exception ex)
        {
            if (_loggedApplyFailure)
                return;

            _loggedApplyFailure = true;
            Plugin.LogSource.LogWarning(
                $"Could not apply the player suppression vignette multiplier: {ex.Message}");
        }
    }
}

/// <summary>
/// Feeds the blur from accepted suppression amounts rather than the native
/// cumulative presentation value, which does not represent a fading effect.
/// </summary>
[HarmonyPatch(typeof(Soldier), nameof(Soldier.Suppress))]
internal static class PlayerSuppressionBlurSignalPatch
{
    private readonly struct SuppressionState
    {
        internal SuppressionState(byte value, float startedAt)
        {
            Value = value;
            StartedAt = startedAt;
        }

        internal byte Value { get; }
        internal float StartedAt { get; }
    }

    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, out SuppressionState __state)
    {
        __state = __instance == null
            ? default
            : new SuppressionState(
                __instance.GetSuppressionValue(),
                __instance.suppression_start_time);
    }

    [HarmonyPostfix]
    private static void Postfix(
        Soldier __instance,
        int suppressionValueAdd,
        SuppressionState __state)
    {
        if (__instance == null || suppressionValueAdd <= 0 || !PlayerController.fpsCamera)
            return;

        var localPlayer = Soldier.CurrentControlledSoldierOrNull();
        if (localPlayer == null || localPlayer.GetInstanceID() != __instance.GetInstanceID())
            return;

        var currentValue = __instance.GetSuppressionValue();
        var accepted = currentValue > __state.Value ||
                       __instance.suppression_start_time > __state.StartedAt;
        if (!accepted)
            return;

        PlayerSuppressionBlurController.AddReceivedSuppression(
            suppressionValueAdd / 100f);
    }
}

/// <summary>
/// Extends only the hostile bullet flyby radius used by the local first-person
/// player. The private native flyby routine remains responsible for sound,
/// faction checks, HUD feedback, and the base game's 25-point suppression hit.
/// </summary>
[HarmonyPatch(typeof(BulletInstance), nameof(BulletInstance.CheckFlybyIfNeeded))]
internal static class PlayerSuppressionNearMissRadiusPatch
{
    private const float ExpandedFlybyCooldownMinSeconds = 0.1f;
    private const float ExpandedFlybyCooldownMaxSeconds = 0.22f;

    private static readonly MethodInfo? FlybyRadiusGetter =
        AccessTools.PropertyGetter(typeof(BulletInstance), "flybyDistanceThreshold");
    private static readonly MethodInfo? PlayFlybySoundMethod =
        AccessTools.Method(typeof(BulletInstance), "PlayFlybySound", new[] { typeof(float), typeof(float) });
    private static bool _loggedReflectionFailure;

    [HarmonyPostfix]
    private static void Postfix(BulletInstance __instance, Camera cam)
    {
        try
        {
            var radiusMultiplier = Settings.PlayerSuppressionNearMissRadiusMultiplier.Value;
            if (radiusMultiplier <= 1.001f || __instance == null || cam == null ||
                __instance.hasPlayedFlyby || __instance.isMortar || !__instance.canDamage ||
                __instance.bulletData == null || __instance.shooter == null ||
                !PlayerController.fpsCamera ||
                Time.time < BulletInstance.nextPossibleBulletCrack)
            {
                return;
            }

            var player = Soldier.CurrentControlledSoldierOrNull();
            if (player == null || player == __instance.shooter || !player.IsAlive ||
                CombatSafety.SameFaction(player.faction, __instance.shooter.faction))
            {
                return;
            }

            if (FlybyRadiusGetter == null || PlayFlybySoundMethod == null)
            {
                LogReflectionFailure("native flyby methods were not found");
                return;
            }

            var segmentStart = __instance.previousPosition;
            var segment = __instance.curPos - segmentStart;
            var segmentLengthSqr = segment.sqrMagnitude;
            if (segmentLengthSqr <= 0.0001f)
                return;

            var cameraPosition = cam.transform.position;
            var towardCamera = cameraPosition - segmentStart;
            if (towardCamera.sqrMagnitude <= 0.0001f ||
                Vector3.Dot(segment.normalized, towardCamera.normalized) <= BulletInstance.minForwardDot)
            {
                return;
            }

            // Match the native point-to-segment geometry, then handle only the
            // added outer ring after the original method has had first refusal.
            var along = Mathf.Clamp01(Vector3.Dot(towardCamera, segment) / segmentLengthSqr);
            var closestPosition = segmentStart + segment * along;
            var closestDistance = Vector3.Distance(cameraPosition, closestPosition);
            var nativeRadiusObject = FlybyRadiusGetter.Invoke(__instance, null);
            if (nativeRadiusObject is not float nativeRadius || nativeRadius <= 0f)
                return;

            var expandedRadius = nativeRadius * radiusMultiplier;
            if (closestDistance <= nativeRadius || closestDistance > expandedRadius)
                return;

            var distanceFromShooter = Vector3.Distance(__instance.startPosition, cameraPosition);
            BulletInstance.nextPossibleBulletCrack =
                Time.time + UnityEngine.Random.Range(
                    ExpandedFlybyCooldownMinSeconds,
                    ExpandedFlybyCooldownMaxSeconds);
            if (__instance.bulletData.IsHighCaliber())
                BulletInstance.nextPossibleBulletCrack += UnityEngine.Random.Range(0f, 1f);

            PlayFlybySoundMethod.Invoke(
                __instance,
                new object[] { distanceFromShooter, closestDistance });
            __instance.hasPlayedFlyby = true;
        }
        catch (Exception ex)
        {
            LogReflectionFailure(ex.GetBaseException().Message);
        }
    }

    private static void LogReflectionFailure(string reason)
    {
        if (_loggedReflectionFailure)
            return;

        _loggedReflectionFailure = true;
        Plugin.LogSource.LogWarning(
            $"Could not extend the player suppression near-miss radius; native behavior remains active: {reason}");
    }
}

[HarmonyPatch(typeof(FPSGunManager), nameof(FPSGunManager.GetSuppressionEulerAngles))]
internal static class PlayerSuppressionWobblePatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Vector3 __result)
    {
        __result *= Settings.PlayerSuppressionWobbleMultiplier.Value;
    }
}

[HarmonyPatch(typeof(DamageIndicator), nameof(DamageIndicator.ShowIndicator))]
internal static class PlayerSuppressionDirectionMarkerPatch
{
    [HarmonyPrefix]
    private static bool Prefix(bool isDamage)
    {
        // Damage and suppression share this HUD component. The base game passes
        // false only for suppression, so damage direction feedback stays native.
        return isDamage || Settings.ShowPlayerSuppressionDirectionMarker.Value;
    }
}
