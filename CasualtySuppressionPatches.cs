using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class CasualtySuppression
{
    private const float WoundDebounceSeconds = 1.25f;
    private static readonly Dictionary<int, float> NextWoundEventAt = new();
    private static readonly HashSet<int> ReportedDeaths = new();

    // Deaths are batched: a mass-casualty frame (e.g. an artillery strike) used to
    // run one full aliveCreatures scan + List allocation per dead soldier. All
    // deaths reported this frame are queued here and resolved in one scan by
    // FlushPendingDeaths, called once per frame from the BattleManager.Update
    // postfix below.
    private static readonly List<PendingDeath> PendingDeaths = new();

    // Stutter-probe markers: the frame of the last death-batch flush and how many
    // deaths it resolved. Diagnostic-only.
    internal static int LastFlushFrame = int.MinValue;
    internal static int LastFlushCount;

    private readonly struct PendingDeath
    {
        internal PendingDeath(Soldier casualty, Vector3 origin, string faction, float radius, int maximumAmount)
        {
            Casualty = casualty;
            Origin = origin;
            Faction = faction;
            Radius = radius;
            MaximumAmount = maximumAmount;
        }

        internal Soldier Casualty { get; }
        internal Vector3 Origin { get; }
        internal string Faction { get; }
        internal float Radius { get; }
        internal int MaximumAmount { get; }
    }

    internal static void ResetBattle()
    {
        NextWoundEventAt.Clear();
        ReportedDeaths.Clear();
        PendingDeaths.Clear();
    }

    internal static void ReportWound(Soldier casualty)
    {
        if (!CanReport(casualty))
            return;

        var casualtyId = casualty.GetInstanceID();
        var now = Time.time;
        if (NextWoundEventAt.TryGetValue(casualtyId, out var nextAt) && now < nextAt)
            return;

        NextWoundEventAt[casualtyId] = now + WoundDebounceSeconds;
        ApplyNearby(
            casualty,
            Settings.AiWoundSuppressionRadius.Value,
            Settings.AiWoundSuppressionAmount.Value,
            "wound");
    }

    internal static void ReportDeath(Soldier casualty)
    {
        if (!CanReport(casualty))
            return;

        var casualtyId = casualty.GetInstanceID();
        if (!ReportedDeaths.Add(casualtyId))
            return;

        NextWoundEventAt.Remove(casualtyId);

        var radius = Settings.AiDeathSuppressionRadius.Value;
        var maximumAmount = Settings.AiDeathSuppressionAmount.Value;
        if (radius <= 0f || maximumAmount <= 0)
            return;

        try
        {
            PendingDeaths.Add(new PendingDeath(
                casualty, casualty.GetCenterOfUnit(), casualty.faction, radius, maximumAmount));
        }
        catch (ObjectCollectedException)
        {
            // The casualty despawned before its position could be captured; no
            // retry is needed because suppression is intentionally a one-shot
            // reaction.
        }
    }

    /// <summary>
    /// Resolves every death queued this frame in a single aliveCreatures scan.
    /// A recipient near several simultaneous deaths still receives each death's
    /// own distance-scaled contribution, evaluated inside this one pass.
    /// </summary>
    internal static void FlushPendingDeaths()
    {
        if (PendingDeaths.Count == 0)
            return;

        var deaths = PendingDeaths.ToArray();
        PendingDeaths.Clear();
        LastFlushFrame = Time.frameCount;
        LastFlushCount = deaths.Length;

        try
        {
            var creatures = Creature.aliveCreatures;
            if (creatures == null)
                return;

            var affected = 0;
            foreach (var creature in creatures)
            {
                var soldier = creature as Soldier;
                if (soldier == null || !soldier.IsAlive || !AiOwnership.IsAutonomous(soldier))
                    continue;

                Vector3? position = null;
                var soldierAffected = false;
                // Invariant across the death loop, but it was being re-marshalled from
                // il2cpp once per death per creature — on a mass-casualty flush with the
                // battle's whole alive list that is the dominant cost of this pass, and
                // the frame it landed on allocated 62KB against a 3-6KB baseline.
                var soldierFaction = soldier.faction;
                foreach (var death in deaths)
                {
                    if (soldier == death.Casualty ||
                        !CombatSafety.SameFaction(soldierFaction, death.Faction))
                    {
                        continue;
                    }

                    position ??= soldier.GetCenterOfUnit();
                    var distanceSqr = (position.Value - death.Origin).sqrMagnitude;
                    var radiusSqr = death.Radius * death.Radius;
                    if (distanceSqr > radiusSqr)
                        continue;

                    // Retain a restrained 35% shock at the edge, then rise smoothly
                    // toward the configured maximum beside the casualty.
                    var proximity = 1f - Mathf.Clamp01(Mathf.Sqrt(distanceSqr) / death.Radius);
                    var amount = Mathf.RoundToInt(death.MaximumAmount * Mathf.Lerp(0.35f, 1f, proximity));
                    if (amount <= 0 || !soldier.IsAlive || !AiOwnership.IsAutonomous(soldier))
                        continue;

                    IncomingFireAwareness.ApplyNonDirectionalSuppression(
                        soldier, amount, responsible: null);
                    soldierAffected = true;
                }

                if (soldierAffected)
                    affected++;
            }

            if (affected > 0)
            {
                AiState.Trace(
                    $"AI casualty suppression: death batch of {deaths.Length} affected {affected} allied AI");
            }
        }
        catch (ObjectCollectedException)
        {
            // A casualty or recipient despawned during the event; no retry is
            // needed because suppression is intentionally a one-shot reaction.
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"AI casualty suppression skipped a death batch: {ex.Message}");
        }
    }

    private static bool CanReport(Soldier casualty)
        => casualty != null &&
           Settings.AiCasualtySuppressionEnabled.Value &&
           MultiplayerAuthority.CanMutateGameplay() &&
           !string.IsNullOrEmpty(casualty.faction);

    private static void ApplyNearby(Soldier casualty, float radius, int maximumAmount, string eventName)
    {
        if (radius <= 0f || maximumAmount <= 0)
            return;

        try
        {
            var creatures = Creature.aliveCreatures;
            if (creatures == null)
                return;

            var origin = casualty.GetCenterOfUnit();
            var radiusSqr = radius * radius;
            var recipients = new List<(Soldier Soldier, float Distance)>();

            foreach (var creature in creatures)
            {
                var soldier = creature as Soldier;
                if (soldier == null || soldier == casualty || !soldier.IsAlive ||
                    !AiOwnership.IsAutonomous(soldier) ||
                    !CombatSafety.SameFaction(soldier.faction, casualty.faction))
                {
                    continue;
                }

                var distanceSqr = (soldier.GetCenterOfUnit() - origin).sqrMagnitude;
                if (distanceSqr <= radiusSqr)
                    recipients.Add((soldier, Mathf.Sqrt(distanceSqr)));
            }

            foreach (var recipient in recipients)
            {
                if (recipient.Soldier == null || !recipient.Soldier.IsAlive ||
                    !AiOwnership.IsAutonomous(recipient.Soldier))
                {
                    continue;
                }

                // Retain a restrained 35% shock at the edge, then rise smoothly
                // toward the configured maximum beside the casualty.
                var proximity = 1f - Mathf.Clamp01(recipient.Distance / radius);
                var amount = Mathf.RoundToInt(maximumAmount * Mathf.Lerp(0.35f, 1f, proximity));
                if (amount > 0)
                {
                    IncomingFireAwareness.ApplyNonDirectionalSuppression(
                        recipient.Soldier,
                        amount,
                        responsible: null);
                }
            }

            if (recipients.Count > 0)
            {
                AiState.Trace(
                    $"AI casualty suppression: {eventName} affected {recipients.Count} allied AI within {radius:0.#}m");
            }
        }
        catch (ObjectCollectedException)
        {
            // A casualty or recipient despawned during the event; no retry is
            // needed because suppression is intentionally a one-shot reaction.
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"AI casualty suppression skipped an event: {ex.Message}");
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
            CasualtySuppression.FlushPendingDeaths();
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
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
