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

            if (_fatigueSeconds >= threshold || player.staminaCount <= 1f)
            {
                player.out_of_stamina = true;
                _ownsExhaustion = true;
            }
            else if (_ownsExhaustion)
            {
                // Reassert while tired because the native stamina update can clear
                // the flag between frames. Once rested, hand ownership back.
                if (_fatigueSeconds > threshold * 0.2f || player.staminaCount <= 10f)
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

        if (player.staminaCount > 10f)
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
