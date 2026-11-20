using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class CasualtySuppression
{
    private const float WoundDebounceSeconds = 1.25f;
    private static readonly Dictionary<int, float> NextWoundEventAt = new();
    private static readonly HashSet<int> ReportedDeaths = new();

    private readonly record struct Casualty(int SoldierId, Vector3 Origin, string Faction, float Radius, int Amount);
    private static List<Casualty> _pending = new();
    private static List<Casualty> _processing = new();

    internal static int LastFlushFrame = -1;
    internal static int LastFlushCount;

    internal static void ResetBattle()
    {
        NextWoundEventAt.Clear();
        ReportedDeaths.Clear();
        _pending.Clear();
        _processing.Clear();
        LastFlushFrame = -1;
        LastFlushCount = 0;
    }

    internal static void ForgetSoldier(int id)
    {
        NextWoundEventAt.Remove(id);
        ReportedDeaths.Remove(id);
    }

    internal static void ReportWound(Soldier casualty)
    {
        if (!CanReport(casualty))
            return;

        var id = casualty.GetInstanceID();
        var now = Time.time;
        if (NextWoundEventAt.TryGetValue(id, out var nextAt) && now < nextAt)
            return;

        NextWoundEventAt[id] = now + WoundDebounceSeconds;
        Enqueue(casualty, id, Settings.AiWoundSuppressionRadius.Value, Settings.AiWoundSuppressionAmount.Value);
    }

    internal static void ReportDeath(Soldier casualty)
    {
        if (!CanReport(casualty))
            return;

        var id = casualty.GetInstanceID();
        if (!ReportedDeaths.Add(id))
            return;

        NextWoundEventAt.Remove(id);
        Enqueue(casualty, id, Settings.AiDeathSuppressionRadius.Value, Settings.AiDeathSuppressionAmount.Value);
    }

    private static bool CanReport(Soldier casualty)
        => casualty != null && Settings.AiCasualtySuppressionEnabled.Value &&
           MultiplayerAuthority.CanMutateGameplay();

    private static void Enqueue(Soldier soldier, int id, float radius, int amount)
    {
        if (radius <= 0f || amount <= 0)
            return;

        try
        {
            var faction = AiState.FactionOf(soldier, id);
            if (!string.IsNullOrEmpty(faction))
                _pending.Add(new Casualty(id, soldier.GetCenterOfUnit(), faction, radius, amount));
        }
        catch (ObjectCollectedException) { }
        catch (Il2CppException) { }
    }

    internal static void FlushPending()
    {
        if (_pending.Count == 0 || _processing.Count != 0)
            return;

        // A suppression callback can report another casualty. It goes into the
        // next batch, while the current batch keeps its original order and size.
        (_processing, _pending) = (_pending, _processing);
        LastFlushFrame = Time.frameCount;
        LastFlushCount = _processing.Count;
        try
        {
            if (!Settings.AiCasualtySuppressionEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
                return;

            var creatures = Creature.aliveCreatures;
            if (creatures == null)
                return;
            foreach (var creature in creatures)
            {
                try
                {
                    var soldier = Il2CppTypeTests.As<Soldier>(creature);
                    if (soldier != null && soldier.IsAlive)
                        ApplyBatchTo(soldier);
                }
                catch (ObjectCollectedException) { }
                catch (Il2CppException) { }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"AI casualty suppression skipped a batch: {ex.Message}");
        }
        finally
        {
            _processing.Clear();
        }
    }

    private static void ApplyBatchTo(Soldier soldier)
    {
        var id = soldier.GetInstanceID();
        var faction = AiState.FactionOf(soldier, id);
        Vector3? position = null;
        foreach (var casualty in _processing)
        {
            if (id == casualty.SoldierId || !CombatSafety.SameFaction(faction, casualty.Faction))
                continue;

            position ??= soldier.GetCenterOfUnit();
            var center = position.Value;
            var dx = center.x - casualty.Origin.x;
            var dy = center.y - casualty.Origin.y;
            var dz = center.z - casualty.Origin.z;
            var distanceSquared = dx * dx + dy * dy + dz * dz;
            if (distanceSquared > casualty.Radius * casualty.Radius)
                continue;

            // Each wound and death contributes separately, including at the radius edge.
            var proximity = 1f - Mathf.Clamp01(Mathf.Sqrt(distanceSquared) / casualty.Radius);
            var amount = Mathf.RoundToInt(casualty.Amount * Mathf.Lerp(0.35f, 1f, proximity));
            if (amount > 0 && soldier.IsAlive && AiOwnership.IsAutonomous(soldier))
                IncomingFireAwareness.ApplyNonDirectionalSuppression(soldier, amount, responsible: null);
        }
    }
}

[HarmonyPatch(typeof(BattleManager), "Update")]
internal static class CasualtySuppressionDeathBatchPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            CasualtySuppression.FlushPending();
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Casualty, __t);
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Damage), new[] { typeof(float) })]
internal static class SoldierWoundSuppressionPatch
{
    private readonly struct DamageState
    {
        internal DamageState(bool wasAlive, int lifeBefore)
        {
            WasAlive = wasAlive;
            LifeBefore = lifeBefore;
        }

        internal bool WasAlive { get; }
        internal int LifeBefore { get; }
    }

    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, float dam, out DamageState __state)
    {
        var wasAlive = dam > 0f && __instance != null && __instance.IsAlive;
        var lifeBefore = wasAlive && __instance != null ? __instance.life_total.Value : 0;
        __state = new DamageState(wasAlive, lifeBefore);
    }

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance, DamageState __state)
    {
        if (__state.WasAlive && __instance != null && __instance.IsAlive &&
            __instance.life_total.Value < __state.LifeBefore)
        {
            CasualtySuppression.ReportWound(__instance);
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.Kill))]
internal static class SoldierDeathSuppressionPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance)
    {
        CasualtySuppression.ReportDeath(__instance);
    }
}
