using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class CasualtySuppression
{
    private const float WoundDebounceSeconds = 1.25f;
    private static readonly Dictionary<int, float> NextWoundEventAt = new();
    private static readonly HashSet<int> ReportedDeaths = new();

    internal static void ResetBattle()
    {
        NextWoundEventAt.Clear();
        ReportedDeaths.Clear();
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
        ApplyNearby(
            casualty,
            Settings.AiDeathSuppressionRadius.Value,
            Settings.AiDeathSuppressionAmount.Value,
            "death");
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
