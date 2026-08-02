using HarmonyLib;

namespace ER2RealismOverhaul;

internal static class AiBehaviorTuning
{
    internal static float AccuracySpreadMultiplier =>
        AiBehaviorTuningCore.ScaleDown(1f, Settings.AiAccuracy.Value);

    internal static float ObservationSeconds(float baseline) =>
        AiBehaviorTuningCore.ScaleDown(baseline, Settings.AiReactionSpeed.Value);

    internal static float HorizontalFov =>
        AiBehaviorTuningCore.ScaleUp(Settings.HorizontalFov.Value, Settings.AiAwareness.Value);

    internal static float PeripheralAwarenessDistance =>
        AiBehaviorTuningCore.ScaleUp(Settings.PeripheralAwarenessDistance.Value, Settings.AiAwareness.Value);

    internal static float MinimumPeripheralAwarenessDistance =>
        AiBehaviorTuningCore.ScaleUp(Settings.MinimumPeripheralAwarenessMeters.Value, Settings.AiAwareness.Value);

    internal static float TargetMemorySeconds =>
        AiBehaviorTuningCore.ScaleUp(Settings.TargetMemorySeconds.Value, Settings.AiAwareness.Value);

    internal static float EngagementHaltDistance =>
        AiBehaviorTuningCore.ScaleUp(
            Settings.ContactEngagementHaltDistance.Value,
            Settings.AiAwareness.Value);

    internal static float ImmediateFireDistance =>
        AiBehaviorTuningCore.ScaleUp(Settings.ContactImmediateFireDistance.Value, Settings.AiAggressiveness.Value);

    internal static float MaximumAttackCombatHaltSeconds =>
        AiBehaviorTuningCore.ScaleDown(Settings.MaximumAttackCombatHaltSeconds.Value, Settings.AiAggressiveness.Value);

    internal static float AttackFiringHoldSeconds =>
        CombatMovementPolicyCore.ResolveAttackFiringHoldSeconds(Settings.AttackFiringHoldSeconds.Value);

    internal static float GrenadeCooldownSeconds =>
        AiBehaviorTuningCore.ScaleDown(Settings.GrenadeCooldownSeconds.Value, Settings.AiAggressiveness.Value);

    internal static int CrouchSuppressionThreshold =>
        AiBehaviorTuningCore.ScaleThreshold(
            Settings.CrouchSuppression.Value,
            Settings.AiSuppressionResistance.Value,
            1,
            AiBehaviorTuningCore.NativeMaximumSuppression - 1);

    internal static int CrouchSuppressionReleaseThreshold =>
        AiBehaviorTuningCore.ScaleThreshold(
            Settings.CrouchSuppressionRelease.Value,
            Settings.AiSuppressionResistance.Value,
            0,
            Math.Max(0, CrouchSuppressionThreshold - 1));

    internal static int ProneSuppressionThreshold =>
        AiBehaviorTuningCore.ScaleThreshold(
            Settings.ProneSuppression.Value,
            Settings.AiSuppressionResistance.Value,
            2,
            AiBehaviorTuningCore.NativeMaximumSuppression);

    internal static int ProneReleaseSuppressionThreshold =>
        AiBehaviorTuningCore.ScaleThreshold(
            Settings.ProneReleaseSuppression.Value,
            Settings.AiSuppressionResistance.Value,
            1,
            AiBehaviorTuningCore.NativeMaximumSuppression - 1);
}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.ProcessAiAccuracy))]
internal static class AiAccuracyTuningPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier user, ref float __result)
    {
        if (__result <= 0f ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            !AiOwnership.IsAutonomous(user))
            return;

        __result *= AiBehaviorTuning.AccuracySpreadMultiplier;
    }
}

[HarmonyPatch(typeof(SoldierAI), nameof(SoldierAI.ProcessAiAccuracy))]
internal static class AiAntiTankLauncherAccuracyPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier user, ref float __result)
    {
        if (__result <= 0f ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            !AiOwnership.IsAutonomous(user))
        {
            return;
        }

        try
        {
            var gun = user.GetHeldGun();
            if (gun != null && HandheldWeaponClassifier.IsAntiTankLauncher(gun))
                __result *= Settings.LauncherAccuracySpreadMultiplier.Value;
        }
        catch
        {
            // Unrecognized or transient weapons keep the game's normal AI spread.
        }
    }
}
