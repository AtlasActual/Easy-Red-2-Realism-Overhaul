using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.ProcessAiAccuracy))]
internal static class CloseQuartersAccuracyPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier user, ref float __result)
    {
        if (!Settings.CloseQuartersEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
            return;

        try
        {
            if (__result <= 0f || !AiOwnership.IsAutonomous(user) || user.IsOnVehicle())
                return;

            var target = user.CurrentVisibleTarget;
            if (!TargetAcquisition.TryGetTargetSnapshot(target, out _, out var targetPosition))
                return;

            var distance = Vector3.Distance(user.LookPosition(), targetPosition);
            var closeQuartersRange = Settings.CloseQuartersRangeMeters.Value;
            if (distance >= closeQuartersRange)
                return;

            var distanceFactor = Mathf.Clamp01(distance / closeQuartersRange);
            __result *= Mathf.Lerp(Settings.SpreadMultiplierAtPointBlank.Value, 1f, distanceFactor);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Close-quarters accuracy bonus failed: {ex.Message}");
        }
    }
}
