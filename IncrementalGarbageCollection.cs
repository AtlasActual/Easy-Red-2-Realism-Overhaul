using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace ER2RealismOverhaul;

/// <summary>
/// Uses measured frame headroom to keep both managed collectors out of the long tail.
///
/// Easy Red 2 already has incremental collection enabled with Unity's normal 3 ms step,
/// but Unity only fills unused frame time automatically when VSync or targetFrameRate is
/// active. In the uncapped mode used by high-refresh players, the heap was measured
/// growing to nearly 3 GB and repeatedly falling back to 40-74 ms collection frames.
///
/// CoreCLR also scans every managed weak handle during its collections. The interop
/// cleanup fix eliminates finalizer storms, but a max-AI battle can still accumulate
/// hundreds of thousands of liveness handles before a natural collection and pay a
/// 30-56 ms scan. A small proactive Gen0 collection bounds that population before it
/// becomes a visible pause.
///
/// This controller advances Unity's collector and requests managed Gen0 collection only
/// after frames with enough measured headroom. It never changes the game's frame cap and
/// leaves busy frames alone. The previous experiment that globally reduced Unity's step
/// to 400 us is deliberately not repeated: it could not keep up and made the ordinary
/// frame-time distribution worse.
/// </summary>
internal static class IncrementalGarbageCollection
{
    private const float UnityCycleIntervalSeconds = 5f;
    private const float ManagedCollectionMinimumIntervalSeconds = 5f;
    private const int ManagedWeakHandleThreshold = 75_000;
    private const float ManagedCollectionBudgetMilliseconds = 8f;
    private const float TargetFrameMilliseconds = 16f;
    private const float SafetyReserveMilliseconds = 1f;
    private const ulong ManualUnityStepLimitNanoseconds = 1_000_000UL;

    private static bool _applied;
    private static bool _loggedUnavailable;
    private static bool _loggedVsync;
    private static bool _failed;
    private static bool _battleStarted;
    private static bool _unityCycleActive;
    private static float _nextUnityCycleAt;
    private static float _nextManagedCollectionAt;
    private static long _lastManagedGcStamp;
    private static long _unitySteps;
    private static long _unityCyclesStarted;
    private static long _unityCyclesCompleted;
    private static double _unitySpentMilliseconds;
    private static double _unityMaximumStepMilliseconds;
    private static long _unityOverBudgetSteps;
    private static int _lastUnityStepFrame;
    private static double _lastUnityStepMilliseconds;
    private static bool _lastUnityStepCompletedCycle;
    private static long _managedCollections;
    private static double _managedSpentMilliseconds;
    private static double _managedMaximumMilliseconds;
    private static long _managedOverBudgetCollections;
    private static long _managedWeakPopulationTotal;
    private static int _managedMaximumWeakPopulation;

    /// <summary>
    /// Re-applied at battle start because the engine may reset collector settings while
    /// loading a scene. This also gives each runtime test a fresh telemetry interval.
    /// </summary>
    internal static void Apply()
    {
        _battleStarted = true;
        _unityCycleActive = false;
        _nextUnityCycleAt =
            Time.realtimeSinceStartup + UnityCycleIntervalSeconds;
        _nextManagedCollectionAt =
            Time.realtimeSinceStartup + ManagedCollectionMinimumIntervalSeconds;
        _lastManagedGcStamp = ReadManagedGcStamp();
        _unitySteps = 0;
        _unityCyclesStarted = 0;
        _unityCyclesCompleted = 0;
        _unitySpentMilliseconds = 0d;
        _unityMaximumStepMilliseconds = 0d;
        _unityOverBudgetSteps = 0;
        _lastUnityStepFrame = -1;
        _lastUnityStepMilliseconds = 0d;
        _lastUnityStepCompletedCycle = false;
        _managedCollections = 0;
        _managedSpentMilliseconds = 0d;
        _managedMaximumMilliseconds = 0d;
        _managedOverBudgetCollections = 0;
        _managedWeakPopulationTotal = 0;
        _managedMaximumWeakPopulation = 0;
        _failed = false;

        if (!Settings.IncrementalGarbageCollectionEnabled.Value)
            return;

        try
        {
            var sliceMicroseconds = Mathf.Clamp(
                Settings.IncrementalGarbageCollectionSliceMicroseconds.Value, 1000, 5000);
            GarbageCollector.incrementalTimeSliceNanoseconds = (ulong)sliceMicroseconds * 1000UL;

            // isIncremental reports whether the runtime can actually slice collection. A
            // build without incremental support accepts the time slice and ignores it, so
            // without this check a setting that does nothing would look like it worked.
            if (!GarbageCollector.isIncremental)
            {
                if (!_loggedUnavailable)
                {
                    _loggedUnavailable = true;
                    Plugin.LogSource.LogWarning(
                        "Adaptive incremental garbage collection was requested, but this build " +
                        "of the game does not support it. The setting has no effect.");
                }

                return;
            }

            if (_applied)
                return;

            _applied = true;
            Plugin.LogSource.LogInfo(
                $"Adaptive incremental garbage collection active: {sliceMicroseconds}us engine step, " +
                $"{ManualUnityStepLimitNanoseconds / 1000UL}us assisted step, " +
                $"managed weak-handle threshold {ManagedWeakHandleThreshold}, " +
                $"used only after frames with spare time (mode {GarbageCollector.GCMode}, " +
                $"vSync={QualitySettings.vSyncCount}, targetFrameRate={Application.targetFrameRate}).");
        }
        catch (Exception ex)
        {
            DisableAfterFailure(ex);
        }
    }

    internal static void LateUpdate()
    {
        if (!_battleStarted ||
            _failed ||
            !Settings.IncrementalGarbageCollectionEnabled.Value)
        {
            return;
        }

        try
        {
            if (!GarbageCollector.isIncremental ||
                GarbageCollector.GCMode != GarbageCollector.Mode.Enabled)
            {
                return;
            }

            var now = Time.realtimeSinceStartup;
            var previousFrameMilliseconds = Time.unscaledDeltaTime * 1000d;

            if (PaceManagedCollection(now, previousFrameMilliseconds))
                return;

            // Unity automatically spends a VSync frame's remaining idle time on
            // incremental collection. Application.targetFrameRate is deliberately not
            // used as a guard: Easy Red 2 reports a positive internal target even in the
            // user's uncapped setup, yet live heap drops show that its automatic slices
            // are not keeping up. Actual frame duration is the reliable headroom signal.
            if (QualitySettings.vSyncCount > 0)
            {
                if (!_loggedVsync)
                {
                    _loggedVsync = true;
                    Plugin.LogSource.LogInfo(
                        "Adaptive Unity incremental-GC assist is standing down because " +
                        "VSync is active; Unity already uses that idle time.");
                }

                _unityCycleActive = false;
                _nextUnityCycleAt = now + UnityCycleIntervalSeconds;
                return;
            }

            if (!_unityCycleActive && now < _nextUnityCycleAt)
                return;

            var engineSliceNanoseconds = GarbageCollector.incrementalTimeSliceNanoseconds;
            if (engineSliceNanoseconds == 0UL)
                return;

            // Keep Unity's normal global slice intact. The old 400 us experiment changed
            // that global value and made automatic collection fall behind. Only our
            // explicit, headroom-driven work is capped more tightly.
            var assistedSliceNanoseconds =
                Math.Min(engineSliceNanoseconds, ManualUnityStepLimitNanoseconds);
            var sliceMilliseconds = assistedSliceNanoseconds / 1_000_000d;
            if (previousFrameMilliseconds + sliceMilliseconds + SafetyReserveMilliseconds >
                TargetFrameMilliseconds)
            {
                return;
            }

            if (!_unityCycleActive)
            {
                _unityCycleActive = true;
                _unityCyclesStarted++;
            }

            var startedAt = Stopwatch.GetTimestamp();
            var workRemains =
                GarbageCollector.CollectIncremental(assistedSliceNanoseconds);
            var spentMilliseconds =
                (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;

            _unitySteps++;
            _unitySpentMilliseconds += spentMilliseconds;
            if (spentMilliseconds > _unityMaximumStepMilliseconds)
                _unityMaximumStepMilliseconds = spentMilliseconds;
            if (spentMilliseconds > sliceMilliseconds + SafetyReserveMilliseconds)
                _unityOverBudgetSteps++;
            _lastUnityStepFrame = Time.frameCount;
            _lastUnityStepMilliseconds = spentMilliseconds;
            _lastUnityStepCompletedCycle = !workRemains;

            if (!workRemains)
            {
                _unityCycleActive = false;
                _unityCyclesCompleted++;
                _nextUnityCycleAt = now + UnityCycleIntervalSeconds;
            }
        }
        catch (Exception ex)
        {
            DisableAfterFailure(ex);
        }
    }

    private static void DisableAfterFailure(Exception ex)
    {
        _failed = true;
        _unityCycleActive = false;
        Plugin.LogSource.LogWarning(
            $"Adaptive incremental garbage collection was disabled after an error: {ex.Message}");
    }

    private static bool PaceManagedCollection(
        float now,
        double previousFrameMilliseconds)
    {
        var gcStamp = ReadManagedGcStamp();
        if (gcStamp != _lastManagedGcStamp)
        {
            _lastManagedGcStamp = gcStamp;
            _nextManagedCollectionAt =
                now + ManagedCollectionMinimumIntervalSeconds;
            return false;
        }

        if (now < _nextManagedCollectionAt ||
            InteropFinalizerReaper.WeakHandlePopulation <
                ManagedWeakHandleThreshold ||
            previousFrameMilliseconds +
                ManagedCollectionBudgetMilliseconds +
                SafetyReserveMilliseconds >
                TargetFrameMilliseconds)
        {
            return false;
        }

        var weakPopulation = InteropFinalizerReaper.WeakHandlePopulation;
        var startedAt = Stopwatch.GetTimestamp();
        GC.Collect(
            0,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: false);
        var spentMilliseconds =
            (Stopwatch.GetTimestamp() - startedAt) *
            1000d /
            Stopwatch.Frequency;

        _lastManagedGcStamp = ReadManagedGcStamp();
        _nextManagedCollectionAt =
            now + ManagedCollectionMinimumIntervalSeconds;
        _managedCollections++;
        _managedSpentMilliseconds += spentMilliseconds;
        if (spentMilliseconds > _managedMaximumMilliseconds)
            _managedMaximumMilliseconds = spentMilliseconds;
        if (spentMilliseconds >
            ManagedCollectionBudgetMilliseconds + SafetyReserveMilliseconds)
        {
            _managedOverBudgetCollections++;
        }
        _managedWeakPopulationTotal += weakPopulation;
        if (weakPopulation > _managedMaximumWeakPopulation)
            _managedMaximumWeakPopulation = weakPopulation;

        return true;
    }

    private static long ReadManagedGcStamp()
    {
        return
            ((long)GC.CollectionCount(0) << 42) ^
            ((long)GC.CollectionCount(1) << 21) ^
            (uint)GC.CollectionCount(2);
    }

    private static string DescribeAssist()
    {
        if (!Settings.IncrementalGarbageCollectionEnabled.Value)
            return "assist=off";
        if (_failed)
            return "assist=failed";

        var averageUnityStepMilliseconds =
            _unitySteps > 0
                ? _unitySpentMilliseconds / _unitySteps
                : 0d;
        var averageManagedMilliseconds =
            _managedCollections > 0
                ? _managedSpentMilliseconds / _managedCollections
                : 0d;
        var averageManagedWeakPopulation =
            _managedCollections > 0
                ? _managedWeakPopulationTotal / _managedCollections
                : 0L;
        var lastUnityStepAge =
            _lastUnityStepFrame >= 0
                ? Math.Max(0, Time.frameCount - _lastUnityStepFrame)
                : -1;
        return $"unity={(_unityCycleActive ? "working" : "waiting")}" +
               $" cycles={_unityCyclesCompleted}/{_unityCyclesStarted}" +
               $" steps={_unitySteps}" +
               $" avg={averageUnityStepMilliseconds:F2}ms" +
               $" max={_unityMaximumStepMilliseconds:F2}ms" +
               $" over={_unityOverBudgetSteps}" +
               $" last={_lastUnityStepMilliseconds:F2}ms/{lastUnityStepAge}f" +
               $"/{(_lastUnityStepCompletedCycle ? "done" : "more")}; " +
               $"managed collections={_managedCollections}" +
               $" avg={averageManagedMilliseconds:F2}ms" +
               $" max={_managedMaximumMilliseconds:F2}ms" +
               $" over={_managedOverBudgetCollections}" +
               $" weakAtCollect={averageManagedWeakPopulation}/{_managedMaximumWeakPopulation}" +
               $" weak={InteropFinalizerReaper.WeakHandlePopulation}";
    }

    internal static string DescribeFrameAssist()
    {
        try
        {
            return DescribeAssist();
        }
        catch
        {
            return "assist=unavailable";
        }
    }

    internal static string DescribeState()
    {
        try
        {
            return $"incremental={GarbageCollector.isIncremental}" +
                   $" slice={GarbageCollector.incrementalTimeSliceNanoseconds / 1000UL}us" +
                   $" assistedSlice={ManualUnityStepLimitNanoseconds / 1000UL}us" +
                   $" mode={GarbageCollector.GCMode} " +
                   DescribeAssist();
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

internal sealed class IncrementalGarbageCollectionController : MonoBehaviour
{
    private void LateUpdate() => IncrementalGarbageCollection.LateUpdate();
}
