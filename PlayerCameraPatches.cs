using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(Soldier), nameof(Soldier.GetAimingFOV))]
internal static class PlayerHoldBreathZoomPatch
{
    // PlayerController.LateUpdate applies this native multiplier after calling
    // Soldier.GetAimingFOV whenever the player is holding their breath.
    private const float NativeHoldBreathFovMultiplier = 0.7f;

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance, ref float __result)
    {
        var strength = Settings.HoldBreathZoomMultiplier.Value;
        if (__instance == null ||
            !PlayerController.fpsCamera ||
            !Soldier.CurrentPlayerIsHoldingBreath() ||
            Mathf.Approximately(strength, 1f))
            return;

        // Scale only the extra zoom beyond the regular aimed FOV. The game still
        // performs its own frame-by-frame FOV interpolation after this postfix.
        var adjustedMultiplier = 1f - ((1f - NativeHoldBreathFovMultiplier) * strength);
        var compensation = adjustedMultiplier / NativeHoldBreathFovMultiplier;
        __result = Mathf.Clamp(__result * compensation, 1f, 179f);
    }
}
