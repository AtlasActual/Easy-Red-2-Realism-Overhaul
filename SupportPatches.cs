using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(PlaneBomb), "Explode")]
internal static class AircraftBombBlastPatch
{
    private const string ArtilleryExplosionFx = "explosion_huge";
    private const string ArtilleryExplosionBundle = "er2fxs";
    private static readonly HashSet<int> ScaledBombs = new();

    [HarmonyPrefix]
    private static void Prefix(PlaneBomb __instance, ref bool __state)
    {
        __state = false;
        if ((Settings.LayeredBlastEffectsEnabled.Value || Settings.HeavyOrdnanceCratersEnabled.Value) &&
            MultiplayerAuthority.CanMutateGameplay() && __instance != null)
        {
            BlastClassifier.BeginAircraftBomb();
            __state = true;
        }

        if (!Settings.AircraftBombBlastEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || __instance == null)
        {
            return;
        }

        var id = __instance.GetInstanceID();
        if (!ScaledBombs.Add(id))
            return;

        var originalRadius = __instance.explosionRadius;
        var originalFx = __instance.explosion_fx;
        if (originalRadius <= 0f)
            return;

        __instance.explosionRadius = originalRadius * Settings.AircraftBombBlastRadiusMultiplier.Value;
        __instance.explosion_fx = ArtilleryExplosionFx;
        __instance.explosion_bundle = ArtilleryExplosionBundle;
        AiState.Trace($"Aircraft bomb blast radius: {originalRadius:0.0}m -> " +
                      $"{__instance.explosionRadius:0.0}m; visual: {originalFx} -> {ArtilleryExplosionFx}");
    }

    [HarmonyPostfix]
    private static void Postfix(bool __state)
    {
        if (__state)
            BlastClassifier.EndAircraftBomb();
    }
}

internal static class HeavyOrdnanceCraterEffects
{
    internal static void Apply(Vector3 position, float explosionRadius, BlastClass blastClass)
    {
        if (!Settings.HeavyOrdnanceCratersEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || explosionRadius <= 0f)
        {
            return;
        }

        var multiplier = blastClass switch
        {
            BlastClass.Artillery => Settings.ArtilleryCraterRadiusMultiplier.Value,
            BlastClass.AircraftBomb => Settings.AircraftBombCraterRadiusMultiplier.Value,
            _ => 1f
        };

        if (multiplier <= 1f)
            return;

        var widenedRadius = explosionRadius * multiplier;
        try
        {
            // The native explosion has already requested its ordinary decal. Temporarily
            // open the game's spawn gate and layer a wider decal without detouring the
            // update-sensitive ResourcesManager.ExplosionDecal wrapper.
            var previousGate = ResourcesManager.next_explosion_decal_time;
            try
            {
                ResourcesManager.next_explosion_decal_time = 0f;
                ResourcesManager.ExplosionDecal(position, widenedRadius);
            }
            finally
            {
                ResourcesManager.next_explosion_decal_time = Mathf.Max(
                    previousGate, ResourcesManager.next_explosion_decal_time);
            }

            AiState.Trace($"{blastClass} crater decal radius: {explosionRadius:0.0}m -> " +
                          $"{widenedRadius:0.0}m");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Heavy-ordnance crater update failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(ArtilleryStrike), "Start")]
internal static class ArtilleryMissionLengthPatch
{
    private const float SmokeShellCountMultiplier = 2.25f;
    private const float SmokeStrikeRadiusMultiplier = 1.65f;
    private const float MinimumSmokeStrikeRadiusMeters = 65f;

    [HarmonyPostfix]
    private static void Postfix(ArtilleryStrike __instance)
    {
        if (!Settings.ArtilleryMissionLengthEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        var originalShells = __instance.shells;
        if (originalShells <= 0)
            return;

        var isSmoke = (__instance.shellType ?? string.Empty)
            .IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0;
        var shellMultiplier = Settings.ArtilleryShellCountMultiplier.Value;
        if (isSmoke)
            shellMultiplier = Mathf.Max(shellMultiplier, SmokeShellCountMultiplier);

        __instance.shells = Mathf.Max(originalShells,
            Mathf.RoundToInt(originalShells * shellMultiplier));

        var originalRadius = __instance.strikeRadius;
        if (isSmoke)
        {
            __instance.strikeRadius = Mathf.Max(
                originalRadius * SmokeStrikeRadiusMultiplier,
                MinimumSmokeStrikeRadiusMeters);
        }

        AiState.Trace($"Extended artillery mission ({__instance.shellType}): " +
                      $"{originalShells} -> {__instance.shells} rounds; " +
                      $"radius {originalRadius:0}m -> {__instance.strikeRadius:0}m; cadence unchanged");
    }
}

[HarmonyPatch(typeof(ArtilleryStrike), "ShootProjectile")]
internal static class ArtilleryBlastClassificationPatch
{
    [HarmonyPrefix]
    private static void Prefix(ArtilleryStrike __instance)
    {
        if ((Settings.LayeredBlastEffectsEnabled.Value || Settings.HeavyOrdnanceCratersEnabled.Value) &&
            MultiplayerAuthority.CanMutateGameplay() && __instance != null)
        {
            BlastClassifier.RegisterArtilleryRound(__instance);
        }
    }
}

[HarmonyPatch(typeof(StopParticleDelayed), "OnEnable")]
internal static class AtmosphericParticlePersistencePatch
{
    private static readonly Dictionary<int, float> BaseDurations = new();

    private enum PersistentEffect
    {
        None,
        Smoke,
        Dust
    }

    [HarmonyPrefix]
    private static void Prefix(StopParticleDelayed __instance)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
            return;

        var effect = Classify(__instance);
        if (effect == PersistentEffect.None)
            return;

        var id = __instance.GetInstanceID();
        if (!BaseDurations.TryGetValue(id, out var baseDuration))
        {
            baseDuration = __instance.stopAfterSeconds;
            BaseDurations[id] = baseDuration;
        }

        var multiplier = effect switch
        {
            PersistentEffect.Dust when Settings.ExplosionDustPersistenceEnabled.Value
                => Settings.ExplosionDustLifetimeMultiplier.Value,
            PersistentEffect.Smoke when Settings.SmokePersistenceEnabled.Value
                => Settings.SmokeLifetimeMultiplier.Value,
            _ => 1f
        };

        __instance.stopAfterSeconds = baseDuration * multiplier;

        AiState.Trace($"{effect} emitter lifetime: {baseDuration:0.0}s -> " +
                      $"{__instance.stopAfterSeconds:0.0}s ({GetHierarchyName(__instance)})");
    }

    private static PersistentEffect Classify(StopParticleDelayed component)
    {
        // This setting exists to make ordnance leave haze hanging over the battlefield.
        // An emitter carried by a projectile is a different thing entirely: stretching a
        // rocket's exhaust to six times its native duration leaves the trail smearing
        // through the air long after the warhead has gone. The game's projectiles are
        // plain Il2CppSystem.Objects rather than components, so there is nothing to walk
        // the hierarchy for - the name is the only signal available here.
        if (HierarchyNameMatches(component, NameLooksLikeProjectileTrail))
            return PersistentEffect.None;

        // Dust takes precedence over the generic "cloud" smoke identifier so a
        // child named, for example, "Dust Cloud" uses the explosion-dust setting.
        if (HierarchyNameMatches(component, NameLooksLikeDust))
            return PersistentEffect.Dust;

        return HierarchyNameMatches(component, NameLooksLikeSmoke)
            ? PersistentEffect.Smoke
            : PersistentEffect.None;
    }

    private static bool HierarchyNameMatches(
        StopParticleDelayed component,
        Func<string?, bool> predicate)
    {
        if (predicate(component.gameObject.name))
            return true;

        var parent = component.transform.parent;
        for (var depth = 0; parent != null && depth < 3; depth++, parent = parent.parent)
        {
            if (predicate(parent.gameObject.name))
                return true;
        }

        return false;
    }

    private static string GetHierarchyName(StopParticleDelayed component)
    {
        var result = component.gameObject.name;
        var parent = component.transform.parent;
        for (var depth = 0; parent != null && depth < 3; depth++, parent = parent.parent)
            result = $"{parent.gameObject.name}/{result}";

        return result;
    }

    private static bool NameLooksLikeDust(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return name.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("dirt", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("soil", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("sand", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("earth cloud", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("ground cloud", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool NameLooksLikeSmoke(string? name)
    {
        return !string.IsNullOrEmpty(name) &&
               (name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool NameLooksLikeProjectileTrail(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return name.IndexOf("trail", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("rocket", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("missile", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("tracer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("exhaust", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("propell", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

#pragma warning disable CS0618 // IL2CPP exposes these ParticleSystem settings through Unity's legacy accessors.

[HarmonyPatch(typeof(ExplosionManager), "OnEnable")]
internal static class AtmosphericParticleSystemPersistencePatch
{
    private enum AtmosphericEffect
    {
        None,
        Smoke,
        Dust
    }

    private readonly struct ParticleDefaults
    {
        internal readonly float Duration;
        internal readonly float StartLifetime;
        internal readonly float EmissionRate;
        internal readonly bool Loop;
        internal readonly bool EmissionEnabled;

        internal ParticleDefaults(ParticleSystem system)
        {
            Duration = Mathf.Max(0.1f, system.duration);
            StartLifetime = Mathf.Max(0.01f, system.startLifetime);
            EmissionRate = Mathf.Max(0f, system.emissionRate);
            Loop = system.loop;
            EmissionEnabled = system.enableEmission;
        }
    }

    private static readonly Dictionary<int, ParticleDefaults> BaseSettings = new();

    [HarmonyPrefix]
    private static void Prefix(ExplosionManager __instance)
    {
        if (__instance == null)
            return;

        try
        {
            var changed = 0;
            var rootEffect = Classify(__instance.gameObject.name);
            var systems = __instance.gameObject.GetComponentsInChildren<ParticleSystem>(true);

            foreach (var system in systems)
            {
                try
                {
                    if (system == null)
                        continue;

                    var effect = Classify(system.gameObject.name);
                    if (effect == AtmosphericEffect.None && system.transform == __instance.transform)
                        effect = rootEffect;
                    // A destruction-dust prefab commonly gives only its root a descriptive
                    // name. Its otherwise generically named children are still dust. Smoke
                    // explosion prefabs are not treated this way, so sparks and debris under
                    // a smoke root keep their native timing.
                    if (effect == AtmosphericEffect.None && rootEffect == AtmosphericEffect.Dust)
                        effect = AtmosphericEffect.Dust;
                    if (effect == AtmosphericEffect.None)
                        continue;

                    var multiplier = effect switch
                    {
                        AtmosphericEffect.Dust when Settings.ExplosionDustPersistenceEnabled.Value
                            => Settings.ExplosionDustLifetimeMultiplier.Value,
                        AtmosphericEffect.Smoke when Settings.SmokePersistenceEnabled.Value
                            => Settings.SmokeLifetimeMultiplier.Value,
                        _ => 1f
                    };

                    var id = system.GetInstanceID();
                    if (!BaseSettings.TryGetValue(id, out var defaults))
                    {
                        defaults = new ParticleDefaults(system);
                        BaseSettings[id] = defaults;
                    }

                    var wasPlaying = system.isPlaying;
                    if (wasPlaying)
                        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

                    // Smoke artillery is burst-driven, so particle lifetime is the value
                    // that actually governs persistence. Grenade smoke also emits over
                    // time; looping it lets the controller preserve that emission phase.
                    system.startLifetime = defaults.StartLifetime * multiplier;
                    system.loop = defaults.Loop;
                    system.enableEmission = defaults.EmissionEnabled;

                    if (multiplier > 1f && defaults.EmissionEnabled && defaults.EmissionRate > 0.01f)
                    {
                        system.loop = true;
                        AtmosphericParticleEmissionPersistence.KeepEmittingUntil(
                            system,
                            Time.time + defaults.Duration * multiplier);
                    }
                    else
                    {
                        AtmosphericParticleEmissionPersistence.Cancel(system);
                    }

                    changed++;

                    if (wasPlaying)
                        system.Play(false);
                }
                catch (Il2CppInterop.Runtime.ObjectCollectedException)
                {
                    // Explosion effects are pooled and can disappear while Unity is
                    // returning their child components. The remaining systems are
                    // independent, so keep processing the live entries.
                }
            }

            if (changed > 0)
            {
                AiState.Trace($"Atmospheric particles updated: {changed} smoke/dust systems " +
                              $"({__instance.gameObject.name})");
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // The pooled explosion itself was released during OnEnable; there is
            // no live particle system left to extend.
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Smoke particle persistence failed: {ex.Message}");
        }
    }

    private static AtmosphericEffect Classify(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return AtmosphericEffect.None;

        if (name.IndexOf("dust", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("dirt", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("soil", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("sand", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("earth cloud", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("ground cloud", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AtmosphericEffect.Dust;
        }

        return name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0
            ? AtmosphericEffect.Smoke
            : AtmosphericEffect.None;
    }
}

internal static class AtmosphericParticleEmissionPersistence
{
    // Extended smoke can leave many pooled particle systems pending at once.
    // Their stop time does not need frame-perfect polling, while checking every
    // system's native state every frame is needlessly expensive in barrages.
    private const float PendingEmissionPollInterval = 0.25f;

    private sealed class PendingEmission
    {
        internal ParticleSystem System = null!;
        internal float StopAt;
    }

    private static readonly Dictionary<int, PendingEmission> Pending = new();
    private static readonly List<int> Completed = new();
    private static float _nextPollAt;

    internal static void KeepEmittingUntil(ParticleSystem system, float stopAt)
    {
        Pending[system.GetInstanceID()] = new PendingEmission
        {
            System = system,
            StopAt = stopAt
        };
    }

    internal static void Cancel(ParticleSystem system)
    {
        Pending.Remove(system.GetInstanceID());
    }

    internal static void Update()
    {
        if (Pending.Count == 0)
            return;

        var now = Time.time;
        if (now < _nextPollAt)
            return;

        _nextPollAt = now + PendingEmissionPollInterval;
        Completed.Clear();
        foreach (var pair in Pending)
        {
            try
            {
                var system = pair.Value.System;
                if (system == null || !system.gameObject.activeInHierarchy)
                {
                    Completed.Add(pair.Key);
                    continue;
                }

                if (now < pair.Value.StopAt)
                    continue;

                system.enableEmission = false;
                system.loop = false;
                Completed.Add(pair.Key);
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Pooled effects can disappear between frames. Their native
                // emission has already ended, so only the stale tracking entry
                // remains to be removed.
                Completed.Add(pair.Key);
            }
        }

        foreach (var id in Completed)
            Pending.Remove(id);
    }
}

internal sealed class AtmosphericParticlePersistenceController : MonoBehaviour
{
    private void Update()
    {
        DistantSoundShaper.UpdateTrackedExplosions();
        AtmosphericParticleEmissionPersistence.Update();
        AudioSourceCacheMaintenance.Update(Time.unscaledTime);
    }
}

[HarmonyPatch(typeof(ExplosionManager), "OnEnable")]
internal static class GrenadeExplosionVisualScalePatch
{
    private static readonly Dictionary<int, Vector3> BaseTransformScales = new();

    [HarmonyPrefix]
    private static void Prefix(ExplosionManager __instance)
    {
        if (__instance == null ||
            !__instance.gameObject.name.StartsWith("explosion_grenade", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var scale = Settings.GrenadeExplosionVisualScale.Value;
            ScaleTransform(__instance.transform, scale);

            var systems = __instance.gameObject.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var system in systems)
            {
                if (system == null)
                    continue;

                // Play-on-awake may already have emitted its first burst before a
                // script's OnEnable. Restart it so every visible particle uses the
                // reduced root scale. This game build does not expose the newer
                // ParticleSystem.MainModule.scalingMode accessor.
                if (!system.isPlaying)
                    continue;

                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(false);
            }

            AiState.Trace($"Grenade explosion visual scale: {scale:0.##}x");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Grenade explosion visual scaling failed: {ex.Message}");
        }
    }

    private static void ScaleTransform(Transform transform, float scale)
    {
        var id = transform.GetInstanceID();
        if (!BaseTransformScales.TryGetValue(id, out var baseScale))
        {
            baseScale = transform.localScale;
            BaseTransformScales[id] = baseScale;
        }

        transform.localScale = baseScale * scale;
    }
}

#pragma warning restore CS0618

internal static class SquadRadioSupport
{
    internal static void Update(Squad squad, float now)
    {
        if (!Settings.SmokeSupportEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
            return;

        try
        {
            var leader = squad.Leader;
            if (!AiOwnership.IsAutonomous(leader))
                return;
            if (squad.order != Order.charge && squad.order != Order.attackFromSide)
                return;
            if (!squad.HasRadioman() || squad.RadioRequestActive() || !squad.SomeTimePassedSinceLastRadioRequest())
                return;

            var id = leader.GetInstanceID();
            if (!AiState.CooldownReady(AiState.NextSmokeAttempt, id, now))
                return;
            AiState.NextSmokeAttempt[id] = now + Settings.SmokeCooldownSeconds.Value;

            var target = squad.moveOrderPosition;
            if (Vector3.Distance(leader.transform.position, target) < Settings.SmokeMinimumDistance.Value)
                return;
            if (UnityEngine.Random.value > Settings.SmokeRequestChance.Value)
                return;

            var radioman = squad.GetRadioman();
            if (radioman == null)
                return;

            // Leave the request waiting so the native radioman AI can accept and dispatch it.
            squad.GiveRadioRequest(target, RadioRequest.artillerySmoke, false);
            AiState.Trace($"Smoke support: squad led by {id} requested smoke at attack position");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Smoke support update failed: {ex.Message}");
        }
    }
}
