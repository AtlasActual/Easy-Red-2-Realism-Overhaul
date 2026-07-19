using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(PrefabPool), nameof(PrefabPool.PoolObject))]
internal static class HitDecalPoolDurationPatch
{
    private static bool _failureLogged;

    [HarmonyPrefix]
    private static void Prefix(GameObject go, ref float pool_time)
    {
        try
        {
            // Impact-hole prefabs are commonly pooled by a wrapper object, while
            // the TankHole/FPS_Decal component lives on one of its children.
            if (go == null || go.GetComponentInChildren<FPS_Decal>(true) == null)
                return;

            pool_time = Settings.HitDecalDurationSeconds.Value;
        }
        catch (Exception ex)
        {
            if (_failureLogged)
                return;

            _failureLogged = true;
            Plugin.LogSource.LogWarning($"Could not apply the hit-decal duration: {ex.Message}");
        }
    }
}
