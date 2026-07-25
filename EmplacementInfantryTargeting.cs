using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// AI-crewed static AT guns are natively configured with AITargets.vehicles, so
/// TurretAI.FindBestTarget never selects an on-foot soldier. This postfix gives
/// those guns a fallback: when the native pass leaves CurrentVisibleTarget null,
/// the gun engages the nearest visible enemy soldier within range. Armor keeps
/// absolute priority - the fallback only fires when the native pass produced no
/// target, so a vehicle target reclaims the gun the moment it is spotted again.
/// </summary>
internal static class StaticGunInfantryTargeting
{
    private const float ScanCooldownSeconds = 1.75f;
    private const int MaxCandidatesChecked = 4;

    private static readonly Dictionary<int, float> NextScanAt = new();

    internal static void ResetBattle() => NextScanAt.Clear();

    internal static void TryEngageInfantry(TurretAI? turretAi)
    {
        if (!Settings.StaticGunsEngageInfantry.Value || !MultiplayerAuthority.CanMutateGameplay() ||
            turretAi == null || turretAi.CurrentVisibleTarget != null ||
            turretAi.targets != AITargets.vehicles)
        {
            return;
        }

        var turret = turretAi.turret;
        if (turret == null || turret.isAA)
            return;

        var vehicle = turret.GetRootVehicle();
        if (vehicle == null || vehicle.life <= 0 || !vehicle.IsStatic())
            return;

        var gunner = turret.GetSittedUnit();
        if (gunner == null || !gunner.CanFight() || !AiOwnership.IsAutonomous(gunner))
            return;

        turret.CheckBullets(out var primary, out var armorPiercing, out var secondary, out var special);
        if (!primary && !armorPiercing && !secondary && !special)
            return;

        var instanceId = turretAi.GetInstanceID();
        var now = Time.time;
        if (NextScanAt.TryGetValue(instanceId, out var nextAt) && now < nextAt)
            return;
        NextScanAt[instanceId] = now + ScanCooldownSeconds;

        var target = FindNearestVisibleEnemySoldier(turretAi, vehicle.GetCenterOfUnit(), gunner.faction);
        if (target == null)
            return;

        try
        {
            turretAi.CurrentVisibleTarget = target;
            turret.SelectBestAmmo(target);
            AiState.Trace($"Static gun {instanceId} fell back to an infantry target");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Static gun infantry fallback failed for weapon {instanceId}: {ex.Message}");
        }
    }

    private static Spottable? FindNearestVisibleEnemySoldier(
        TurretAI turretAi, Vector3 position, string faction)
    {
        var all = Squad.AllSquads;
        if (all == null)
            return null;

        // Cheap distance prefilter over every enemy soldier, then only the
        // expensive CanSee/range checks are capped to the nearest few.
        List<(float DistanceSqr, Soldier Soldier)>? candidates = null;
        foreach (var pair in all)
        {
            var squad = pair.Value;
            if (squad == null)
                continue;

            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                if (member == null || !member.CanFight() || member.IsOnVehicle() ||
                    !ResourcesManager.IsEnemyFaction(faction, member.faction))
                {
                    continue;
                }

                var offset = member.transform.position - position;
                var distanceSqr = offset.x * offset.x + offset.z * offset.z;
                (candidates ??= new List<(float, Soldier)>()).Add((distanceSqr, member));
            }
        }

        if (candidates == null || candidates.Count == 0)
            return null;

        candidates.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));

        var checkedCount = 0;
        foreach (var candidate in candidates)
        {
            if (checkedCount >= MaxCandidatesChecked)
                break;
            checkedCount++;

            var spottable = candidate.Soldier.TryCast<Spottable>();
            if (spottable == null)
                continue;

            if (!turretAi.IsTargetPositionInRange(candidate.Soldier.transform.position) ||
                !turretAi.CanSee(spottable))
            {
                continue;
            }

            return spottable;
        }

        return null;
    }
}

[HarmonyPatch(typeof(TurretAI), nameof(TurretAI.FindBestTarget))]
internal static class StaticGunInfantryTargetingPatch
{
    [HarmonyPostfix]
    private static void Postfix(TurretAI __instance)
    {
        try
        {
            StaticGunInfantryTargeting.TryEngageInfantry(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Static gun infantry fallback postfix failed: {ex.Message}");
        }
    }
}
