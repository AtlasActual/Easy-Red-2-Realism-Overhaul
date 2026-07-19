using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class AttackingForceBonus
{
    internal static bool AppliesTo(Soldier soldier)
    {
        if (soldier == null || !soldier.IsAI())
            return false;

        var battle = BattleManager.GetCurrentBattleData();
        return battle != null && battle.IsInvaderFaction(soldier.faction);
    }
}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.ProcessAiAccuracy))]
internal static class AttackingForceAccuracyPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier user, ref float __result)
    {
        if (!Settings.AttackingForceBonusEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
            return;

        try
        {
            if (!AttackingForceBonus.AppliesTo(user) || __result <= 0f)
                return;

            __result *= Settings.AttackingForceAccuracySpreadMultiplier.Value;

            var vehicle = user.GetCurrentVehicle();
            if (vehicle != null && vehicle.GetComponent<VehicleTank>() != null)
                __result *= Settings.AttackingTankAdditionalAccuracySpreadMultiplier.Value;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Attacking-force accuracy bonus failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Suppress))]
internal static class AttackingForceSuppressionPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, ref int suppressionValueAdd)
    {
        if (!Settings.AttackingForceBonusEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || suppressionValueAdd <= 0)
            return;

        try
        {
            if (!AttackingForceBonus.AppliesTo(__instance))
                return;

            suppressionValueAdd = Mathf.Max(
                1,
                Mathf.RoundToInt(
                    suppressionValueAdd * Settings.AttackingForceSuppressionReceivedMultiplier.Value));
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Attacking-force suppression bonus failed: {ex.Message}");
        }
    }
}
