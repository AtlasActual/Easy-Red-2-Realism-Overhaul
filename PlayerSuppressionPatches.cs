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
    [HarmonyPrefix]
    private static void Prefix(ref float val)
    {
        val *= Settings.PlayerSuppressionVignetteMultiplier.Value;
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
