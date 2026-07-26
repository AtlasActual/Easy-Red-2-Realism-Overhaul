using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Per-frame Harmony patch entry points that ModTimeProbe attributes mod time to.
/// Add new values only for genuinely distinct always-installed patch groups;
/// everything else that is per-frame but not one of the named suspects goes to
/// <see cref="Other"/>.
/// </summary>
internal enum ModTimeSite
{
    TacticalMove,        // MoveOptimized/MoveFPSOptimized prefix+postfix pipeline
    FightingPose,        // GetFavouriteFightingPose postfix
    SequentialUpdate,    // SoldierAI sequential update pre/postfix (director pipeline)
    IncomingFire,        // per-FixedUpdate incoming-fire/suppression patches
    SuppressiveFire,     // KnownTargetSuppressiveFire FixedUpdate postfix
    VehicleAi,           // per-frame AIVehicle/vehicle-tactics patches
    Projectile,          // per-bullet-per-frame BulletInstance patches (penetration, flyby)
    Other                // any additional per-frame site found
}

/// <summary>
/// Leaf-level sub-attribution for the native-heavy primitives reached from inside the
/// per-soldier tactical pipeline (any bucket). Unlike <see cref="ModTimeSite"/> these
/// are NOT summed into the reported mod total — they are a within-frame breakdown so a
/// spike line can name which primitive dominated. Each is a mutually-exclusive leaf
/// (they do not nest), so the reported "top sub" is a real primitive rather than an
/// outer wrapper that merely contains one.
/// </summary>
/// <summary>
/// Position within the per-soldier tactical pipeline, stamped as it advances. The probe
/// showed single TacticalMove calls blocking for 47-57ms with no allocation, no
/// meaningful GC and no cover-geometry work — the signature of one native call stalling,
/// not of accumulated managed work. Aggregates cannot name that call, so a slow call
/// reports the stage it was standing on when the stall happened.
/// </summary>
internal enum TacticalStage
{
    None,
    // Stamped before the opening interop calls of each side. Without these, a stall in
    // that unstamped head reports whatever stage the PREVIOUS call left behind, which
    // makes the last stage a prefix stamps look guilty for stalls it never saw.
    PrefixEntry,
    PostfixEntry,
    SuppressionReaction,
    FireDanger,
    ReloadPosture,
    PinnedSuppression,
    MovementStall,
    WriteThrough,
    ChargeCheck,
    StopTacticalMovement,
    PoseApply,
    FireDecision,
    ThreatFacingRelease,
    SuppressiveInterrupt,
    PostfixMaintainPose,
    PostfixFireDecision
}

/// <summary>
/// Position within the SequentialUpdate pipeline, stamped as it advances. A single call
/// there was measured at 75.4ms while the bucket total for the whole frame was 75.7ms —
/// one stalled call, not a wave, which is the case a per-frame budget cannot help. This
/// is the same instrument that turned a guess about the tactical pipeline into the
/// thrash-loop fix; SequentialUpdate is the last hot site without it.
/// </summary>
internal enum SequentialStage
{
    None,
    PrefixEntry,
    PrefixDefensiveHold,
    PostfixEntry,
    PostfixVehicleSuspend,
    DirectorSquadOwnership,
    DirectorSquadLeaderSupport,
    DirectorGunfirePoll,
    DirectorPerception,
    DirectorSquadChange,
    DirectorSnapshot,
    DirectorProposals,
    DirectorArbitration,
    DirectorSuppressionReaction,
    DirectorSuppressiveSchedule,
    DirectorMovementExecutor,
    DirectorHazard,
    DirectorMovementWatchdog,
    DirectorPose,
    DirectorAntiArmor,
    DirectorWeaponRange,
    DirectorFireDecision,
    DirectorBattleChatter
}

internal enum ModSubSite
{
    CoverGeometry,       // EvaluateCoverGeometry: 12 ballistic penetration lines + 3 fire-lane casts
    MuzzleLane,          // HasNearMuzzleObstruction muzzle-clearance raycast
    Occupancy,           // CoverOccupancy.IsOccupiedByOther overlap sphere + GetComponentInParent
    SquadScan,           // HasFavorableAttackAdvance O(all ContactStates) covering-fire scan
    StopMove             // Soldier.StopMove native locomotion write
}

/// <summary>
/// Accumulates Stopwatch ticks spent inside the mod's per-frame Harmony patch
/// bodies, bucketed by <see cref="ModTimeSite"/>. Drained every frame by
/// <see cref="StutterProbe.Update"/> so a spike log line can report how much of
/// that frame the mod itself accounted for, and which site cost the most.
/// Overhead when the probe is disabled is a single bool check per Begin() call.
/// </summary>
internal static class ModTimeProbe
{
    private static readonly long[] TicksBySite = new long[8];
    private static readonly long[] SubTicksBySite = new long[5];

    // The most expensive single TacticalMove invocation (one prefix or one postfix)
    // seen this frame: a soldier-clustered spike shows up here even when the summed
    // bucket looks moderate. Reset by Drain each frame.
    private static long _maxTacticalCallTicks;

    // Every actual EvaluateCoverGeometry run this frame, first-eval and refresh alike.
    // The existing postureEvals counter only counts BUDGETED refreshes, so it reads ~0
    // during a first-evaluation wave; this counts the runs that actually happened.
    private static int _coverGeometryRuns;

    // Timed patch-body entries this frame. Divided into the summed ms it gives the mean
    // cost of one patched call, which is what separates "each call is expensive" from
    // "cheap calls, but far too many of them" — the two need opposite fixes.
    private static int _calls;

    internal static long Begin() => Settings.StutterProbeEnabled.Value ? Stopwatch.GetTimestamp() : 0L;

    // Allocation attribution. Managed bytes per patched call sit at a flat 24-31 across
    // every log and fall to zero with the patches uninstalled, so the garbage tracks the
    // NUMBER of calls rather than what any of them does — it is the per-call boundary
    // cost, not the AI logic. These two counters split that: everything a tactical call
    // allocates, versus what its opening block alone allocates (the instance marshalling
    // and GetSoldier). If the two figures match, the cost is at the boundary; if the
    // opening block is near zero, it is deeper in and the boundary is innocent.
    private static long _tacticalAllocBytes;
    private static long _tacticalAllocCalls;
    private static long _entryAllocBytes;
    private static long _entryAllocCalls;

    internal static long BeginAlloc() =>
        Settings.StutterProbeEnabled.Value ? GC.GetAllocatedBytesForCurrentThread() : 0L;

    internal static void EndTacticalAlloc(long begin)
    {
        if (begin == 0L)
            return;

        _tacticalAllocBytes += GC.GetAllocatedBytesForCurrentThread() - begin;
        _tacticalAllocCalls++;
    }

    internal static void EndEntryAlloc(long begin)
    {
        if (begin == 0L)
            return;

        _entryAllocBytes += GC.GetAllocatedBytesForCurrentThread() - begin;
        _entryAllocCalls++;
    }

    // Whole-site allocation for the buckets that have no stage breakdown. Once the
    // tactical pipeline's worst site was fixed it accounted for only ~2% of what the
    // patches allocate, so the remainder is in sites nothing has measured yet — the
    // per-bullet path especially, which runs at far higher volume than anything
    // per-soldier.
    private static readonly long[] AllocBySite = new long[8];

    internal static void EndSiteAlloc(ModTimeSite site, long begin)
    {
        if (begin == 0L)
            return;

        AllocBySite[(int)site] += GC.GetAllocatedBytesForCurrentThread() - begin;
    }

    private static string DescribeSiteAllocation()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < AllocBySite.Length; i++)
        {
            if (AllocBySite[i] < 1024)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append((ModTimeSite)i).Append('=').Append(AllocBySite[i] / 1024).Append("KB");
        }

        Array.Clear(AllocBySite, 0, AllocBySite.Length);
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    internal static string DescribeAllocation()
    {
        var perCall = _tacticalAllocCalls > 0 ? _tacticalAllocBytes / (double)_tacticalAllocCalls : 0d;
        var perEntry = _entryAllocCalls > 0 ? _entryAllocBytes / (double)_entryAllocCalls : 0d;
        _tacticalAllocBytes = 0;
        _tacticalAllocCalls = 0;
        _entryAllocBytes = 0;
        _entryAllocCalls = 0;
        return $"tacticalAlloc {perCall:F1}B/call, entry {perEntry:F1}B/call, " +
               $"topAlloc [{DescribeStageAllocation()}], " +
               $"seqAlloc [{DescribeSequentialAllocation()}], " +
               $"siteAlloc [{DescribeSiteAllocation()}]";
    }

    internal static void End(ModTimeSite site, long begin)
    {
        if (begin == 0L)
            return;

        TicksBySite[(int)site] += Stopwatch.GetTimestamp() - begin;
        _calls++;
    }

    private static TacticalStage _stage;
    private static float _nextSlowCallLogAt;

    // A single call this slow is a stall, not workload: the whole pipeline costs ~3us per
    // call in steady state, so anything past this is one native call blocking.
    private const float SlowCallMs = 10f;

    // Allocation attributed to the stage that was active when it happened. The opening
    // block measured ~0 while a whole call measured 34-53 bytes, so the garbage is inside
    // the pipeline rather than at the il2cpp boundary; the stage markers already stamped
    // for the stall hunt make a per-stage split nearly free to collect.
    private static readonly long[] StageAllocBytes = new long[24];
    private static long _stageAllocMark;

    internal static void Stage(TacticalStage stage)
    {
        if (!Settings.StutterProbeEnabled.Value)
            return;

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        if (_stageAllocMark != 0L)
        {
            var index = (int)_stage;
            if ((uint)index < StageAllocBytes.Length)
                StageAllocBytes[index] += allocated - _stageAllocMark;
        }

        _stageAllocMark = allocated;
        _stage = stage;
    }

    // Closes the open stage span so the last stage of a call is attributed too.
    private static void CloseStageAllocation()
    {
        if (_stageAllocMark == 0L)
            return;

        var index = (int)_stage;
        if ((uint)index < StageAllocBytes.Length)
            StageAllocBytes[index] += GC.GetAllocatedBytesForCurrentThread() - _stageAllocMark;
        _stageAllocMark = 0L;
    }

    private static string DescribeStageAllocation()
    {
        var sb = new StringBuilder();
        for (var round = 0; round < 3; round++)
        {
            var topIndex = -1;
            long topBytes = 0;
            for (var i = 0; i < StageAllocBytes.Length; i++)
            {
                if (StageAllocBytes[i] > topBytes)
                {
                    topBytes = StageAllocBytes[i];
                    topIndex = i;
                }
            }

            if (topIndex < 0)
                break;

            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append((TacticalStage)topIndex).Append('=').Append(topBytes / 1024).Append("KB");
            StageAllocBytes[topIndex] = 0;
        }

        Array.Clear(StageAllocBytes, 0, StageAllocBytes.Length);
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    private static SequentialStage _sequentialStage;

    // The tactical pipeline turned out to be only ~2% of what the patches allocate once
    // its worst site was fixed, so the bulk is on the director path. Same attribution,
    // reusing the markers already stamped there.
    private static readonly long[] SequentialAllocBytes = new long[24];
    private static long _sequentialAllocMark;

    internal static void Stage(SequentialStage stage)
    {
        if (!Settings.StutterProbeEnabled.Value)
            return;

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        if (_sequentialAllocMark != 0L)
        {
            var index = (int)_sequentialStage;
            if ((uint)index < SequentialAllocBytes.Length)
                SequentialAllocBytes[index] += allocated - _sequentialAllocMark;
        }

        _sequentialAllocMark = allocated;
        _sequentialStage = stage;
    }

    private static void CloseSequentialAllocation()
    {
        if (_sequentialAllocMark == 0L)
            return;

        var index = (int)_sequentialStage;
        if ((uint)index < SequentialAllocBytes.Length)
            SequentialAllocBytes[index] += GC.GetAllocatedBytesForCurrentThread() - _sequentialAllocMark;
        _sequentialAllocMark = 0L;
    }

    private static string DescribeSequentialAllocation()
    {
        var sb = new StringBuilder();
        for (var round = 0; round < 3; round++)
        {
            var topIndex = -1;
            long topBytes = 0;
            for (var i = 0; i < SequentialAllocBytes.Length; i++)
            {
                if (SequentialAllocBytes[i] > topBytes)
                {
                    topBytes = SequentialAllocBytes[i];
                    topIndex = i;
                }
            }

            if (topIndex < 0)
                break;

            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append((SequentialStage)topIndex).Append('=').Append(topBytes / 1024).Append("KB");
            SequentialAllocBytes[topIndex] = 0;
        }

        Array.Clear(SequentialAllocBytes, 0, SequentialAllocBytes.Length);
        return sb.Length == 0 ? "none" : sb.ToString();
    }

    // TacticalMove prefix/postfix wrappers use this instead of End so the same span is
    // both summed into the bucket and compared against the per-frame single-call maximum.
    internal static void EndTacticalMove(long begin)
    {
        if (begin == 0L)
            return;

        CloseStageAllocation();
        var elapsed = Stopwatch.GetTimestamp() - begin;
        TicksBySite[(int)ModTimeSite.TacticalMove] += elapsed;
        _calls++;
        if (elapsed > _maxTacticalCallTicks)
            _maxTacticalCallTicks = elapsed;

        var elapsedMs = elapsed * (1000f / Stopwatch.Frequency);
        if (elapsedMs < SlowCallMs)
            return;

        // Rate-limited: a stall usually hits several soldiers in the same frame and the
        // first line already names the stage.
        var now = Time.realtimeSinceStartup;
        if (now < _nextSlowCallLogAt)
            return;
        _nextSlowCallLogAt = now + 1f;

        Plugin.LogSource.LogWarning(
            $"Slow tactical call: {elapsedMs:F1}ms blocked at stage {_stage}.");
    }

    // SequentialUpdate has repeatedly shown 50-55ms in a frame with no way to tell one
    // stalled call from a wave of ordinary ones — maxSoldier only ever covered
    // TacticalMove. Same treatment: track the worst single call and name it when it is
    // slow enough to be a stall rather than workload.
    private static long _maxSequentialCallTicks;

    internal static void EndSequentialUpdate(long begin)
    {
        if (begin == 0L)
            return;

        CloseSequentialAllocation();
        var elapsed = Stopwatch.GetTimestamp() - begin;
        TicksBySite[(int)ModTimeSite.SequentialUpdate] += elapsed;
        _calls++;
        if (elapsed > _maxSequentialCallTicks)
            _maxSequentialCallTicks = elapsed;

        var elapsedMs = elapsed * (1000f / Stopwatch.Frequency);
        if (elapsedMs < SlowCallMs)
            return;

        var now = Time.realtimeSinceStartup;
        if (now < _nextSlowCallLogAt)
            return;
        _nextSlowCallLogAt = now + 1f;

        Plugin.LogSource.LogWarning(
            $"Slow sequential update: {elapsedMs:F1}ms blocked at stage {_sequentialStage}.");
    }

    // Same treatment as the tactical and sequential sites: a bucket total cannot tell one
    // stalled call from a wave of ordinary ones, and that distinction decides whether a
    // per-frame budget is the right fix or the wrong one.
    internal static void EndIncomingFire(long begin)
    {
        if (begin == 0L)
            return;

        var elapsed = Stopwatch.GetTimestamp() - begin;
        TicksBySite[(int)ModTimeSite.IncomingFire] += elapsed;
        _calls++;

        var elapsedMs = elapsed * (1000f / Stopwatch.Frequency);
        if (elapsedMs < SlowCallMs)
            return;

        var now = Time.realtimeSinceStartup;
        if (now < _nextSlowCallLogAt)
            return;
        _nextSlowCallLogAt = now + 1f;

        Plugin.LogSource.LogWarning(
            $"Slow incoming-fire update: {elapsedMs:F1}ms in one call.");
    }

    internal static void EndSub(ModSubSite site, long begin)
    {
        if (begin != 0L)
            SubTicksBySite[(int)site] += Stopwatch.GetTimestamp() - begin;
    }

    internal static void CountCoverGeometryRun()
    {
        if (Settings.StutterProbeEnabled.Value)
            _coverGeometryRuns++;
    }

    // Fills siteMs[i] with the ms spent in ModTimeSite i this frame (caller-owned
    // buffer, length >= TicksBySite.Length; reused every frame so Drain never allocates).
    // The full per-site breakdown replaced the single "top" site: capping one site can
    // squeeze the frame-wide interop/GC tax into the uncapped ones, which is only visible
    // when every nonzero bucket is printed.
    internal static void Drain(
        float[] siteMs,
        out float totalMs,
        out float maxTacticalCallMs,
        out ModSubSite subTop,
        out float subTopMs,
        out int coverGeometryRuns,
        out int calls,
        out float maxSequentialCallMs)
    {
        var msPerTick = 1000f / Stopwatch.Frequency;
        long totalTicks = 0;
        for (var i = 0; i < TicksBySite.Length; i++)
        {
            var ticks = TicksBySite[i];
            totalTicks += ticks;
            siteMs[i] = ticks * msPerTick;
            TicksBySite[i] = 0;
        }

        long subTopTicks = 0;
        var subTopSite = ModSubSite.CoverGeometry;
        for (var i = 0; i < SubTicksBySite.Length; i++)
        {
            var ticks = SubTicksBySite[i];
            if (ticks > subTopTicks)
            {
                subTopTicks = ticks;
                subTopSite = (ModSubSite)i;
            }
            SubTicksBySite[i] = 0;
        }

        totalMs = totalTicks * msPerTick;
        maxTacticalCallMs = _maxTacticalCallTicks * msPerTick;
        subTop = subTopSite;
        subTopMs = subTopTicks * msPerTick;
        coverGeometryRuns = _coverGeometryRuns;
        calls = _calls;
        maxSequentialCallMs = _maxSequentialCallTicks * msPerTick;
        _maxTacticalCallTicks = 0;
        _maxSequentialCallTicks = 0;
        _coverGeometryRuns = 0;
        _calls = 0;
    }
}

/// <summary>
/// Diagnostic frame-time probe for hunting intermittent stutters. Whenever a frame
/// takes far longer than the recent average, one log line records what coincided
/// with it: managed GC collections, a detailed cover search, a casualty-suppression
/// batch, the current soldier count, and how much of the frame the mod's own
/// patches accounted for. A spike with none of those markers points at
/// native/game-side work instead of the mod.
/// Diagnostic-only; never changes AI decisions or synchronized gameplay.
/// </summary>
internal static class StutterProbe
{
    // 70ms only catches frames bad enough to read as a jolt. Continuous stutter is made
    // of 40-60ms frames, which the old floor discarded silently, so the attributed lines
    // never described what the player was actually feeling.
    private const float MinimumSpikeSeconds = 0.04f;
    private const float SpikeFactor = 2.5f;
    private const float LogCooldownSeconds = 1f;

    private const float CacheCensusIntervalSeconds = 30f;

    // Spike lines answer "what happened during that one bad frame?", which cannot answer
    // "is this stuttering constantly?" — a run can feel terrible while crossing the spike
    // floor once a minute. Every frame is bucketed instead, and the shape is reported on
    // the census cadence: the fraction of frames over each threshold is the difference
    // between constant stutter and an occasional hitch, and comparing that fraction with
    // InstallGameplayPatches on and off is what attributes it to the mod or clears it.
    // Fixed millisecond thresholds only describe a run whose baseline is near 60 FPS. At
    // ~210 FPS a 19ms frame is a four-times-median hitch the player plainly feels, and a
    // 20ms floor records it as nothing at all. Half-millisecond bins to 64ms plus one
    // open-ended overflow bin give percentiles and a median-relative hitch count, which
    // stay meaningful at any framerate; two absolute counts remain for cross-run comparison.
    private const int FrameBinCount = 129;
    private const float FrameBinMs = 0.5f;
    private static readonly int[] FrameBins = new int[FrameBinCount];
    private static int _intervalFrames;
    private static float _intervalSeconds;
    private static float _intervalWorstMs;
    private static double _intervalModMs;
    private static long _intervalModCalls;

    private static float _lastFrameAt = -1f;
    private static float _smoothedFrameSeconds = 1f / 60f;
    private static float _nextLogAllowedAt;
    private static float _nextCacheCensusAt;
    private static int _lastSoldierCount = -1;
    private static long _lastIl2CppUsedBytes = -1;
    // Managed bytes allocated on the game thread. The game itself is IL2CPP/native, so
    // nearly all of this is the mod or Il2CppInterop marshalling on its behalf. The
    // collection counters and GCMemoryInfo below establish whether a hitch actually
    // coincided with managed collection; allocation volume alone does not establish cause.
    private static long _lastAllocatedBytes = -1;
    private static double _intervalAllocBytes;

    // Gap between consecutive spikes. A stutter that recurs on a fixed period is a TIMER,
    // not load — and this mod is full of candidate periods (0.75s tank checks, 4s cover
    // posture cache, 12s decision interval, 20s report lifetime). A clustered gap points
    // straight at whichever cadence matches; scattered gaps rule the whole class out.
    private static float _lastSpikeAt = -1f;
    private static int _intervalSpikes;

    // Managed exceptions THROWN, caught or not. Several hot paths deliberately recover
    // from disappearing native objects, so log output alone cannot show whether an
    // exception storm coincided with a hitch. This counter is installed only while the
    // probe is enabled because FirstChanceException is process-wide diagnostic overhead.
    private static long _exceptionsTotal;
    private static long _lastFrameExceptionTotal;
    private static long _intervalExceptions;
    private static bool _exceptionCounterInstalled;
    private static bool _exceptionCounterUnavailable;
    private static readonly EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs>
        ExceptionCounter = (_, _) => Interlocked.Increment(ref _exceptionsTotal);

    private static void SetExceptionCounterEnabled(bool enabled)
    {
        if (_exceptionCounterUnavailable || enabled == _exceptionCounterInstalled)
            return;

        try
        {
            if (enabled)
                AppDomain.CurrentDomain.FirstChanceException += ExceptionCounter;
            else
                AppDomain.CurrentDomain.FirstChanceException -= ExceptionCounter;
            _exceptionCounterInstalled = enabled;
        }
        catch (Exception ex)
        {
            _exceptionCounterUnavailable = true;
            Plugin.LogSource.LogWarning($"Could not change exception counter state: {ex.Message}");
        }
    }

    private static readonly int[] LastGcCounts = new int[3];
    private static readonly object? InteropObjectPoolCache = ResolveInteropObjectPoolCache();
    private static readonly PropertyInfo? InteropObjectPoolCount =
        InteropObjectPoolCache?.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);

    // Reused every frame by ModTimeProbe.Drain so the drain never allocates. Length
    // matches the site-tick buffer; the string breakdown is built only on spike frames.
    private static readonly float[] SiteMsScratch = new float[8];

    private static string BuildSiteBreakdown(float[] siteMs)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < siteMs.Length; i++)
        {
            if (siteMs[i] < 0.05f)
                continue;
            if (sb.Length > 0)
                sb.Append(',');
            sb.Append((ModTimeSite)i).Append('=').Append(siteMs[i].ToString("F1"));
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    // Integer bucket increment on the existing per-frame path: no allocation, no
    // formatting, nothing that could itself show up in the numbers it reports.
    private static void RecordFrame(float frameSeconds, float modMs, int modCalls, long allocBytes)
    {
        var frameMs = frameSeconds * 1000f;
        _intervalFrames++;
        _intervalSeconds += frameSeconds;
        _intervalModMs += modMs;
        _intervalModCalls += modCalls;
        _intervalAllocBytes += allocBytes;
        if (frameMs > _intervalWorstMs)
            _intervalWorstMs = frameMs;

        var bin = (int)(frameMs / FrameBinMs);
        if (bin < 0)
            bin = 0;
        else if (bin >= FrameBinCount)
            bin = FrameBinCount - 1;
        FrameBins[bin]++;
    }

    // Upper edge of the bin holding the requested percentile. The top bin is open-ended,
    // so a percentile landing there reports the interval's worst frame rather than a
    // meaningless "64ms" edge.
    private static float Percentile(float fraction)
    {
        var target = (int)(_intervalFrames * fraction);
        if (target >= _intervalFrames)
            target = _intervalFrames - 1;

        var seen = 0;
        for (var i = 0; i < FrameBinCount; i++)
        {
            seen += FrameBins[i];
            if (seen > target)
                return i >= FrameBinCount - 1 ? _intervalWorstMs : (i + 1) * FrameBinMs;
        }

        return _intervalWorstMs;
    }

    // Frames at or above `floorMs`, counted by whole bins so the figure never overstates.
    private static int FramesAtLeast(float floorMs)
    {
        var count = 0;
        for (var i = FrameBinCount - 1; i >= 0; i--)
        {
            if (i * FrameBinMs < floorMs)
                break;
            count += FrameBins[i];
        }

        return count;
    }

    private static void LogFrameDistribution()
    {
        if (_intervalFrames <= 0)
            return;

        var median = Percentile(0.5f);
        // A hitch is relative: four times the run's own median frame is the point where a
        // frame stops blending into the others and registers as a jolt, whether the
        // baseline is 4ms or 16ms.
        var hitchFloorMs = Mathf.Max(median * 4f, 2f);
        var hitches = FramesAtLeast(hitchFloorMs);

        var sb = new StringBuilder();
        sb.Append("Frame census: utc=").Append(DateTime.UtcNow.ToString("O"))
            .Append("; qpc=").Append(Stopwatch.GetTimestamp())
            .Append("; ").Append(_intervalFrames).Append(" frames in ")
            .Append(_intervalSeconds.ToString("F1")).Append("s (avg ")
            .Append((_intervalSeconds * 1000f / _intervalFrames).ToString("F1")).Append("ms); p50 ")
            .Append(median.ToString("F1")).Append("ms; p95 ")
            .Append(Percentile(0.95f).ToString("F1")).Append("ms; p99 ")
            .Append(Percentile(0.99f).ToString("F1")).Append("ms; p99.9 ")
            .Append(Percentile(0.999f).ToString("F1")).Append("ms; worst ")
            .Append(_intervalWorstMs.ToString("F0")).Append("ms");

        sb.Append("; >=4x median (").Append(hitchFloorMs.ToString("F1")).Append("ms) ")
            .Append(hitches).Append(" (")
            .Append((100f * hitches / _intervalFrames).ToString("F2")).Append("%)");
        sb.Append("; >16.7ms ").Append(FramesAtLeast(16.7f))
            .Append("; >33ms ").Append(FramesAtLeast(33f));

        // Mod cost stated per frame AND per call: a high per-frame figure with a low
        // per-call one is call volume, the reverse is a genuinely slow patch body.
        sb.Append("; spikes ").Append(_intervalSpikes);
        if (_intervalSpikes > 1)
        {
            sb.Append(" (every ")
                .Append((_intervalSeconds / _intervalSpikes).ToString("F1")).Append("s avg)");
        }

        sb.Append("; exceptions ").Append(_intervalExceptions);
        sb.Append("; alloc ").Append((_intervalAllocBytes / _intervalFrames / 1024d).ToString("F1"))
            .Append("KB/frame (")
            .Append((_intervalAllocBytes / 1048576d / Mathf.Max(_intervalSeconds, 0.001f)).ToString("F1"))
            .Append("MB/s)");
        sb.Append("; mod ").Append((_intervalModMs / _intervalFrames).ToString("F2"))
            .Append("ms/frame over ").Append(_intervalModCalls).Append(" calls");
        if (_intervalModCalls > 0)
        {
            sb.Append(" (").Append((_intervalModMs / _intervalModCalls).ToString("F4"))
                .Append("ms/call, ").Append(_intervalModCalls / _intervalFrames).Append("/frame)");
        }

        Plugin.LogSource.LogInfo(sb.ToString());
        ResetFrameDistribution();
    }

    private static void ResetFrameDistribution()
    {
        _intervalFrames = 0;
        _intervalSeconds = 0f;
        _intervalWorstMs = 0f;
        _intervalModMs = 0d;
        _intervalModCalls = 0;
        _intervalAllocBytes = 0d;
        _intervalSpikes = 0;
        _intervalExceptions = 0;
        Array.Clear(FrameBins, 0, FrameBins.Length);
    }

    private static void LogCacheCensus()
    {
        var il2cppUsed = IL2CPP.il2cpp_gc_get_used_size();
        var gcInfo = GC.GetGCMemoryInfo();
        Plugin.LogSource.LogInfo(
            "Cache census: il2cppHeap " + (il2cppUsed / 1048576f).ToString("F1") + "MB; " +
            "managedHeap " + (GC.GetTotalMemory(false) / 1048576f).ToString("F1") + "MB; " +
            "gcMode " + System.Runtime.GCSettings.LatencyMode +
            " server=" + System.Runtime.GCSettings.IsServerGC + "; " +
            "lastGc index=" + gcInfo.Index +
            " gen=" + gcInfo.Generation +
            " finalizers=" + gcInfo.FinalizationPendingCount +
            " pinned=" + gcInfo.PinnedObjectsCount + "; " +
            "engineGc " + IncrementalGarbageCollection.DescribeState() + "; " +
            ModTimeProbe.DescribeAllocation() + "; caches: " +
            "interopPool=" + GetInteropObjectPoolCount() +
            ",retainedInterop=" + InteropWrapperLifetime.Count +
            ",deferredInterop=" + InteropFinalizerReaper.DescribeState() +
            ",vehTrack=" + VehicleAudioBalance.TrackSourceCount +
            ",vehEngine=" + VehicleAudioBalance.EngineSourceCount +
            ",cannon=" + TankCannonAudioGain.SourceCount +
            ",flames=" + AiState.Flames.Count +
            ",contactStates=" + AiState.ContactStates.Count);
    }

    internal static void Update()
    {
        var enabled = Settings.StutterProbeEnabled.Value;
        SetExceptionCounterEnabled(enabled);
        if (!enabled)
        {
            _lastFrameAt = -1f;
            return;
        }

        // Drained every frame regardless of spike status: if this only ran on
        // spike frames, ticks accumulated by non-logged frames would carry over
        // and misattribute cost to whichever frame happens to log next.
        ModTimeProbe.Drain(
            SiteMsScratch,
            out var modMs, out var maxTacticalCallMs,
            out var modSubTop, out var modSubTopMs, out var coverGeometryRuns,
            out var modCalls, out var maxSequentialCallMs);

        var now = Time.realtimeSinceStartup;
        if (_lastFrameAt < 0f)
        {
            // Also reached when the probe is switched back on mid-session, so the
            // interval starts empty instead of blending counts from before the gap.
            _lastFrameAt = now;
            _nextCacheCensusAt = now + CacheCensusIntervalSeconds;
            ResetFrameDistribution();
            for (var generation = 0; generation < 3; generation++)
                LastGcCounts[generation] = GC.CollectionCount(generation);
            return;
        }

        var frameSeconds = now - _lastFrameAt;
        _lastFrameAt = now;

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        var allocDelta = _lastAllocatedBytes >= 0 ? allocatedBytes - _lastAllocatedBytes : 0;
        _lastAllocatedBytes = allocatedBytes;

        var exceptionsTotal = Interlocked.Read(ref _exceptionsTotal);
        var frameExceptions = exceptionsTotal - _lastFrameExceptionTotal;
        _lastFrameExceptionTotal = exceptionsTotal;
        _intervalExceptions += frameExceptions;

        RecordFrame(frameSeconds, modMs, modCalls, allocDelta);

        // Independent of spikes: a coarse-cadence census of the collections most
        // likely to grow the IL2CPP heap, so a fixed post-fix plateau is visible in
        // the field even when no frame spikes. Counts are O(1) reads; the line is
        // built at most once per CacheCensusIntervalSeconds.
        if (now >= _nextCacheCensusAt)
        {
            _nextCacheCensusAt = now + CacheCensusIntervalSeconds;
            LogFrameDistribution();
            LogCacheCensus();
        }

        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var gcDelta0 = gc0 - LastGcCounts[0];
        var gcDelta1 = gc1 - LastGcCounts[1];
        var gcDelta2 = gc2 - LastGcCounts[2];
        LastGcCounts[0] = gc0;
        LastGcCounts[1] = gc1;
        LastGcCounts[2] = gc2;

        // Tracked every frame so a spike can report how many soldiers appeared or
        // disappeared across the spike frame — a positive jump means a native
        // reinforcement wave spawned during it.
        var soldiers = 0;
        try
        {
            soldiers = Creature.aliveCreatures?.Count ?? 0;
        }
        catch (NullReferenceException)
        {
        }
        catch (Il2CppException)
        {
        }
        catch (ObjectCollectedException)
        {
        }

        var soldiersDelta = _lastSoldierCount >= 0 ? soldiers - _lastSoldierCount : 0;
        _lastSoldierCount = soldiers;

        // The game-side IL2CPP (Boehm) collector is invisible to System.GC — its
        // world-stop pauses are the one stutter source no managed counter shows.
        // A used-size DROP across a spike frame means it collected during it.
        var il2cppUsed = IL2CPP.il2cpp_gc_get_used_size();
        var il2cppDelta = _lastIl2CppUsedBytes >= 0 ? il2cppUsed - _lastIl2CppUsedBytes : 0;
        _lastIl2CppUsedBytes = il2cppUsed;

        var spike = frameSeconds >= Mathf.Max(MinimumSpikeSeconds, _smoothedFrameSeconds * SpikeFactor);
        // Spikes are excluded from the average so one hitch cannot raise the
        // baseline and hide the next one.
        if (!spike)
        {
            _smoothedFrameSeconds = Mathf.Lerp(_smoothedFrameSeconds, frameSeconds, 0.05f);
            return;
        }

        // Counted before the log cooldown so the rate reflects every spike, not just the
        // ones that got a line.
        _intervalSpikes++;
        var sinceLastSpike = _lastSpikeAt >= 0f ? now - _lastSpikeAt : -1f;
        _lastSpikeAt = now;

        if (now < _nextLogAllowedAt)
            return;
        _nextLogAllowedAt = now + LogCooldownSeconds;

        // The measured duration covers the previous frame's work, so markers
        // stamped on this frame or the one before both count as coincident.
        var frame = Time.frameCount;
        var coverSearchCoincided = frame - ContactResponse.LastCoverSearchFrame <= 1;
        var casualtyBatch = frame - CasualtySuppression.LastFlushFrame <= 1
            ? CasualtySuppression.LastFlushCount
            : 0;
        var explosions = frame - ExplosionProbeMarker.LastExplosionFrame <= 1
            ? ExplosionProbeMarker.LastExplosionCount
            : 0;
        var postureEvals = frame - ContactResponse.LastPostureEvalFrame <= 1
            ? ContactResponse.LastPostureEvalCount
            : 0;
        var staggerSkips = frame - ContactResponse.LastStaggerSkipFrame <= 1
            ? ContactResponse.LastStaggerSkipCount
            : 0;
        var managedGc = gcDelta0 + gcDelta1 + gcDelta2 > 0
            ? DescribeLatestManagedCollection()
            : "none this frame";

        Plugin.LogSource.LogInfo(
            $"Stutter probe: utc={DateTime.UtcNow:O}; qpc={Stopwatch.GetTimestamp()}; " +
            $"frame {frameSeconds * 1000f:F0}ms (recent avg {_smoothedFrameSeconds * 1000f:F1}ms); " +
            $"GC {gcDelta0}/{gcDelta1}/{gcDelta2} [{managedGc}]; " +
            $"engineGcAssist=[{IncrementalGarbageCollection.DescribeFrameAssist()}]; " +
            $"il2cppHeap {il2cppUsed / 1048576f:F1}MB (delta {il2cppDelta / 1048576f:+0.0;-0.0;0}MB); " +
            $"interopPool={GetInteropObjectPoolCount()}; retainedInterop={InteropWrapperLifetime.Count}; " +
            $"deferredInterop={InteropFinalizerReaper.DescribeState()}; " +
            $"coverSearch={coverSearchCoincided}; " +
            $"casualtyBatch={casualtyBatch}; explosions={explosions}; " +
            $"postureEvals={postureEvals}; " +
            $"geomRuns={coverGeometryRuns}; staggerSkips={staggerSkips}; " +
            $"decided={ContactResponse.LastServicedCount}/deferred={ContactResponse.LastDeniedCount}; " +
            $"soldiers={soldiers} (delta {soldiersDelta:+#;-#;0}); " +
            $"sinceLastSpike {(sinceLastSpike < 0f ? "n/a" : sinceLastSpike.ToString("F2") + "s")}; " +
            $"alloc {allocDelta / 1024f:F0}KB; exceptions {frameExceptions}; " +
            $"modMs={modMs:F1} (sites: {BuildSiteBreakdown(SiteMsScratch)}; " +
            $"maxSoldier={maxTacticalCallMs:F1}ms; maxSeq={maxSequentialCallMs:F1}ms; " +
            $"sub: {modSubTop}={modSubTopMs:F1}ms)");
    }

    private static object? ResolveInteropObjectPoolCache()
    {
        try
        {
            return typeof(Il2CppInterop.Runtime.Runtime.Il2CppObjectPool)
                .GetField("s_cache", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static int GetInteropObjectPoolCount()
    {
        try
        {
            return InteropObjectPoolCache != null && InteropObjectPoolCount != null
                ? (int)(InteropObjectPoolCount.GetValue(InteropObjectPoolCache) ?? -1)
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static string DescribeLatestManagedCollection()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            var pauses = info.PauseDurations;
            var pauseMs = 0d;
            for (var index = 0; index < pauses.Length; index++)
                pauseMs += pauses[index].TotalMilliseconds;

            return $"index={info.Index},gen={info.Generation},pause={pauseMs:F1}ms," +
                   $"finalizers={info.FinalizationPendingCount},pinned={info.PinnedObjectsCount}," +
                   $"promoted={info.PromotedBytes / 1048576d:F1}MB," +
                   $"heap={info.HeapSizeBytes / 1048576d:F1}MB," +
                   $"concurrent={info.Concurrent}";
        }
        catch (Exception ex)
        {
            return "unavailable:" + ex.GetType().Name;
        }
    }
}

[HarmonyPatch(typeof(BattleManager), "Update")]
internal static class StutterProbeUpdatePatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix() => StutterProbe.Update();
}
