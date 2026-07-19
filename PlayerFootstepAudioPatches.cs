using System;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(Soldier), nameof(Soldier.TryPlayStepSound))]
internal static class PlayerFootstepContextPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance)
    {
        if (!Settings.AudioBalanceEnabled.Value)
            return;

        var player = Soldier.CurrentControlledSoldierOrNull();
        if (player == null || player.GetInstanceID() != __instance.GetInstanceID() ||
            __instance.vfxSoundEmitter == null)
        {
            return;
        }

        PlayerFootstepAudio.Begin(__instance.vfxSoundEmitter);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        PlayerFootstepAudio.End();
        return __exception;
    }
}

[HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) })]
internal static class PlayerFootstepOneShotVolumePatch
{
    [HarmonyPrefix]
    private static void Prefix(AudioSource __instance, ref float __1)
    {
        if (PlayerFootstepAudio.IsRedirecting || !PlayerFootstepAudio.IsActiveFor(__instance))
            return;

        __1 *= Settings.PlayerFootstepVolumeMultiplier.Value;
    }
}

[HarmonyPatch(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip) })]
internal static class PlayerFootstepOneShotPatch
{
    [HarmonyPrefix]
    private static bool Prefix(AudioSource __instance, AudioClip __0)
    {
        if (PlayerFootstepAudio.IsRedirecting || !PlayerFootstepAudio.IsActiveFor(__instance))
            return true;

        PlayerFootstepAudio.PlayWithConfiguredVolume(__instance, __0);
        return false;
    }
}

internal static class PlayerFootstepAudio
{
    private static int _activeEmitterId;
    internal static bool IsRedirecting { get; private set; }

    internal static void Begin(AudioSource emitter)
    {
        _activeEmitterId = emitter.GetInstanceID();
    }

    internal static void End()
    {
        _activeEmitterId = 0;
        IsRedirecting = false;
    }

    internal static bool IsActiveFor(AudioSource source)
        => _activeEmitterId != 0 && source.GetInstanceID() == _activeEmitterId;

    internal static void PlayWithConfiguredVolume(AudioSource source, AudioClip clip)
    {
        IsRedirecting = true;
        try
        {
            source.PlayOneShot(clip, Settings.PlayerFootstepVolumeMultiplier.Value);
        }
        finally
        {
            IsRedirecting = false;
        }
    }
}
