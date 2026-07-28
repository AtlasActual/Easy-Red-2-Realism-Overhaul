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
            // Preserve the configured point-blank value but let the advantage
            // remain meaningful through the room/building engagement band instead
            // of shedding most of it halfway to the configured range.
            var spreadCurve = distanceFactor * distanceFactor;
            __result *= Mathf.Lerp(
                Settings.SpreadMultiplierAtPointBlank.Value, 1f, spreadCurve);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Close-quarters accuracy bonus failed: {ex.Message}");
        }
    }
}
