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
            !AiOwnership.IsAutonomous(__instance))
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
    private static bool Prefix(Soldier __instance)
    {
        // The authoritative point to release this soldier's per-soldier state: here the
        // instance id is still valid, whereas SoldierAI.OnDestroy has to reach the Soldier
        // through GetSoldier() and gets null once the native object is gone — which is how
        // the state leaked. Purging by id from both sides is idempotent.
        try
        {
            AiState.RemoveSoldierById(__instance.GetInstanceID(), __instance.Pointer);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Soldier destroy cleanup failed: {ex.Message}");
        }

        return !RuntimeLifecycle.IsQuitting;
    }
}

/// <summary>
/// Creature.OnDestroy exists only to un-parent the main camera when it is attached
/// to the dying creature, but it dereferences ResourcesManager.mainCamera without a
/// null check. A multiplayer client destroys soldiers while it still has no camera
/// (it has not spawned yet), so the native body throws. Soldier.OnDestroy calls this
/// first, so that throw also skips the Squad.Leave that follows it, leaving destroyed
/// soldiers registered in their squads. With no camera there is nothing to detach,
/// which is exactly what the native early-outs already do when their guards hold.
/// </summary>
[HarmonyPatch(typeof(Creature), "OnDestroy")]
internal static class CreatureDestroyCameraDetachPatch
{
    private static bool _loggedSkip;

    [HarmonyPrefix]
    private static bool Prefix()
    {
        try
        {
            var camera = ResourcesManager.mainCamera;
            if (camera != null && camera.transform != null)
                return true;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Could not inspect the main camera while destroying a creature: {ex.Message}");
            return true;
        }

        if (!_loggedSkip)
        {
            _loggedSkip = true;
            Plugin.LogSource.LogWarning(
                "Skipped the native camera detach for a creature destroyed while no main camera exists; further repeats will be suppressed.");
        }

        return false;
    }
}
