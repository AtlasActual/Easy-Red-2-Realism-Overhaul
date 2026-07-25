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

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.UseBestWearedWeaponCheck))]
internal static class SoldierAiLauncherSelectionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(SoldierAI __instance)
    {
        try
        {
            if (!MultiplayerAuthority.CanMutateGameplay())
                return true;

            var soldier = __instance.GetSoldier();
            if (!AiOwnership.IsAutonomous(soldier))
                return true;

            var alternateGun = soldier.GetHeldGun(1);
            return alternateGun == null ||
                   !HandheldWeaponClassifier.ShouldDeferLauncherSelection(
                       soldier, __instance, alternateGun);
        }
        catch
        {
            // Preserve the base game's weapon selection for unknown or modded loadouts.
            return true;
        }
    }
}

internal static class HandheldWeaponClassifier
{
    private const int MaximumLauncherMuzzleVelocity = 300;

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

            var target = TargetAcquisition.GetUsableAiTarget(ai) ??
                         TargetAcquisition.GetUsableSoldierTarget(soldier);
            var hasTarget = TargetAcquisition.TryGetTargetSnapshot(
                target, out _, out var targetPosition);
            var distance = 0f;
            if (hasTarget)
            {
                var offset = targetPosition - soldier.transform.position;
                offset.y = 0f;
                distance = offset.magnitude;
            }

            var rifleBand = Settings.RifleMovingFireMaxDistance.Value;
            return MovingFireCore.Allows(
                true,
                ClassifyForMovingFire(gun, rifleBand),
                hasTarget,
                distance,
                Settings.AutomaticMovingFireMaxDistance.Value,
                rifleBand);
        }
        catch
        {
            return false;
        }
    }

    // SMGs are close-assault weapons intended for controllable fire while moving. A
    // machine gunner or launcher operator instead halts and braces the heavier weapon.
    // Rifles have their own opt-in band - see MovingFireCore.
    private static MovingFireWeapon ClassifyForMovingFire(GenericGun gun, float rifleBandMeters)
    {
        if (IsSubmachineGun(gun))
            return MovingFireWeapon.SubmachineGun;

        // With no rifle band configured (the vision.md default) every non-SMG halts, so
        // the interop-heavy machine-gun/launcher distinction is not worth computing on
        // the per-frame moving-fire path.
        if (!(rifleBandMeters > 0f))
            return MovingFireWeapon.MachineGun;

        if (IsAntiTankLauncher(gun))
            return MovingFireWeapon.Launcher;
        return IsMachineGun(gun, true) ? MovingFireWeapon.MachineGun : MovingFireWeapon.Rifle;
    }

    internal static void EnforceEngagementRange(Soldier soldier, SoldierAI ai)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        try
        {
            var gun = soldier.GetHeldGun();
            if (gun == null || !TryGetMaximumEngagementDistance(gun, out var maximumDistance))
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
            if (offset.sqrMagnitude <= maximumDistance * maximumDistance)
            {
                ReleaseRangeInhibition(state, soldier, ai);
                return;
            }

            // The flag is the arbiter's rank e input; ApplyFireDecision writes the
            // permission and clears targetInWeaponRange.
            state.FireInhibitedByRange = true;
            GroundAiDirector.ExecuteSoldierStopFire(soldier);
        }
        catch
        {
            // Unknown or modded weapons default to the base game's behavior.
            ReleaseRangeInhibition(state, soldier, ai);
        }
    }

    internal static bool ShouldDeferLauncherSelection(
        Soldier soldier,
        SoldierAI ai,
        GenericGun candidateGun)
    {
        var isLauncher = IsAntiTankLauncher(candidateGun);
        var target = TargetAcquisition.ResolveObservedTarget(ai, soldier);
        var hasTarget = TargetAcquisition.TryGetTargetSnapshot(
            target, out _, out var targetPosition);
        if (!hasTarget)
        {
            return LauncherSelectionDecisionCore.ShouldDeferSelection(
                isLauncher, false, 0f,
                Settings.LauncherMaximumEngagementDistance.Value);
        }

        var offset = targetPosition - soldier.transform.position;
        offset.y = 0f;
        return LauncherSelectionDecisionCore.ShouldDeferSelection(
            isLauncher,
            true,
            offset.magnitude,
            Settings.LauncherMaximumEngagementDistance.Value);
    }

    private static void ReleaseRangeInhibition(
        ContactResponseState state,
        Soldier soldier,
        SoldierAI ai)
    {
        if (!state.FireInhibitedByRange)
            return;

        if (!state.FireInhibitedByArmoredTarget)
            ai.targetInWeaponRange = true;
        state.FireInhibitedByRange = false;
    }

    private static bool TryGetMaximumEngagementDistance(
        GenericGun gun,
        out float maximumDistance)
    {
        if (IsSubmachineGun(gun))
        {
            maximumDistance = Settings.SmgMaximumEngagementDistance.Value;
            return true;
        }

        if (IsAntiTankLauncher(gun))
        {
            maximumDistance = Settings.LauncherMaximumEngagementDistance.Value;
            return true;
        }

        maximumDistance = 0f;
        return false;
    }

    private static bool IsAntiTankLauncher(GenericGun gun)
    {
        // The game exposes AP capability but does not distinguish launchers from
        // anti-tank rifles. Muzzle velocity supplies that missing distinction:
        // rockets and spigot/rifle grenades are low velocity, while AT rifles are not.
        if (!gun.IsAtWeapon())
            return false;

        var muzzleVelocity = gun.ActualMuzzleVelocity;
        return muzzleVelocity > 0 && muzzleVelocity <= MaximumLauncherMuzzleVelocity;
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
        GroundAiDirector.ExecuteSoldierStopFire(soldier);
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
    }
}
