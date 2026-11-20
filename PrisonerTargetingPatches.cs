using HarmonyLib;

namespace ER2RealismOverhaul;

internal static class PrisonerTargeting
{
    internal static bool IsPrisoner(Spottable? target)
        => target != null && !target.WasCollected && target.TryCast<Soldier>()?.HasSurrended() == true;

    internal static void Forget(Soldier prisoner)
    {
        var token = prisoner.Pointer;
        foreach (var memory in AiState.TargetMemory.Values)
        {
            TargetAcquisition.Forget(memory, token);
            if (memory.ConfirmedLastKnownTargetToken == token)
            {
                memory.HasConfirmedLastKnownPosition = false;
                memory.ConfirmedLastKnownTargetToken = IntPtr.Zero;
            }
            if (memory.IncomingFireShooterToken == token)
                IncomingFireAwareness.Clear(memory);
            if (memory.ReportedTargetToken == token)
                memory.ReportedTargetToken = IntPtr.Zero;
        }

        // Drop existing locks and release a held trigger at the transition,
        // without waiting for the next visibility scan or reaction timer.
        var roster = Creature.aliveCreatures;
        if (roster == null)
            return;
        for (var i = 0; i < roster.Count; i++)
        {
            var soldier = roster[i]?.TryCast<Soldier>();
            if (soldier == null || !soldier.IsAI())
                continue;
            var ai = soldier.aiController;
            bool visible = ai?.visibleTarget?.Pointer == token;
            bool current = soldier.CurrentVisibleTarget?.Pointer == token;
            if (visible) ai!.visibleTarget = null;
            if (current) soldier.SetBestVisibleEnemy(null);
            if (visible || current) soldier.SetGunTriggerPressed(false);
        }
    }
}

[HarmonyPatch(typeof(TurretAI), nameof(TurretAI.GetCalculatedBestTarget))]
internal static class TurretPrisonerSelectionPatch
{
    private static void Postfix(ref Spottable __result)
    {
        if (PrisonerTargeting.IsPrisoner(__result))
            __result = null!;
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Surrender))]
internal static class PrisonerSurrenderPatch
{
    private static void Prefix(Soldier __instance, out bool __state)
        => __state = __instance.HasSurrended();

    private static void Postfix(Soldier __instance, bool __state)
    {
        if (!__state && MultiplayerAuthority.CanMutateGameplay() && __instance.HasSurrended())
            PrisonerTargeting.Forget(__instance);
    }
}

[HarmonyPatch(typeof(TurretAI), nameof(TurretAI.CanSee))]
internal static class TurretPrisonerVisibilityPatch
{
    private static bool Prefix(Spottable __0, ref bool __result)
    {
        if (!PrisonerTargeting.IsPrisoner(__0)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(TurretAI), nameof(TurretAI.IsFireTime))]
internal static class TurretPrisonerFirePatch
{
    private static bool Prefix(TurretAI __instance, ref bool __result)
    {
        if (!PrisonerTargeting.IsPrisoner(__instance.CurrentVisibleTarget)) return true;
        __instance.CurrentVisibleTarget = null;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Melee))]
internal static class PrisonerMeleeStartPatch
{
    private static bool Prefix(Soldier __instance)
        => !__instance.IsAI() || !PrisonerTargeting.IsPrisoner(__instance.CurrentVisibleTarget);
}
