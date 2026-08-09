using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class PlayerAimingInput
{
    internal static bool IsHoldBreathHeld()
    {
        try
        {
            var gamepad = GamepadsAPI.GetGamepad();
            return gamepad != null && gamepad.GetButtonHeld(GameInput.HoldBreath, 0.01f);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Accumulates unsupported aim fatigue for the local player and feeds it into
/// the game's native exhausted-aim sway instead of replacing weapon motion.
/// </summary>
[HarmonyPatch(typeof(Soldier), nameof(Soldier.Update))]
internal static class PlayerAimFatiguePatch
{
    private const float NativeStaminaEmptyThreshold = 1f;
    private const float NativeStaminaRecoveryThreshold = 50f;

    private static int _trackedSoldierId;
    private static float _fatigueSeconds;
    private static bool _ownsExhaustion;
    private static string _lastErrorSignature = string.Empty;

    [HarmonyPostfix]
    private static void Postfix(Soldier __instance)
    {
        try
        {
            var player = Soldier.CurrentControlledSoldierOrNull();
            if (player == null || __instance.GetInstanceID() != player.GetInstanceID())
                return;

            var soldierId = player.GetInstanceID();
            if (_trackedSoldierId != soldierId)
            {
                _trackedSoldierId = soldierId;
                _fatigueSeconds = 0f;
                _ownsExhaustion = false;
            }

            if (!Settings.RealisticAimFatigueEnabled.Value || !player.IsAlive)
            {
                ReleaseOwnedExhaustion(player);
                _fatigueSeconds = 0f;
                return;
            }

            var baseThreshold = Mathf.Max(0.1f, Settings.UnsupportedAimFatigueSeconds.Value);
            var threshold = player.Pose == SoldierPose.Crouch
                ? baseThreshold * 1.5f
                : baseThreshold;
            var supported = player.Pose == SoldierPose.Prone ||
                            PlayerAimingInput.IsHoldBreathHeld();

            if (player.IsAiming && !supported)
            {
                var staminaPressure = player.staminaCount <= 15f ? 1.5f : 1f;
                _fatigueSeconds = Mathf.Min(threshold * 1.25f,
                    _fatigueSeconds + Time.deltaTime * staminaPressure);
            }
            else
            {
                var recoveryRate = player.IsAiming ? 1f : 2.25f;
                _fatigueSeconds = Mathf.Max(0f, _fatigueSeconds - Time.deltaTime * recoveryRate);
            }

            // Native stamina exhaustion owns this flag once stamina is empty.
            // Do not claim it here or the mod can later clear/reassert native state.
            if (player.staminaCount <= NativeStaminaEmptyThreshold)
            {
                _ownsExhaustion = false;
                return;
            }

            var fatigueShouldExhaust = !supported && _fatigueSeconds >= threshold;
            if (fatigueShouldExhaust)
            {
                // If native stamina already owns exhaustion, leave it alone.
                if (_ownsExhaustion || !player.out_of_stamina)
                {
                    player.out_of_stamina = true;
                    _ownsExhaustion = true;
                }
            }
            else if (_ownsExhaustion)
            {
                // Supporting the weapon must immediately release mod-owned fatigue;
                // otherwise the shared flag prevents hold breath from activating.
                if (supported)
                {
                    player.out_of_stamina = false;
                    _ownsExhaustion = false;
                    return;
                }

                // Reassert while tired because the native stamina update can clear
                // the flag between frames. Once rested, hand ownership back.
                if (_fatigueSeconds > threshold * 0.2f ||
                    player.staminaCount <= NativeStaminaRecoveryThreshold)
                {
                    player.out_of_stamina = true;
                }
                else
                {
                    ReleaseOwnedExhaustion(player);
                }
            }
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private static void ReleaseOwnedExhaustion(Soldier player)
    {
        if (!_ownsExhaustion)
            return;

        if (player.staminaCount <= NativeStaminaRecoveryThreshold)
            return;

        player.out_of_stamina = false;
        _ownsExhaustion = false;
    }

    private static void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Player aim fatigue failed (further identical errors suppressed): {exception.Message}");
    }
}

/// <summary>
/// Scales native stamina costs for the locally controlled soldier only.
/// This covers sprinting, breath holding, and jumps without replacing the
/// game's own exhaustion and recovery rules.
/// </summary>
[HarmonyPatch(typeof(Soldier), nameof(Soldier.DecreaseStamina))]
internal static class PlayerStaminaDrainPatch
{
    private static string _lastErrorSignature = string.Empty;

    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, ref int decreaseAmount)
    {
        try
        {
            if (decreaseAmount <= 0)
                return;

            var player = Soldier.CurrentControlledSoldierOrNull();
            if (player == null || __instance.GetInstanceID() != player.GetInstanceID())
                return;

            var multiplier = Mathf.Clamp(Settings.PlayerStaminaMultiplier.Value, 0.5f, 3f);
            decreaseAmount = Mathf.Max(1, Mathf.RoundToInt(decreaseAmount / multiplier));
        }
        catch (Exception ex)
        {
            var signature = ex.GetType().FullName + ": " + ex.Message;
            if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
                return;

            _lastErrorSignature = signature;
            Plugin.LogSource.LogWarning(
                $"Player stamina scaling failed (further identical errors suppressed): {ex.Message}");
        }
    }
}
