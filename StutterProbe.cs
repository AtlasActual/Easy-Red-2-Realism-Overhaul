using System.Diagnostics;
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
internal enum ModSubSite
{
    CoverGeometry,       // EvaluateCoverGeometry: 12 ballistic penetration lines + 3 fire-lane casts
    MuzzleLane,          // HasNearMuzzleObstruction muzzle-clearance raycast
    Occupancy,           // CoverOccupancy.IsOccupiedByOther overlap sphere + GetComponentInParent
    SquadScan            // HasFavorableAttackAdvance O(all ContactStates) covering-fire scan
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
    private static readonly long[] SubTicksBySite = new long[4];

    // The most expensive single TacticalMove invocation (one prefix or one postfix)
    // seen this frame: a soldier-clustered spike shows up here even when the summed
    // bucket looks moderate. Reset by Drain each frame.
    private static long _maxTacticalCallTicks;

    // Every actual EvaluateCoverGeometry run this frame, first-eval and refresh alike.
    // The existing postureEvals counter only counts BUDGETED refreshes, so it reads ~0
    // during a first-evaluation wave; this counts the runs that actually happened.
    private static int _coverGeometryRuns;

    internal static long Begin() => Settings.StutterProbeEnabled.Value ? Stopwatch.GetTimestamp() : 0L;

    internal static void End(ModTimeSite site, long begin)
    {
        if (begin != 0L)
            TicksBySite[(int)site] += Stopwatch.GetTimestamp() - begin;
    }

    // TacticalMove prefix/postfix wrappers use this instead of End so the same span is
    // both summed into the bucket and compared against the per-frame single-call maximum.
    internal static void EndTacticalMove(long begin)
    {
        if (begin == 0L)
            return;

        var elapsed = Stopwatch.GetTimestamp() - begin;
        TicksBySite[(int)ModTimeSite.TacticalMove] += elapsed;
        if (elapsed > _maxTacticalCallTicks)
            _maxTacticalCallTicks = elapsed;
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
        out int coverGeometryRuns)
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
        _maxTacticalCallTicks = 0;
        _coverGeometryRuns = 0;
    }
}

/// <summary>
/// Diagnostic frame-time probe for hunting intermittent stutters. Whenever a frame
/// takes far longer than the recent average, one log line records what coincided
/// with it: managed GC collections, a commander planning tick, a detailed cover
/// search, a casualty-suppression batch, the current soldier count, and how much
/// of the frame the mod's own patches accounted for. A spike with none of those
/// markers points at native/game-side work instead of the mod.
/// Diagnostic-only; never changes AI decisions or synchronized gameplay.
/// </summary>
internal static class StutterProbe
{
    private const float MinimumSpikeSeconds = 0.07f;
    private const float SpikeFactor = 2.5f;
    private const float LogCooldownSeconds = 1f;

    private static float _lastFrameAt = -1f;
    private static float _smoothedFrameSeconds = 1f / 60f;
    private static float _nextLogAllowedAt;
    private static int _lastSoldierCount = -1;
    private static long _lastIl2CppUsedBytes = -1;
    private static readonly int[] LastGcCounts = new int[3];

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

    internal static void Update()
    {
        if (!Settings.StutterProbeEnabled.Value)
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
            out var modSubTop, out var modSubTopMs, out var coverGeometryRuns);

        var now = Time.realtimeSinceStartup;
        if (_lastFrameAt < 0f)
        {
            _lastFrameAt = now;
            for (var generation = 0; generation < 3; generation++)
                LastGcCounts[generation] = GC.CollectionCount(generation);
            return;
        }

        var frameSeconds = now - _lastFrameAt;
        _lastFrameAt = now;

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

        if (now < _nextLogAllowedAt)
            return;
        _nextLogAllowedAt = now + LogCooldownSeconds;

        // The measured duration covers the previous frame's work, so markers
        // stamped on this frame or the one before both count as coincident.
        var frame = Time.frameCount;
        var commanderTag = frame - CommanderMvp.LastPlanFrame <= 1
            ? CommanderMvp.LastPlanSideName
            : "no";
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
        var ordersIssued = frame - CommanderMvp.LastOrderExecutionFrame <= 1
            ? CommanderMvp.LastOrderExecutionCount
            : 0;
        var staggerSkips = frame - ContactResponse.LastStaggerSkipFrame <= 1
            ? ContactResponse.LastStaggerSkipCount
            : 0;

        Plugin.LogSource.LogInfo(
            $"Stutter probe: frame {frameSeconds * 1000f:F0}ms (recent avg {_smoothedFrameSeconds * 1000f:F1}ms); " +
            $"GC {gcDelta0}/{gcDelta1}/{gcDelta2}; il2cppHeap {il2cppUsed / 1048576f:F1}MB (delta {il2cppDelta / 1048576f:+0.0;-0.0;0}MB); " +
            $"commanderPlan={commanderTag}; coverSearch={coverSearchCoincided}; " +
            $"casualtyBatch={casualtyBatch}; explosions={explosions}; " +
            $"postureEvals={postureEvals}; ordersIssued={ordersIssued}; " +
            $"geomRuns={coverGeometryRuns}; staggerSkips={staggerSkips}; " +
            $"soldiers={soldiers} (delta {soldiersDelta:+#;-#;0}); " +
            $"modMs={modMs:F1} (sites: {BuildSiteBreakdown(SiteMsScratch)}; " +
            $"maxSoldier={maxTacticalCallMs:F1}ms; sub: {modSubTop}={modSubTopMs:F1}ms)");
    }
}

[HarmonyPatch(typeof(BattleManager), "Update")]
internal static class StutterProbeUpdatePatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix() => StutterProbe.Update();
}
