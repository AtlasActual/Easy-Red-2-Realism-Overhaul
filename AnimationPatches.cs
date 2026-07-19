using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(Soldier), "SetAnimatorRefreshRate", new[] { typeof(float) })]
internal static class SoldierDistantAnimationQualityPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref float refreshDelay)
    {
        if (Settings.KeepHighQualityDistantAnimations.Value)
            refreshDelay = 0f;
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.PlayOrderAnimSynched))]
internal static class SoldierOrderAnimationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance)
    {
        if (!Settings.LeaderOnlyOrderGestures.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            __instance == null || !__instance.IsAI() || __instance.IsFPSPlayer())
        {
            return true;
        }

        if (!__instance.IsSquadLeader())
            return false;

        var id = __instance.GetInstanceID();
        var now = Time.time;
        if (!AiState.CooldownReady(AiState.NextOrderGesture, id, now))
            return false;

        AiState.NextOrderGesture[id] = now + Settings.OrderGestureCooldownSeconds.Value;
        return true;
    }
}

[HarmonyPatch(typeof(Vehicle), nameof(Vehicle.ForceExitAllVehicle))]
internal static class QuitTimeVehicleExitPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !RuntimeLifecycle.IsQuitting;
}

[HarmonyPatch(typeof(Soldier), "OnDestroy")]
internal static class QuitTimeSoldierDestroyPatch
{
    [HarmonyPrefix]
    private static bool Prefix() => !RuntimeLifecycle.IsQuitting;
}
