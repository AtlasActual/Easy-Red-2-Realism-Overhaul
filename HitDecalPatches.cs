using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(PrefabPool), nameof(PrefabPool.PoolObject))]
internal static class HitDecalPoolDurationPatch
{
    private static bool _failureLogged;

    /// <summary>
    /// Whether a pooled object carries an impact decal, keyed by instance id.
    ///
    /// PrefabPool.PoolObject is the game's generic pooling entry point: every bullet,
    /// casing, muzzle flash, impact effect and particle passes through it, which in a
    /// firefight is far more traffic than anything per-soldier. Answering the question
    /// with GetComponentInChildren(true) — a recursive walk of the whole child hierarchy
    /// including inactive objects — on every one of those was one of the heaviest
    /// per-object costs in the mod, and it scales with rounds fired rather than with
    /// soldier count, which is why it shows up the same in a small battle as at maximum
    /// AI.
    ///
    /// Pooled objects are reused by design, so after warm-up this is a hit almost every
    /// time, and the answer cannot change for a given object: a prefab either has a decal
    /// component or it does not.
    /// </summary>
    private static readonly Dictionary<int, bool> DecalObjects = new();

    internal static void ClearCache() => DecalObjects.Clear();

    [HarmonyPrefix]
    private static void Prefix(GameObject go, ref float pool_time)
    {
        try
        {
            if (go == null)
                return;

            var id = go.GetInstanceID();
            if (!DecalObjects.TryGetValue(id, out var isDecal))
            {
                // Impact-hole prefabs are commonly pooled by a wrapper object, while
                // the TankHole/FPS_Decal component lives on one of its children.
                isDecal = go.GetComponentInChildren<FPS_Decal>(true) != null;
                DecalObjects[id] = isDecal;
            }

            if (!isDecal)
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
