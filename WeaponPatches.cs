using HarmonyLib;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(GenericGun), nameof(GenericGun.UseTracers))]
internal static class GenericGunTracerPatch
{
    [HarmonyPostfix]
    private static void Postfix(GenericGun __instance, ref bool __result)
    {
        if (!__result || !Settings.TracerReductionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        if (!HandheldWeaponClassifier.IsMachineGun(__instance, true))
        {
            __result = false;
            return;
        }

        if (UnityEngine.Random.value > Settings.MachineGunTracerRetention.Value)
            __result = false;
    }

}

internal static class HandheldWeaponClassifier
{
    private static readonly string[] MachineGunIds =
    {
        "machinegun", "machine_gun", "heavy_mg", "light_mg", "_hmg", "_lmg",
        "mg34", "mg42", "bren", "m1918", "browning_automatic", "dp28", "dp_28",
        "zb26", "zb_26", "type96", "type_96", "type99", "type_99", "madsen",
        "chauchat", "lewis", "vickers", "maxim", "m1919", "fm24", "fm_24",
        "breda_m30", "bredam30"
    };

    private static readonly string[] SubmachineGunIds =
    {
        "smg", "submachine", "sub_machine", "mp38", "mp40", "mp41",
        "mab38", "beretta38", "beretta_38", "thompson", "ppsh", "pps43",
        "pps_43", "sten", "suomi", "type100", "type_100", "mas38", "mas_38",
        "owen", "reising", "mp34", "mp_34", "zk383", "zk_383", "fnab",
        "greasegun", "grease_gun", "m3_grease", "erma_emp", "emp35"
    };

    internal static bool IsMachineGun(GenericGun gun, bool allowHolderRoleFallback)
    {
        if (gun.IsHeavyMg() ||
            gun.GetComponent<AutomaticGunWithAmmoBelt>() != null ||
            gun.GetComponent<Type11LmgGun>() != null)
        {
            return true;
        }

        var id = (gun.item_id ?? string.Empty).ToLowerInvariant();
        foreach (var marker in MachineGunIds)
        {
            if (id.Contains(marker))
                return true;
        }

        if (!allowHolderRoleFallback)
            return false;

        var holder = gun.GetComponentInParent<Soldier>();
        if (holder == null || (!holder.hasMg && !holder.IsGunner()))
            return false;

        var heldGun = holder.GetHeldGun();
        return heldGun != null && heldGun.GetInstanceID() == gun.GetInstanceID();
    }

    internal static bool AllowsMovingFire(Soldier soldier, SoldierAI ai)
    {
        if (!Settings.MovingFireRestrictionEnabled.Value)
            return true;

        try
        {
            var gun = soldier.GetHeldGun();
            if (gun == null)
                return false;

            // SMGs are close-assault weapons intended for controllable fire while
            // moving. A machine gunner instead halts and braces the heavier weapon.
            if (!IsSubmachineGun(gun))
                return false;

            var target = TargetAcquisition.GetUsableAiTarget(ai) ??
                         TargetAcquisition.GetUsableSoldierTarget(soldier);
            if (!TargetAcquisition.TryGetTargetSnapshot(target, out _, out var targetPosition))
                return false;

            var offset = targetPosition - soldier.transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= Settings.AutomaticMovingFireMaxDistance.Value *
                Settings.AutomaticMovingFireMaxDistance.Value;
        }
        catch
        {
            return false;
        }
    }

    internal static void EnforceEngagementRange(Soldier soldier, SoldierAI ai)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        try
        {
            var gun = soldier.GetHeldGun();
            if (gun == null || !IsSubmachineGun(gun))
            {
                ReleaseRangeInhibition(state, soldier, ai);
                return;
            }

            var target = TargetAcquisition.GetUsableAiTarget(ai) ??
                         TargetAcquisition.GetUsableSoldierTarget(soldier);
            if (!TargetAcquisition.TryGetTargetSnapshot(target, out _, out var targetPosition))
            {
                ReleaseRangeInhibition(state, soldier, ai);
                return;
            }

            var offset = targetPosition - soldier.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= Settings.SmgMaximumEngagementDistance.Value *
                Settings.SmgMaximumEngagementDistance.Value)
            {
                ReleaseRangeInhibition(state, soldier, ai);
                return;
            }

            state.FireInhibitedByRange = true;
            state.FireRestorePending = true;
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
            ai.targetInWeaponRange = false;
            soldier.StopFire();
        }
        catch
        {
            // Unknown or modded weapons default to the base game's behavior.
            ReleaseRangeInhibition(state, soldier, ai);
        }
    }

    private static void ReleaseRangeInhibition(
        ContactResponseState state,
        Soldier soldier,
        SoldierAI ai)
    {
        if (!state.FireInhibitedByRange)
            return;

        ai.targetInWeaponRange = true;
        state.FireInhibitedByRange = false;
        ContactResponse.RestoreFireAfterOwnedInhibition(ai, soldier);
    }

    private static bool IsSubmachineGun(GenericGun gun)
    {
        if (!gun.IsFullAuto())
            return false;

        var id = (gun.item_id ?? string.Empty).ToLowerInvariant();
        foreach (var marker in SubmachineGunIds)
        {
            if (id.Contains(marker))
                return true;
        }

        return false;
    }
}

internal static class InfantryAntiArmorFireDiscipline
{
    internal static void Update(SoldierAI ai, Soldier soldier)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        GenericGun? gun;
        try
        {
            gun = soldier.GetHeldGun();
        }
        catch
        {
            Release(ai, soldier, state);
            return;
        }

        if (gun == null || !ShouldWithhold(soldier, gun, ai))
        {
            Release(ai, soldier, state);
            return;
        }

        var newlyInhibited = !state.FireInhibitedByArmoredTarget;
        state.FireInhibitedByArmoredTarget = true;
        state.FireRestorePending = true;
        ai.allowFireAtEnemy = false;
        ai.aimingEnemy = false;
        ai.targetInWeaponRange = false;
        soldier.StopFire();
        if (newlyInhibited)
            AiState.Trace($"Anti-armor discipline: soldier {soldier.GetInstanceID()} withholding ineffective small-arms fire");
    }

    internal static bool ShouldWithhold(
        Soldier soldier,
        GenericGun gun,
        SoldierAI? ai = null)
    {
        try
        {
            var target = ai != null
                ? TargetAcquisition.ResolveObservedTarget(ai, soldier)
                : soldier.GetCurrentBestVisibleEnemy();
            if (!IsLiveEnemyTank(soldier, target))
                return false;

            // Use the game's own armor-ammunition classification. This preserves
            // AT rifles, launchers, and other genuinely capable ammunition while
            // rejecting ordinary rifle, SMG, and machine-gun rounds.
            var bullet = gun.GetBulletData();
            return bullet != null && !bullet.IsAPullet();
        }
        catch
        {
            // Unknown modded weapons retain native behavior rather than risk
            // suppressing a legitimate anti-tank shot.
            return false;
        }
    }

    private static bool IsLiveEnemyTank(Soldier shooter, Spottable? target)
    {
        if (!TargetAcquisition.IsUsableTarget(target))
            return false;

        var vehicle = target!.TryCast<Vehicle>();
        return vehicle != null && vehicle.life > 0 &&
               vehicle.GetComponent<VehicleTank>() != null &&
               !CombatSafety.SameFaction(vehicle.GetVehicleFaction(), shooter.faction);
    }

    private static void Release(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state)
    {
        if (!state.FireInhibitedByArmoredTarget)
            return;

        state.FireInhibitedByArmoredTarget = false;
        if (!state.FireInhibitedByRange)
            ai.targetInWeaponRange = true;
        ContactResponse.RestoreFireAfterOwnedInhibition(ai, soldier);
    }
}
