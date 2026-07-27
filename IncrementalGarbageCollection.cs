using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace ER2RealismOverhaul;

/// <summary>
/// Tunes the engine's own garbage collector toward time-sliced collection.
///
/// The game's il2cpp heap was measured growing past 2GB in a large battle, with single
/// collections releasing 222-395MB. Each of those is a stop-the-world pause and shows up
/// as a 40-46ms frame in which this mod accounts for ~2ms — they are not the mod's work
/// and no amount of mod-side budgeting reaches them.
///
/// Unity's incremental mode collects in bounded slices instead of one pass, which trades
/// a little throughput for a much shorter worst-case pause. That is the only lever a
/// plugin actually has here: the collector cannot be replaced (tracing needs the
/// runtime's own roots and object layout) and it cannot simply be switched off (this heap
/// would exhaust memory within minutes).
/// </summary>
internal static class IncrementalGarbageCollection
{
    private static bool _applied;
    private static bool _loggedUnavailable;

    /// <summary>
    /// Re-applied at battle start because the engine may reset collector settings when
    /// loading a scene, and applying it once at plugin load would silently lapse.
    /// </summary>
    internal static void Apply()
    {
        if (!Settings.IncrementalGarbageCollectionEnabled.Value)
            return;

        try
        {
            var sliceMicroseconds = Mathf.Clamp(
                Settings.IncrementalGarbageCollectionSliceMicroseconds.Value, 100, 10000);
            GarbageCollector.incrementalTimeSliceNanoseconds = (ulong)sliceMicroseconds * 1000UL;

            // isIncremental reports whether the RUNTIME can actually slice collection. A
            // build without incremental support accepts the time slice and ignores it, so
            // without this check a setting that does nothing would look like it worked.
            if (!GarbageCollector.isIncremental)
            {
                if (!_loggedUnavailable)
                {
                    _loggedUnavailable = true;
                    Plugin.LogSource.LogWarning(
                        "Incremental garbage collection was requested, but this build of the game " +
                        "does not support it; the engine will keep collecting in one pass. " +
                        "The setting has no effect and can be turned back off.");
                }

                return;
            }

            if (_applied)
                return;

            _applied = true;
            Plugin.LogSource.LogInfo(
                $"Incremental garbage collection active: {sliceMicroseconds}us slice per frame " +
                $"(mode {GarbageCollector.GCMode}).");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Could not configure incremental garbage collection: {ex.Message}");
        }
    }

    internal static string DescribeState()
    {
        try
        {
            return $"incremental={GarbageCollector.isIncremental}" +
                   $" slice={GarbageCollector.incrementalTimeSliceNanoseconds / 1000UL}us" +
                   $" mode={GarbageCollector.GCMode}";
        }
        catch (Exception)
        {
            return "incremental=unavailable";
        }
    }
}

[HarmonyPatch(typeof(BattleManager), "Start")]
internal static class IncrementalGarbageCollectionBattleStartPatch
{
    [HarmonyPostfix]
    private static void Postfix() => IncrementalGarbageCollection.Apply();
}
