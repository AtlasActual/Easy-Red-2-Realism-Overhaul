using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(AudioSource), nameof(AudioSource.Play), new Type[] { })]
internal static class InvalidFirePositionAudioPlayPatch
{
    [HarmonyPrefix]
    private static bool Prefix(AudioSource __instance)
    {
        if (__instance == null || __instance.clip != null)
            return true;

        var owner = __instance.gameObject;
        return owner == null ||
               !owner.name.StartsWith("firePos", StringComparison.Ordinal);
    }
}

[HarmonyPatch(typeof(TankTrucksSound), nameof(TankTrucksSound.Update))]
internal static class TankTrackVolumePatch
{
    [HarmonyPrefix]
    private static void Prefix(TankTrucksSound __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            VehicleAudioBalance.RestoreTrackVolume(__instance);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(TankTrucksSound __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            VehicleAudioBalance.ApplyTrackVolume(__instance);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }
}

[HarmonyPatch(typeof(MovableVehicle), nameof(MovableVehicle.UpdateMotorSound))]
internal static class GroundVehicleEngineSoundPatch
{
    [HarmonyPrefix]
    private static void Prefix(MovableVehicle __instance)
    {
        VehicleAudioBalance.RestoreEngineVolume(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(MovableVehicle __instance)
    {
        VehicleAudioBalance.ApplyEngineVolume(__instance);
    }
}

[HarmonyPatch(typeof(VehicleTank), nameof(VehicleTank.UpdateMotorSound))]
internal static class TankEngineSoundOverridePatch
{
    [HarmonyPrefix]
    private static void Prefix(VehicleTank __instance)
    {
        VehicleAudioBalance.RestoreEngineVolume(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(VehicleTank __instance)
    {
        VehicleAudioBalance.ApplyEngineVolume(__instance);
    }
}

internal static class VehicleAudioBalance
{
    private sealed class SourceState
    {
        internal AudioSource Source = null!;
        internal float NativeVolume;
    }

    private static readonly Dictionary<int, SourceState> TrackSources = new();
    private static readonly Dictionary<int, SourceState> EngineSources = new();
    private static readonly List<int> DeadSources = new();

    internal static void RestoreTrackVolume(TankTrucksSound? controller)
    {
        if (!TryGetTankTrackSource(controller, out var source))
            return;

        RestoreNativeVolume(source, TrackSources);
    }

    internal static void ApplyTrackVolume(TankTrucksSound? controller)
    {
        if (!TryGetTankTrackSource(controller, out var source))
            return;

        CaptureAndScale(source, TrackSources, Settings.TankTrackVolumeMultiplier.Value);

        // Track loops use the same distant air absorption as engines, but
        // remain dry so continuous vehicle noise does not develop reverb.
    }

    internal static void RestoreEngineVolume(MovableVehicle? vehicle)
    {
        if (vehicle?.engine_source != null)
            RestoreNativeVolume(vehicle.engine_source, EngineSources);
    }

    internal static void ApplyEngineVolume(MovableVehicle? vehicle)
    {
        if (vehicle?.engine_source == null)
            return;

        CaptureAndScale(
            vehicle.engine_source,
            EngineSources,
            Settings.VehicleEngineSound.Value);
    }

    internal static void RefreshTrackedSources()
    {
        RefreshSources(TrackSources, Settings.TankTrackVolumeMultiplier.Value);
        RefreshSources(EngineSources, Settings.VehicleEngineSound.Value);
    }

    private static bool TryGetTankTrackSource(
        TankTrucksSound? controller,
        out AudioSource source)
    {
        source = null!;
        if (controller == null || controller.connectedVehicle == null ||
            controller.tracksAudioSource == null ||
            controller.connectedVehicle.GetComponent<VehicleTank>() == null)
        {
            return false;
        }

        source = controller.tracksAudioSource;
        return true;
    }

    private static void RestoreNativeVolume(
        AudioSource source,
        Dictionary<int, SourceState> states)
    {
        var id = source.GetInstanceID();
        if (states.TryGetValue(id, out var state) && state.Source != null)
            source.volume = state.NativeVolume;
    }

    private static void CaptureAndScale(
        AudioSource source,
        Dictionary<int, SourceState> states,
        float configuredMultiplier)
    {
        var id = source.GetInstanceID();
        if (!states.TryGetValue(id, out var state))
        {
            state = new SourceState();
            states[id] = state;
        }

        state.Source = source;
        state.NativeVolume = source.volume;
        SetVolume(source, state.NativeVolume, configuredMultiplier);
    }

    private static void RefreshSources(
        Dictionary<int, SourceState> states,
        float configuredMultiplier)
    {
        DeadSources.Clear();
        foreach (var pair in states)
        {
            var state = pair.Value;
            if (state.Source == null)
            {
                DeadSources.Add(pair.Key);
                continue;
            }

            SetVolume(state.Source, state.NativeVolume, configuredMultiplier);
        }

        foreach (var id in DeadSources)
            states.Remove(id);
    }

    internal static int TrackSourceCount => TrackSources.Count;
    internal static int EngineSourceCount => EngineSources.Count;

    // Drops entries whose AudioSource wrapper points at a destroyed native object.
    // Both dictionaries are keyed by AudioSource instance id, and vehicles respawn
    // constantly, so without this sweep every destroyed engine/track source stays
    // pinned via its wrapper's GCHandle for the whole session (RefreshSources only
    // runs on an audio SettingChanged event, not on vehicle death).
    internal static void SweepStaleSources()
    {
        SweepDeadSources(TrackSources);
        SweepDeadSources(EngineSources);
    }

    internal static void ClearSources()
    {
        TrackSources.Clear();
        EngineSources.Clear();
    }

    private static void SweepDeadSources(Dictionary<int, SourceState> states)
    {
        DeadSources.Clear();
        foreach (var pair in states)
        {
            if (!IsSourceAlive(pair.Value.Source))
                DeadSources.Add(pair.Key);
        }

        foreach (var id in DeadSources)
            states.Remove(id);
    }

    private static bool IsSourceAlive(AudioSource source)
    {
        try
        {
            return source != null;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            return false;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static void SetVolume(
        AudioSource source,
        float nativeVolume,
        float configuredMultiplier)
    {
        var multiplier = Settings.AudioBalanceEnabled.Value
            ? configuredMultiplier
            : 1f;
        source.volume = Mathf.Clamp01(nativeVolume * multiplier);
    }
}

[HarmonyPatch(typeof(GenericGun), "FixAudioSourceVolumes")]
internal static class HandheldGunVolumePatch
{
    [HarmonyPostfix]
    private static void Postfix(GenericGun __instance)
    {
        if (__instance.audioSource == null)
            return;

        if (Settings.AudioBalanceEnabled.Value)
        {
            __instance.audioSource.volume = Mathf.Clamp01(
                __instance.audioSource.volume * Settings.WeaponFireVolumeMultiplier.Value);
        }
    }
}

[HarmonyPatch(typeof(TurretGun), "FixAudioSourceVolumes")]
internal static class TankGunVolumePatch
{
    [HarmonyPostfix]
    private static void Postfix(TurretGun __instance)
    {
        if (__instance.audioSource == null)
            return;

        var isTankCannon = __instance.GetCaliber() >= 20f &&
                           __instance.GetComponentInParent<VehicleTank>() != null;
        if (isTankCannon)
        {
            var multiplier = Settings.AudioBalanceEnabled.Value
                ? Settings.TankGunVolumeMultiplier.Value
                : 1f;
            TankCannonAudioGain.ApplyVolume(__instance.audioSource, multiplier);
        }
        else
        {
            // A turret can switch between its cannon and coaxial machine gun while
            // retaining the same AudioSource, so always clear stale cannon gain.
            TankCannonAudioGain.Disable(__instance.audioSource);
            if (Settings.AudioBalanceEnabled.Value)
            {
                __instance.audioSource.volume = Mathf.Clamp01(
                    __instance.audioSource.volume * Settings.WeaponFireVolumeMultiplier.Value);
            }
        }
    }
}

[HarmonyPatch(typeof(TurretGun), "SingleFireSound")]
internal static class TankCannonSupplementalPlaybackPatch
{
    [HarmonyPostfix]
    private static void Postfix(TurretGun __instance)
    {
        TankCannonAudioGain.PlaySupplement(__instance.audioSource);
    }
}

internal static class TankCannonAudioGain
{
    private const string SupplementObjectName = "ER2 Tank Cannon Gain";

    private sealed class SourceState
    {
        internal AudioSource Source = null!;
        internal AudioSource? Supplement;
        internal float SupplementVolume;
    }

    private static readonly Dictionary<int, SourceState> Sources = new();

    internal static void ApplyVolume(AudioSource source, float multiplier)
    {
        var state = GetState(source);
        var targetVolume = source.volume * Mathf.Max(0f, multiplier);

        // AudioSource.volume is capped at one. Preserve gain above that ceiling
        // for a second, co-located source instead of silently discarding it.
        source.volume = Mathf.Clamp01(targetVolume);
        state.SupplementVolume = Mathf.Clamp01(targetVolume - source.volume);
    }

    internal static void Disable(AudioSource source)
    {
        if (Sources.TryGetValue(source.GetInstanceID(), out var state))
            state.SupplementVolume = 0f;
    }

    internal static int SourceCount => Sources.Count;

    // Sources is keyed by TurretGun AudioSource instance id and is never pruned by
    // the hot path (Disable only zeroes the supplement gain). Tank guns respawn with
    // each destroyed vehicle, so drop entries whose native source is gone. The
    // supplement GameObject is a child of state.Source's transform and Unity destroys
    // it with the parent, so removing the entry releases both wrappers.
    internal static void SweepStaleSources()
    {
        StaleCannonIds.Clear();
        foreach (var pair in Sources)
        {
            var alive = false;
            try
            {
                alive = pair.Value.Source != null;
            }
            catch (NullReferenceException)
            {
            }
            catch (Il2CppInterop.Runtime.Il2CppException)
            {
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
            }

            if (!alive)
                StaleCannonIds.Add(pair.Key);
        }

        foreach (var id in StaleCannonIds)
            Sources.Remove(id);
    }

    internal static void ClearSources() => Sources.Clear();

    private static readonly List<int> StaleCannonIds = new();

    internal static void PlaySupplement(AudioSource? source)
    {
        if (source == null || !source.isPlaying || source.clip == null ||
            !Sources.TryGetValue(source.GetInstanceID(), out var state) ||
            state.Source == null || state.SupplementVolume <= 0.001f)
        {
            return;
        }

        var supplement = GetOrCreateSupplement(state);
        CopyPlaybackSettings(source, supplement);
        supplement.volume = state.SupplementVolume;

        // SingleFireSound has just started the native source. Starting the
        // companion in the same frame keeps both voices sample-close and makes
        // the slider's 1x-2x range audible instead of flattening at 1x.
        supplement.Play();
    }

    private static SourceState GetState(AudioSource source)
    {
        var id = source.GetInstanceID();
        if (!Sources.TryGetValue(id, out var state) || state.Source == null)
        {
            state = new SourceState { Source = source };
            Sources[id] = state;
        }

        return state;
    }

    private static AudioSource GetOrCreateSupplement(SourceState state)
    {
        if (state.Supplement != null)
            return state.Supplement;

        var supplementObject = new GameObject(SupplementObjectName);
        supplementObject.transform.SetParent(state.Source.transform, false);
        state.Supplement = supplementObject
            .AddComponent(Il2CppType.Of<AudioSource>())
            .Cast<AudioSource>();
        return state.Supplement;
    }

    private static void CopyPlaybackSettings(AudioSource source, AudioSource supplement)
    {
        supplement.clip = source.clip;
        supplement.outputAudioMixerGroup = source.outputAudioMixerGroup;
        supplement.mute = source.mute;
        supplement.bypassEffects = source.bypassEffects;
        supplement.bypassListenerEffects = source.bypassListenerEffects;
        supplement.bypassReverbZones = source.bypassReverbZones;
        supplement.playOnAwake = false;
        supplement.loop = false;
        supplement.priority = source.priority;
        supplement.pitch = source.pitch;
        supplement.panStereo = source.panStereo;
        supplement.spatialBlend = source.spatialBlend;
        supplement.reverbZoneMix = source.reverbZoneMix;
        supplement.dopplerLevel = source.dopplerLevel;
        supplement.spread = source.spread;
        supplement.rolloffMode = source.rolloffMode;
        supplement.minDistance = source.minDistance;
        supplement.maxDistance = source.maxDistance;
    }
}
internal static class AudioSourceCacheMaintenance
{
    private const float SweepIntervalSeconds = 10f;
    private static float _nextSweepAt;

    // Slow enough to matter is anything a player could perceive as a hitch; the sweep is
    // supposed to be bookkeeping over a few hundred cache entries.
    private const float SlowSweepMs = 5f;

    internal static void Update(float now)
    {
        if (now < _nextSweepAt)
            return;
        _nextSweepAt = now + SweepIntervalSeconds;

        // This runs from a MonoBehaviour rather than a Harmony patch, so it never landed
        // in the probe's mod total: a periodic all-at-once sweep of every cached audio
        // source was invisible in every measurement taken so far. It walks hundreds of
        // entries and each liveness test is an interop call, and it fires on ONE frame
        // every SweepIntervalSeconds — exactly the shape of a stutter that recurs on a
        // fixed period.
        var begin = System.Diagnostics.Stopwatch.GetTimestamp();

        VehicleAudioBalance.SweepStaleSources();
        TankCannonAudioGain.SweepStaleSources();
        // Weapon-trait cache: keyed by pointer, so it is dropped on the same cadence to
        // bound how long a reused address could carry a stale classification.
        HandheldWeaponClassifier.ClearCache();
        // Vehicle tank/faction caches, keyed by instance id. These had no eviction at all
        // and grew for the whole battle as vehicles were destroyed and replaced.
        SoldierSequentialUpdatePatch.ClearVehicleCaches();
        // Pooled-object decal lookups, also keyed by instance id.
        HitDecalPoolDurationPatch.ClearCache();

        if (!Settings.StutterProbeEnabled.Value)
            return;

        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - begin) *
                        (1000f / System.Diagnostics.Stopwatch.Frequency);
        if (elapsedMs >= SlowSweepMs)
        {
            Plugin.LogSource.LogWarning(
                $"Audio cache sweep took {elapsedMs:F1}ms (every {SweepIntervalSeconds:F0}s): " +
                $"vehTrack={VehicleAudioBalance.TrackSourceCount}, " +
                $"vehEngine={VehicleAudioBalance.EngineSourceCount}, cannon={TankCannonAudioGain.SourceCount}.");
        }
    }

    internal static void ResetBattle()
    {
        _nextSweepAt = 0f;
        VehicleAudioBalance.ClearSources();
        TankCannonAudioGain.ClearSources();
        HandheldWeaponClassifier.ClearCache();
    }
}

[HarmonyPatch(typeof(BattleManager), "Start")]
internal static class AudioCacheBattleResetPatch
{
    [HarmonyPrefix]
    private static void Prefix() => AudioSourceCacheMaintenance.ResetBattle();
}
