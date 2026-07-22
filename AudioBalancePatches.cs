using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(ResourcesManager), nameof(ResourcesManager.SetUpDisabledReverbFilter))]
internal static class ExistingReverbFilterSetupPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameObject filterPos, ref AudioReverbFilter __result)
    {
        if (filterPos == null)
            return true;

        var existing = filterPos.GetComponent<AudioReverbFilter>();
        if (existing == null)
            return true;

        // Unity permits only one AudioReverbFilter per GameObject. Distant sound
        // shaping can create it before Easy Red 2 initializes the gun's native
        // reverb, so return that component instead of letting the game attempt a
        // duplicate AddComponent and then dereference its null result.
        __result = existing;
        return false;
    }
}

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
        DistantSoundShaper.Apply(source, false, false);
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
        DistantSoundShaper.Apply(vehicle.engine_source, false, false);
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

        DistantSoundShaper.Apply(__instance.audioSource);
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

        DistantSoundShaper.Apply(__instance.audioSource);
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
        DistantSoundShaper.Apply(supplement);
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

[HarmonyPatch(typeof(ExplosionManager), "OnEnable")]
internal static class ExplosionDistanceSoundPatch
{
    [HarmonyPostfix]
    private static void Postfix(ExplosionManager __instance)
    {
        DistantSoundShaper.TrackExplosion(__instance);
    }
}

internal static class DistantSoundShaper
{
    private const float NeutralCutoffHz = 22000f;
    private const float ContinuousUpdateInterval = 0.25f;
    // Explosion audio sources are static for the life of the pooled effect.  A
    // full child-component walk ten times per second per explosion becomes very
    // costly during artillery barrages, so refresh them at a human-inaudible
    // cadence and spread concurrent effects across frames.
    private const float ExplosionScanInterval = 0.35f;
    private const int MaxExplosionAudioScansPerFrame = 2;
    private static readonly Dictionary<int, AudioLowPassFilter> Filters = new();
    private static readonly Dictionary<int, AudioReverbFilter> ReverbFilters = new();
    private static bool _reverbFilterAvailable = true;
    private static readonly Dictionary<int, float> NextUpdates = new();
    private static readonly Dictionary<int, TrackedExplosion> TrackedExplosions = new();
    private static readonly List<int> CompletedExplosions = new();
    private static Camera? _listenerCamera;

    private sealed class TrackedExplosion
    {
        internal ExplosionManager Manager = null!;
        internal float ExpiresAt;
        internal float NextScanAt;
    }

    internal static void TrackExplosion(ExplosionManager? manager)
    {
        if (manager == null)
            return;

        var duration = Mathf.Clamp(manager.max_distance / 300f + 1f, 4f, 15f);
        TrackedExplosions[manager.GetInstanceID()] = new TrackedExplosion
        {
            Manager = manager,
            ExpiresAt = Time.unscaledTime + duration,
            NextScanAt = 0f
        };
    }

    internal static void UpdateTrackedExplosions()
    {
        if (TrackedExplosions.Count == 0)
            return;

        var now = Time.unscaledTime;
        var scansThisFrame = 0;
        CompletedExplosions.Clear();
        foreach (var pair in TrackedExplosions)
        {
            var tracked = pair.Value;
            var manager = tracked.Manager;
            if (manager == null || now >= tracked.ExpiresAt)
            {
                CompletedExplosions.Add(pair.Key);
                continue;
            }

            if (now < tracked.NextScanAt || scansThisFrame >= MaxExplosionAudioScansPerFrame)
                continue;
            tracked.NextScanAt = now + ExplosionScanInterval;
            scansThisFrame++;

            try
            {
                var sources = manager.gameObject.GetComponentsInChildren<AudioSource>(true);
                foreach (var source in sources)
                    Apply(source);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"Explosion distance-audio scan failed: {ex.Message}");
                CompletedExplosions.Add(pair.Key);
            }
        }

        foreach (var id in CompletedExplosions)
            TrackedExplosions.Remove(id);
    }

    internal static void Apply(
        AudioSource? source,
        bool forceUpdate = true,
        bool addReflections = true)
    {
        if (source == null)
            return;

        var id = source.GetInstanceID();
        if (!forceUpdate && NextUpdates.TryGetValue(id, out var nextUpdate) &&
            Time.unscaledTime < nextUpdate)
        {
            return;
        }

        NextUpdates[id] = Time.unscaledTime + ContinuousUpdateInterval;

        if (!Settings.AudioBalanceEnabled.Value || !Settings.DistantSoundShapingEnabled.Value)
        {
            DisableOwnedEffects(id);
            return;
        }

        if (_listenerCamera == null)
            _listenerCamera = Camera.main;
        if (_listenerCamera == null)
        {
            DisableOwnedEffects(id);
            return;
        }

        var startDistance = Settings.DistantSoundStartDistance.Value;
        var fullEffectDistance = Mathf.Max(
            startDistance + 1f,
            Settings.DistantSoundFullEffectDistance.Value);
        var distance = Vector3.Distance(
            source.transform.position,
            _listenerCamera.transform.position);
        var amount = Mathf.InverseLerp(startDistance, fullEffectDistance, distance);
        amount = amount * amount * (3f - 2f * amount);

        if (amount <= 0.001f)
        {
            DisableOwnedEffects(id);
            return;
        }

        if (!Filters.TryGetValue(id, out var filter) || filter == null)
        {
            filter = source.gameObject
                .AddComponent(Il2CppType.Of<AudioLowPassFilter>())
                .Cast<AudioLowPassFilter>();
            Filters[id] = filter;
        }

        // Logarithmic interpolation gives audible air absorption through the
        // middle distance without abruptly muffling sounds at the threshold.
        var minimumCutoff = Settings.DistantSoundMinimumCutoff.Value;
        filter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(
            Mathf.Log(NeutralCutoffHz),
            Mathf.Log(minimumCutoff),
            amount));
        filter.lowpassResonanceQ = 1f;
        filter.enabled = true;

        ConfigureReflections(source, id, amount, addReflections);
    }

    private static void ConfigureReflections(
        AudioSource source,
        int sourceId,
        float distanceAmount,
        bool addReflections)
    {
        var reverbAmount = addReflections
            ? Settings.DistantReverbAmount.Value * distanceAmount
            : 0f;
        if (reverbAmount > 0.001f && _reverbFilterAvailable)
        {
            try
            {
                if (!ReverbFilters.TryGetValue(sourceId, out var reverb) || reverb == null)
                {
                    reverb = source.gameObject.GetComponent<AudioReverbFilter>();
                    if (reverb == null)
                    {
                        reverb = source.gameObject
                            .AddComponent(Il2CppType.Of<AudioReverbFilter>())
                            .Cast<AudioReverbFilter>();
                    }

                    reverb.reverbPreset = AudioReverbPreset.User;
                    reverb.dryLevel = 0f;
                    reverb.room = -700f;
                    reverb.roomHF = -2800f;
                    reverb.roomLF = -300f;
                    reverb.decayTime = 2.2f;
                    reverb.decayHFRatio = 0.55f;
                    reverb.reflectionsLevel = -1800f;
                    reverb.reflectionsDelay = 0.04f;
                    reverb.reverbDelay = 0.06f;
                    reverb.diffusion = 65f;
                    reverb.density = 45f;
                    reverb.hfReference = 5000f;
                    reverb.lfReference = 250f;
                    ReverbFilters[sourceId] = reverb;
                }

                reverb.reverbLevel = Mathf.Lerp(
                    -3500f,
                    -500f,
                    Mathf.Sqrt(reverbAmount));
                reverb.enabled = true;
            }
            catch (Exception)
            {
                // Reverb is optional as well and can be absent from stripped players.
                _reverbFilterAvailable = false;
                ReverbFilters.Clear();
            }
        }
        else
        {
            DisableReverb(sourceId);
        }
    }

    private static void DisableOwnedEffects(int sourceId)
    {
        if (Filters.TryGetValue(sourceId, out var filter) && filter != null)
            filter.enabled = false;

        DisableReverb(sourceId);
    }

    private static void DisableReverb(int sourceId)
    {
        if (ReverbFilters.TryGetValue(sourceId, out var reverb) && reverb != null)
            reverb.enabled = false;
    }
}
