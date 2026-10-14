using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ER2RealismOverhaul;

/// <summary>
/// Keeps a coordinated attacker's task and order during one native leader pass.
/// Native squad bookkeeping, individual movement, cover handling and spotting
/// still run. Calls made by mission scripts outside that pass retain their normal
/// task-selection and ordering behavior.
/// </summary>
internal static class ObjectiveAttackerNativeScope
{
    [ThreadStatic]
    private static HashSet<IntPtr>? _activeSquads;

    internal static IntPtr Enter(Squad squad)
    {
        try
        {
            var pointer = squad.Pointer;
            if (pointer == IntPtr.Zero || !ObjectiveCoordination.TryBindAttackerTask(squad))
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

    internal static bool RetainAssignedTask(Squad squad)
    {
        try
        {
            return _activeSquads != null &&
                   _activeSquads.Contains(squad.Pointer) &&
                   ObjectiveCoordination.TryBindAttackerTask(squad);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.SquadLeaderRoutine))]
internal static class ObjectiveAttackerLeaderScopePatch
{
    [HarmonyPrefix]
    private static void Prefix(Squad __instance, out IntPtr __state)
        => __state = ObjectiveAttackerNativeScope.Enter(__instance);

    [HarmonyFinalizer]
    private static void Finalizer(IntPtr __state)
        => ObjectiveAttackerNativeScope.Exit(__state);
}

[HarmonyPatch(typeof(Squad), nameof(Squad.CalculateSquadTask))]
internal static class ObjectiveAttackerTaskRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
        => !ObjectiveAttackerNativeScope.RetainAssignedTask(__instance);
}

// The native leader helper also requests faction-specific charges. Keep that
// helper active and guard only its strategic HoldArea/AttackFromSide writes;
// Charge sends units and updates its order directly, so it remains available.
[HarmonyPatch(typeof(Squad), nameof(Squad.HoldArea))]
internal static class ObjectiveAttackerHoldOrderRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
        => !ObjectiveAttackerNativeScope.RetainAssignedTask(__instance);
}

[HarmonyPatch(typeof(Squad), nameof(Squad.AttackFromSide))]
internal static class ObjectiveAttackerAttackOrderRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
        => !ObjectiveAttackerNativeScope.RetainAssignedTask(__instance);
}

[HarmonyPatch(typeof(Squad), nameof(Squad.SetHoldFireOrder))]
internal static class ObjectiveAttackerHoldFireRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance, bool setEnabled)
        // The native leader may randomly enable hold fire after a rejected
        // strategic reorder. Charge's explicit hold-fire release still runs.
        => !setEnabled || !ObjectiveAttackerNativeScope.RetainAssignedTask(__instance);
}
