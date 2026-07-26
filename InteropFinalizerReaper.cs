using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace ER2RealismOverhaul;

/// <summary>
/// Moves cleanup of transient Il2CppInterop wrappers off CoreCLR's finalizer path.
///
/// Every Il2CppObjectBase wrapper owns a strong native IL2CPP GC handle and has a
/// managed finalizer. Large battles can create hundreds of thousands of wrappers
/// between managed collections, including many wrappers that never enter
/// Il2CppObjectPool. Discovering that finalizable population stops the game thread,
/// then wrapper finalizers contend with fresh native-to-managed callbacks while
/// freeing and recreating IL2CPP handles.
///
/// This reaper suppresses finalization for every ordinary IntPtr-constructed
/// wrapper. When Il2CppObjectPool publishes its own WeakReference for that wrapper,
/// the reaper reuses that existing liveness handle; only wrappers that bypass the
/// pool receive a private non-finalizable weak GCHandle. A below-normal-priority
/// worker releases dead native handles in bounded batches, atomically detaches stale
/// pool entries, and then retires their managed weak handles after a grace period
/// for concurrent readers.
/// </summary>
internal static class InteropFinalizerReaper
{
    private const int DrainBatchSize = 8192;
    private const int SweepBatchSize = 512;
    private const int PoolMatchGraceMilliseconds = 25;
    private const int PoolReferenceGraceMilliseconds = 1000;

    private readonly struct PendingWrapper
    {
        internal PendingWrapper(
            Il2CppObjectBase wrapper,
            IntPtr pointer,
            IntPtr nativeHandle,
            long privateTrackerAfterTick)
        {
            Wrapper = wrapper;
            Pointer = pointer;
            NativeHandle = nativeHandle;
            PrivateTrackerAfterTick = privateTrackerAfterTick;
        }

        internal Il2CppObjectBase Wrapper { get; }
        internal IntPtr Pointer { get; }
        internal IntPtr NativeHandle { get; }
        internal long PrivateTrackerAfterTick { get; }
    }

    private readonly struct TrackedWrapper
    {
        internal TrackedWrapper(
            GCHandle managedWeakHandle,
            WeakReference<Il2CppObjectBase>? poolReference,
            IntPtr pointer,
            IntPtr nativeHandle)
        {
            ManagedWeakHandle = managedWeakHandle;
            PoolReference = poolReference;
            Pointer = pointer;
            NativeHandle = nativeHandle;
        }

        internal GCHandle ManagedWeakHandle { get; }
        internal WeakReference<Il2CppObjectBase>? PoolReference { get; }
        internal IntPtr Pointer { get; }
        internal IntPtr NativeHandle { get; }
        internal bool UsesPrivateWeakHandle => PoolReference == null;
    }

    private readonly struct TrackedPoolReference
    {
        internal TrackedPoolReference(
            WeakReference<Il2CppObjectBase> reference,
            IntPtr pointer,
            IntPtr nativeHandle)
        {
            Reference = reference;
            Pointer = pointer;
            NativeHandle = nativeHandle;
        }

        internal WeakReference<Il2CppObjectBase> Reference { get; }
        internal IntPtr Pointer { get; }
        internal IntPtr NativeHandle { get; }
    }

    private readonly struct RetiredPoolReference
    {
        internal RetiredPoolReference(
            WeakReference<Il2CppObjectBase> reference,
            long retireAfterTick)
        {
            Reference = reference;
            RetireAfterTick = retireAfterTick;
        }

        internal WeakReference<Il2CppObjectBase> Reference { get; }
        internal long RetireAfterTick { get; }
    }

    private static readonly ConcurrentQueue<PendingWrapper> IncomingWrappers = new();
    private static readonly ConcurrentQueue<TrackedPoolReference> IncomingPoolReferences = new();
    private static readonly AutoResetEvent WorkAvailable = new(false);

    private static AccessTools.FieldRef<Il2CppObjectBase, IntPtr>? _nativeHandleField;
    private static ConcurrentDictionary<IntPtr, WeakReference<Il2CppObjectBase>>? _interopPool;
    private static Action<WeakReference<Il2CppObjectBase>>? _runPoolReferenceFinalizer;
    private static volatile bool _enabled;
    private static volatile bool _installed;
    private static int _workerStarted;

    private static long _wrapperIncoming;
    private static long _poolReferenceIncoming;
    private static long _wrappersTracked;
    private static long _poolReferencesTracked;
    private static long _nativeHandlesReaped;
    private static long _poolReferencesReaped;
    private static long _poolTrackersReused;
    private static long _privateWeakTrackersCreated;
    private static long _failures;
    private static long _workerSweepPasses;
    private static long _workerSweepTicks;
    private static long _workerMaximumSweepTicks;
    private static long _workerOverTwoMillisecondPasses;
    private static int _activeWrappers;
    private static int _activePoolReferences;
    private static int _activePrivateWeakTrackers;
    private static int _retiredPoolReferences;

    internal static bool TryInstall(Harmony harmony)
    {
        if (_installed)
        {
            return true;
        }

        try
        {
            var wrapperConstructor = AccessTools.Constructor(
                typeof(Il2CppObjectBase),
                new[] { typeof(IntPtr) })
                ?? throw new MissingMethodException(
                    typeof(Il2CppObjectBase).FullName,
                    ".ctor(IntPtr)");

            var poolReferenceType = typeof(WeakReference<Il2CppObjectBase>);
            var poolReferenceConstructor = AccessTools.Constructor(
                poolReferenceType,
                new[] { typeof(Il2CppObjectBase) })
                ?? throw new MissingMethodException(
                    poolReferenceType.FullName,
                    ".ctor(Il2CppObjectBase)");

            var poolField = AccessTools.Field(typeof(Il2CppObjectPool), "s_cache")
                ?? throw new MissingFieldException(
                    typeof(Il2CppObjectPool).FullName,
                    "s_cache");
            _interopPool =
                poolField.GetValue(null)
                    as ConcurrentDictionary<IntPtr, WeakReference<Il2CppObjectBase>>
                ?? throw new InvalidCastException(
                    "Il2CppObjectPool.s_cache has an unexpected runtime type.");

            _nativeHandleField =
                AccessTools.FieldRefAccess<Il2CppObjectBase, IntPtr>("myGcHandle");

            var poolReferenceFinalizer = poolReferenceType.GetMethod(
                "Finalize",
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly)
                ?? throw new MissingMethodException(
                    poolReferenceType.FullName,
                    "Finalize");
            _runPoolReferenceFinalizer =
                poolReferenceFinalizer
                    .CreateDelegate<Action<WeakReference<Il2CppObjectBase>>>();

            var wrapperPostfix = AccessTools.Method(
                typeof(InteropFinalizerReaper),
                nameof(OnWrapperConstructed))
                ?? throw new MissingMethodException(
                    typeof(InteropFinalizerReaper).FullName,
                    nameof(OnWrapperConstructed));
            var poolPostfix = AccessTools.Method(
                typeof(InteropFinalizerReaper),
                nameof(OnPoolReferenceConstructed))
                ?? throw new MissingMethodException(
                    typeof(InteropFinalizerReaper).FullName,
                    nameof(OnPoolReferenceConstructed));

            // Both patches remain inert until every runtime hook has resolved and
            // the worker is running. A partial installation therefore preserves
            // Il2CppInterop's stock finalization behavior.
            harmony.Patch(
                wrapperConstructor,
                postfix: new HarmonyMethod(wrapperPostfix));
            harmony.Patch(
                poolReferenceConstructor,
                postfix: new HarmonyMethod(poolPostfix));

            StartWorker();
            _enabled = true;
            _installed = true;
            Plugin.LogSource.LogInfo(
                "Deferred IL2CPP wrapper cleanup installed for all ordinary " +
                "wrappers; native handles and object-pool weak references will " +
                "be reclaimed off the game thread.");
            return true;
        }
        catch (Exception ex)
        {
            _enabled = false;
            Plugin.LogSource.LogError(
                "Deferred IL2CPP wrapper cleanup could not be installed. " +
                "The runtime's original finalizers remain in effect: " + ex);
            return false;
        }
    }

    internal static string DescribeState()
    {
        var workerSweepPasses = Interlocked.Read(ref _workerSweepPasses);
        var averageWorkerSweepMilliseconds =
            workerSweepPasses > 0
                ? Interlocked.Read(ref _workerSweepTicks) *
                  1000d /
                  Stopwatch.Frequency /
                  workerSweepPasses
                : 0d;
        var maximumWorkerSweepMilliseconds =
            Interlocked.Read(ref _workerMaximumSweepTicks) *
            1000d /
            Stopwatch.Frequency;
        return
            (_installed ? "on" : "off") +
            " wrappers=" + Volatile.Read(ref _activeWrappers) +
            "+" + Interlocked.Read(ref _wrapperIncoming) +
            " poolRefs=" + Volatile.Read(ref _activePoolReferences) +
            "+" + Interlocked.Read(ref _poolReferenceIncoming) +
            "+" + Volatile.Read(ref _retiredPoolReferences) +
            " handles=" + Interlocked.Read(ref _nativeHandlesReaped) +
            " weakRefs=" + Interlocked.Read(ref _poolReferencesReaped) +
            " trackerReuse=" + Interlocked.Read(ref _poolTrackersReused) +
            "/" + Interlocked.Read(ref _privateWeakTrackersCreated) +
            " weakPopulation=" + WeakHandlePopulation +
            " tracked=" + Interlocked.Read(ref _wrappersTracked) +
            "/" + Interlocked.Read(ref _poolReferencesTracked) +
            " worker=" + averageWorkerSweepMilliseconds.ToString("F2") +
            "/" + maximumWorkerSweepMilliseconds.ToString("F2") +
            "ms>" + Interlocked.Read(ref _workerOverTwoMillisecondPasses) +
            " failures=" + Interlocked.Read(ref _failures);
    }

    internal static int WeakHandlePopulation =>
        Math.Max(0, Volatile.Read(ref _activePrivateWeakTrackers)) +
        Math.Max(0, Volatile.Read(ref _activePoolReferences)) +
        Math.Max(0, (int)Math.Min(int.MaxValue, Interlocked.Read(ref _poolReferenceIncoming))) +
        Math.Max(0, Volatile.Read(ref _retiredPoolReferences));

    private static void OnWrapperConstructed(Il2CppObjectBase __instance)
    {
        if (!_enabled)
        {
            return;
        }

        var finalizerSuppressed = false;
        var incomingCountIncremented = false;
        var queued = false;
        try
        {
            var nativeHandle = _nativeHandleField!(__instance);
            if (nativeHandle == IntPtr.Zero)
            {
                return;
            }

            var pointer = IL2CPP.il2cpp_gchandle_get_target(nativeHandle);
            if (pointer == IntPtr.Zero)
            {
                return;
            }

            GC.SuppressFinalize(__instance);
            finalizerSuppressed = true;

            var wasEmpty = Interlocked.Increment(ref _wrapperIncoming) == 1;
            incomingCountIncremented = true;
            IncomingWrappers.Enqueue(
                new PendingWrapper(
                    __instance,
                    pointer,
                    nativeHandle,
                    Environment.TickCount64 + PoolMatchGraceMilliseconds));
            queued = true;
            Interlocked.Increment(ref _wrappersTracked);
            if (wasEmpty)
            {
                WorkAvailable.Set();
            }
        }
        catch (Exception ex)
        {
            if (incomingCountIncremented && !queued)
            {
                Interlocked.Decrement(ref _wrapperIncoming);
            }

            if (!queued && finalizerSuppressed)
            {
                GC.ReRegisterForFinalize(__instance);
            }

            ReportFailure("registering an interop wrapper", ex);
        }
    }

    private static void OnPoolReferenceConstructed(
        WeakReference<Il2CppObjectBase> __instance)
    {
        if (!_enabled)
        {
            return;
        }

        var finalizerSuppressed = false;
        var incomingCountIncremented = false;
        var queued = false;
        try
        {
            if (!__instance.TryGetTarget(out var wrapper))
            {
                return;
            }

            var nativeHandle = _nativeHandleField!(wrapper);
            if (nativeHandle == IntPtr.Zero)
            {
                return;
            }

            var pointer = IL2CPP.il2cpp_gchandle_get_target(nativeHandle);
            if (pointer == IntPtr.Zero)
            {
                return;
            }

            GC.SuppressFinalize(__instance);
            finalizerSuppressed = true;

            var wasEmpty =
                Interlocked.Increment(ref _poolReferenceIncoming) == 1;
            incomingCountIncremented = true;
            IncomingPoolReferences.Enqueue(
                new TrackedPoolReference(__instance, pointer, nativeHandle));
            queued = true;
            Interlocked.Increment(ref _poolReferencesTracked);
            if (wasEmpty)
            {
                WorkAvailable.Set();
            }
        }
        catch (Exception ex)
        {
            if (incomingCountIncremented && !queued)
            {
                Interlocked.Decrement(ref _poolReferenceIncoming);
            }

            if (!queued && finalizerSuppressed)
            {
                GC.ReRegisterForFinalize(__instance);
            }

            ReportFailure("registering an interop-pool weak reference", ex);
        }
    }

    private static void StartWorker()
    {
        if (Interlocked.Exchange(ref _workerStarted, 1) != 0)
        {
            return;
        }

        var worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "ER2 deferred interop cleanup",
            Priority = ThreadPriority.BelowNormal
        };
        worker.Start();
    }

    private static void WorkerLoop()
    {
        var pendingWrappers = new List<PendingWrapper>(2048);
        var wrappers = new List<TrackedWrapper>(4096);
        var poolReferences = new List<TrackedPoolReference>(4096);
        var unmatchedPoolReferences =
            new Dictionary<IntPtr, WeakReference<Il2CppObjectBase>>();
        var retiredPoolReferences = new Queue<RetiredPoolReference>();

        var wrapperSweepIndex = 0;
        var wrapperWriteIndex = 0;
        var wrapperSweepLimit = 0;
        var poolSweepIndex = 0;
        var poolWriteIndex = 0;
        var poolSweepLimit = 0;
        var sweepInProgress = false;
        var observedGcStamp = ReadGcStamp();

        while (!RuntimeLifecycle.IsQuitting)
        {
            try
            {
                var sweepPassStartedAt =
                    sweepInProgress ? Stopwatch.GetTimestamp() : 0L;
                if (!sweepInProgress)
                {
                    DrainIncoming(
                        pendingWrappers,
                        poolReferences,
                        unmatchedPoolReferences);
                    PromotePendingWrappers(
                        pendingWrappers,
                        wrappers,
                        unmatchedPoolReferences);

                    var currentGcStamp = ReadGcStamp();
                    if (currentGcStamp != observedGcStamp)
                    {
                        observedGcStamp = currentGcStamp;
                        wrapperSweepIndex = 0;
                        wrapperWriteIndex = 0;
                        wrapperSweepLimit = wrappers.Count;
                        poolSweepIndex = 0;
                        poolWriteIndex = 0;
                        poolSweepLimit = poolReferences.Count;
                        sweepInProgress = true;
                        sweepPassStartedAt = Stopwatch.GetTimestamp();
                    }
                }

                if (sweepInProgress)
                {
                    // Detach dead cache entries before freeing their wrappers'
                    // native handles. Identity-matched removal cannot delete a
                    // replacement wrapper published for the same native pointer.
                    SweepPoolReferences(
                        poolReferences,
                        unmatchedPoolReferences,
                        retiredPoolReferences,
                        ref poolSweepIndex,
                        ref poolWriteIndex,
                        poolSweepLimit);

                    if (poolSweepIndex >= poolSweepLimit)
                    {
                        SweepWrappers(
                            wrappers,
                            ref wrapperSweepIndex,
                            ref wrapperWriteIndex,
                            wrapperSweepLimit);
                    }

                    if (poolSweepIndex >= poolSweepLimit &&
                        wrapperSweepIndex >= wrapperSweepLimit)
                    {
                        CompactAfterSweep(poolReferences, poolWriteIndex);
                        CompactAfterSweep(wrappers, wrapperWriteIndex);
                        sweepInProgress = false;
                    }
                }

                ReapRetiredPoolReferences(retiredPoolReferences);

                Volatile.Write(
                    ref _activeWrappers,
                    wrappers.Count + pendingWrappers.Count);
                Volatile.Write(
                    ref _activePoolReferences,
                    poolReferences.Count);
                Volatile.Write(
                    ref _retiredPoolReferences,
                    retiredPoolReferences.Count);

                if (sweepPassStartedAt != 0L)
                {
                    RecordWorkerSweepPass(
                        Stopwatch.GetTimestamp() - sweepPassStartedAt);
                }

                if (sweepInProgress)
                {
                    // Yield between bounded native-handle batches so cleanup never
                    // monopolizes the IL2CPP GC-handle table. Live testing showed
                    // that smaller batches retain strong handles too long, inflate
                    // Unity's heap, and make its collection tail worse.
                    Thread.Sleep(1);
                }
                else
                {
                    WorkAvailable.WaitOne(10);
                }
            }
            catch (Exception ex)
            {
                ReportFailure("running the deferred interop cleanup worker", ex);
                Thread.Sleep(10);
            }
        }
    }

    private static void RecordWorkerSweepPass(long elapsedTicks)
    {
        Interlocked.Increment(ref _workerSweepPasses);
        Interlocked.Add(ref _workerSweepTicks, elapsedTicks);
        if (elapsedTicks * 1000d / Stopwatch.Frequency > 2d)
            Interlocked.Increment(ref _workerOverTwoMillisecondPasses);

        var currentMaximum =
            Interlocked.Read(ref _workerMaximumSweepTicks);
        while (elapsedTicks > currentMaximum)
        {
            var observed = Interlocked.CompareExchange(
                ref _workerMaximumSweepTicks,
                elapsedTicks,
                currentMaximum);
            if (observed == currentMaximum)
                break;
            currentMaximum = observed;
        }
    }

    private static void DrainIncoming(
        List<PendingWrapper> pendingWrappers,
        List<TrackedPoolReference> poolReferences,
        Dictionary<IntPtr, WeakReference<Il2CppObjectBase>>
            unmatchedPoolReferences)
    {
        var drained = 0;
        while (drained < DrainBatchSize &&
               IncomingWrappers.TryDequeue(out var wrapper))
        {
            pendingWrappers.Add(wrapper);
            Interlocked.Decrement(ref _wrapperIncoming);
            drained++;
        }

        drained = 0;
        while (drained < DrainBatchSize &&
               IncomingPoolReferences.TryDequeue(out var poolReference))
        {
            poolReferences.Add(poolReference);
            unmatchedPoolReferences[poolReference.NativeHandle] =
                poolReference.Reference;
            Interlocked.Decrement(ref _poolReferenceIncoming);
            drained++;
        }
    }

    private static void PromotePendingWrappers(
        List<PendingWrapper> pendingWrappers,
        List<TrackedWrapper> wrappers,
        Dictionary<IntPtr, WeakReference<Il2CppObjectBase>>
            unmatchedPoolReferences)
    {
        var now = Environment.TickCount64;
        var writeIndex = 0;

        for (var index = 0; index < pendingWrappers.Count; index++)
        {
            var pending = pendingWrappers[index];
            if (unmatchedPoolReferences.Remove(
                    pending.NativeHandle,
                    out var poolReference))
            {
                wrappers.Add(
                    new TrackedWrapper(
                        default,
                        poolReference,
                        pending.Pointer,
                        pending.NativeHandle));
                Interlocked.Increment(ref _poolTrackersReused);
                continue;
            }

            if (pending.PrivateTrackerAfterTick > now)
            {
                pendingWrappers[writeIndex++] = pending;
                continue;
            }

            try
            {
                var managedWeakHandle =
                    GCHandle.Alloc(pending.Wrapper, GCHandleType.Weak);
                wrappers.Add(
                    new TrackedWrapper(
                        managedWeakHandle,
                        null,
                        pending.Pointer,
                        pending.NativeHandle));
                Interlocked.Increment(ref _privateWeakTrackersCreated);
                Interlocked.Increment(ref _activePrivateWeakTrackers);
            }
            catch (Exception ex)
            {
                // The pending queue deliberately keeps the wrapper alive until its
                // liveness tracker is ready, so the runtime finalizer can be restored
                // safely if allocating the fallback weak handle fails.
                GC.ReRegisterForFinalize(pending.Wrapper);
                ReportFailure(
                    "creating a private interop-wrapper weak tracker",
                    ex);
            }
        }

        if (writeIndex < pendingWrappers.Count)
        {
            pendingWrappers.RemoveRange(
                writeIndex,
                pendingWrappers.Count - writeIndex);
        }
    }

    private static void SweepWrappers(
        List<TrackedWrapper> wrappers,
        ref int sweepIndex,
        ref int writeIndex,
        int sweepLimit)
    {
        var batchEnd = Math.Min(sweepIndex + SweepBatchSize, sweepLimit);
        while (sweepIndex < batchEnd)
        {
            var entry = wrappers[sweepIndex++];
            var targetIsAlive = false;
            try
            {
                targetIsAlive = entry.PoolReference != null
                    ? entry.PoolReference.TryGetTarget(out _)
                    : entry.ManagedWeakHandle.Target != null;
            }
            catch (Exception ex)
            {
                wrappers[writeIndex++] = entry;
                ReportFailure("reading an interop wrapper weak handle", ex);
                continue;
            }

            if (targetIsAlive)
            {
                wrappers[writeIndex++] = entry;
                continue;
            }

            if (entry.UsesPrivateWeakHandle)
            {
                try
                {
                    entry.ManagedWeakHandle.Free();
                    Interlocked.Decrement(ref _activePrivateWeakTrackers);
                }
                catch (Exception ex)
                {
                    // The handle is still valid when Free throws, so retain the
                    // record and retry after the next managed collection.
                    wrappers[writeIndex++] = entry;
                    ReportFailure("releasing an interop wrapper weak handle", ex);
                    continue;
                }
            }

            try
            {
                IL2CPP.il2cpp_gchandle_free(entry.NativeHandle);
                Interlocked.Increment(ref _nativeHandlesReaped);
            }
            catch (Exception ex)
            {
                // The managed weak handle has already been released and cannot
                // safely be reused. Report the native leak without risking a
                // double free or an invalid-handle retry loop.
                ReportFailure("freeing an IL2CPP wrapper handle", ex);
            }
        }
    }

    private static void SweepPoolReferences(
        List<TrackedPoolReference> poolReferences,
        Dictionary<IntPtr, WeakReference<Il2CppObjectBase>>
            unmatchedPoolReferences,
        Queue<RetiredPoolReference> retiredPoolReferences,
        ref int sweepIndex,
        ref int writeIndex,
        int sweepLimit)
    {
        var batchEnd = Math.Min(sweepIndex + SweepBatchSize, sweepLimit);
        while (sweepIndex < batchEnd)
        {
            var entry = poolReferences[sweepIndex++];
            if (entry.Reference.TryGetTarget(out _))
            {
                poolReferences[writeIndex++] = entry;
                continue;
            }

            if (!TryDetachDeadPoolReference(entry))
            {
                poolReferences[writeIndex++] = entry;
                continue;
            }

            if (unmatchedPoolReferences.TryGetValue(
                    entry.NativeHandle,
                    out var unmatched) &&
                ReferenceEquals(unmatched, entry.Reference))
            {
                unmatchedPoolReferences.Remove(entry.NativeHandle);
            }

            retiredPoolReferences.Enqueue(
                new RetiredPoolReference(
                    entry.Reference,
                    Environment.TickCount64 +
                    PoolReferenceGraceMilliseconds));
        }
    }

    private static bool TryDetachDeadPoolReference(
        TrackedPoolReference entry)
    {
        var pool = _interopPool!;
        if (!pool.TryGetValue(entry.Pointer, out var current))
        {
            return true;
        }

        if (!ReferenceEquals(current, entry.Reference))
        {
            return true;
        }

        var pair = new KeyValuePair<
            IntPtr,
            WeakReference<Il2CppObjectBase>>(
                entry.Pointer,
                entry.Reference);
        var removed =
            ((ICollection<KeyValuePair<
                IntPtr,
                WeakReference<Il2CppObjectBase>>>)pool)
            .Remove(pair);
        if (removed)
        {
            return true;
        }

        // A game-thread lookup may have replaced this stale entry between the
        // identity check and removal. It is safe to retire only after confirming
        // that the dictionary no longer publishes this WeakReference.
        return
            !pool.TryGetValue(entry.Pointer, out current) ||
            !ReferenceEquals(current, entry.Reference);
    }

    private static void ReapRetiredPoolReferences(
        Queue<RetiredPoolReference> retiredPoolReferences)
    {
        var now = Environment.TickCount64;
        var reaped = 0;
        while (reaped < SweepBatchSize &&
               retiredPoolReferences.Count > 0 &&
               retiredPoolReferences.Peek().RetireAfterTick <= now)
        {
            var retired = retiredPoolReferences.Dequeue();
            try
            {
                _runPoolReferenceFinalizer!(retired.Reference);
                Interlocked.Increment(ref _poolReferencesReaped);
            }
            catch (Exception ex)
            {
                ReportFailure(
                    "releasing an interop-pool weak handle",
                    ex);
            }

            reaped++;
        }
    }

    private static void CompactAfterSweep<T>(
        List<T> entries,
        int writeIndex)
    {
        if (writeIndex < entries.Count)
        {
            entries.RemoveRange(
                writeIndex,
                entries.Count - writeIndex);
        }
    }

    private static long ReadGcStamp()
    {
        // Gen1 and Gen2 CollectionCount values include lower-generation work on
        // CoreCLR. We only need a monotonic change detector, not an event count.
        return
            ((long)GC.CollectionCount(0) << 42) ^
            ((long)GC.CollectionCount(1) << 21) ^
            (uint)GC.CollectionCount(2);
    }

    private static void ReportFailure(string operation, Exception ex)
    {
        var failures = Interlocked.Increment(ref _failures);
        if (failures <= 5 || failures % 1000 == 0)
        {
            Plugin.LogSource.LogError(
                $"Deferred interop cleanup failed while {operation} " +
                $"(failure {failures}): {ex}");
        }
    }
}
