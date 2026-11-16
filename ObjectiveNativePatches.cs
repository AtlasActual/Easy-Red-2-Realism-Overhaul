using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ER2RealismOverhaul;

/// <summary>
/// Keeps a coordinated squad's task and order during one native leader pass.
/// Native squad bookkeeping, individual movement, cover handling and spotting
/// still run. Calls made by mission scripts outside that pass retain their normal
/// task-selection and ordering behavior.
/// </summary>
internal static class ObjectiveNativeScope
{
    [ThreadStatic]
    private static HashSet<IntPtr>? _activeSquads;

    internal static IntPtr Enter(Squad squad)
    {
        try
        {
            var pointer = squad.Pointer;
            if (pointer == IntPtr.Zero || !ObjectiveCoordination.TryBindAssignedTask(squad))
                return IntPtr.Zero;

            _activeSquads ??= new HashSet<IntPtr>();
            // A nested pass for the same squad leaves the outer pass's entry in
            // place; only the invocation that added it may remove it.
            return _activeSquads.Add(pointer) ? pointer : IntPtr.Zero;
        }
        catch (Exception)
        {
            // A collected interop object or expired assignment gives control
            // straight back to the native planner.
            return IntPtr.Zero;
        }
    }

    internal static void Exit(IntPtr enteredPointer)
    {
        if (enteredPointer != IntPtr.Zero)
            _activeSquads?.Remove(enteredPointer);
    }

    internal static bool IsInLeaderPass(Squad squad)
    {
        try
        {
            return _activeSquads != null && _activeSquads.Contains(squad.Pointer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool RetainAssignedTask(Squad squad)
    {
        try
        {
            return _activeSquads != null &&
                   _activeSquads.Contains(squad.Pointer) &&
                   ObjectiveCoordination.TryBindAssignedTask(squad);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.SquadLeaderRoutine))]
internal static class ObjectiveLeaderScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(Squad __instance, out IntPtr __state)
        => __state = ObjectiveNativeScope.Enter(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(IntPtr __state)
        => ObjectiveNativeScope.Exit(__state);
}

[HarmonyPatch(typeof(Squad), nameof(Squad.CalculateSquadTask))]
internal static class ObjectiveTaskRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
        => !ObjectiveNativeScope.RetainAssignedTask(__instance);
}

// The native leader helper also requests faction-specific charges. Keep that
// helper active and guard only its strategic HoldArea/AttackFromSide writes;
// Charge sends units and updates its order directly, so it remains available.
[HarmonyPatch(typeof(Squad), nameof(Squad.HoldArea))]
internal static class ObjectiveHoldOrderRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
        => !ObjectiveNativeScope.RetainAssignedTask(__instance);
}

[HarmonyPatch(typeof(Squad), nameof(Squad.AttackFromSide))]
internal static class ObjectiveAttackOrderRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
        => !ObjectiveNativeScope.RetainAssignedTask(__instance);
}

[HarmonyPatch(typeof(Squad), nameof(Squad.SetHoldFireOrder))]
internal static class ObjectiveHoldFireRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance, bool setEnabled)
        // The native leader may randomly enable hold fire after a rejected
        // strategic reorder. Charge's explicit hold-fire release still runs.
        => !setEnabled || !ObjectiveNativeScope.RetainAssignedTask(__instance);
}

/// <summary>
/// The native leader pass turns a coordinated attack order into a charge, which
/// heads straight in and undoes a flanking squad's detour until the next plan pass
/// restores it. Only while a squad is on its flank staging leg, and only inside its
/// native leader pass, is that charge declined. Charges by the direct-attack squad,
/// by flankers already released onto the objective, and from mission scripts run
/// unchanged.
/// </summary>
[HarmonyPatch(typeof(Squad), nameof(Squad.Charge))]
internal static class FlankStagingChargePatch
{
    private static readonly Dictionary<int, float> NextTraceAt = new();

    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
    {
        try
        {
            if (!ObjectiveCoordination.IsOnFlankStagingLeg(__instance, out var squadId))
                return true;

            var inLeaderPass = ObjectiveNativeScope.IsInLeaderPass(__instance);
            if (AiState.TraceEnabled)
            {
                var now = UnityEngine.Time.time;
                if (!NextTraceAt.TryGetValue(squadId, out var next) || now >= next)
                {
                    NextTraceAt[squadId] = now + 10f;
                    AiState.Trace(inLeaderPass
                        ? $"Flank staging: squad {squadId} native charge declined until released"
                        : $"Flank staging: squad {squadId} charge requested outside the leader pass; allowed");
                }
            }

            return !inLeaderPass;
        }
        catch (Exception)
        {
            return true;
        }
    }

    internal static void ResetBattle() => NextTraceAt.Clear();
}
