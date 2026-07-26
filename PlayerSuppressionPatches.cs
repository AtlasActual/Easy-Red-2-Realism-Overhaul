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

    // Bound once as delegates rather than invoked reflectively. This postfix runs for
    // every bullet in flight on every frame, and MethodInfo.Invoke allocates on each
    // call — a box for the returned float, and an object[] plus a box per argument for
    // the sound call — on top of dispatch that is an order of magnitude slower than a
    // direct call. Per-bullet-per-frame is the highest-volume path in this mod, well
    // above anything per-soldier, so allocation here is multiplied by the number of
    // rounds in the air rather than the number of soldiers.
    private static readonly Func<BulletInstance, float>? FlybyRadiusOf =
        BindFlybyRadiusGetter();
    private static readonly Action<BulletInstance, float, float>? PlayFlybySound =
        BindPlayFlybySound();
    private static bool _loggedReflectionFailure;

    private static Func<BulletInstance, float>? BindFlybyRadiusGetter()
    {
        try
        {
            var getter = AccessTools.PropertyGetter(typeof(BulletInstance), "flybyDistanceThreshold");
            return getter == null
                ? null
                : AccessTools.MethodDelegate<Func<BulletInstance, float>>(getter);
        }
        catch (Exception)
        {
            // Falls back to the native behaviour through the null checks below.
            return null;
        }
    }

    private static Action<BulletInstance, float, float>? BindPlayFlybySound()
    {
        try
        {
            var method = AccessTools.Method(
                typeof(BulletInstance), "PlayFlybySound", new[] { typeof(float), typeof(float) });
            return method == null
                ? null
                : AccessTools.MethodDelegate<Action<BulletInstance, float, float>>(method);
        }
        catch (Exception)
        {
            return null;
        }
    }

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

            if (FlybyRadiusOf == null || PlayFlybySound == null)
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
            var nativeRadius = FlybyRadiusOf(__instance);
            if (nativeRadius <= 0f)
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

            PlayFlybySound(__instance, distanceFromShooter, closestDistance);
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
