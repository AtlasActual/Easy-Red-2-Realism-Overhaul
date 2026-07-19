using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal enum BlastClass
{
    None,
    Mortar,
    Artillery,
    AircraftBomb
}

internal static class BlastClassifier
{
    private sealed class PendingRound
    {
        internal Vector3 Center;
        internal float MatchRadius;
        internal float ExpiresAt;
        internal BlastClass Class;
    }

    private static readonly List<PendingRound> PendingRounds = new();
    private static readonly List<BlastClass> ActiveExplosions = new();
    private static int _aircraftBombDepth;

    internal static void BeginAircraftBomb() => _aircraftBombDepth++;

    internal static void EndAircraftBomb()
        => _aircraftBombDepth = Mathf.Max(0, _aircraftBombDepth - 1);

    internal static void RegisterArtilleryRound(ArtilleryStrike strike)
    {
        var now = Time.time;
        for (var i = PendingRounds.Count - 1; i >= 0; i--)
        {
            if (PendingRounds[i].ExpiresAt < now)
                PendingRounds.RemoveAt(i);
        }

        if (PendingRounds.Count >= 128)
            PendingRounds.RemoveAt(0);

        var shell = strike.shellType ?? string.Empty;
        if (shell.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        var blastClass = shell.IndexOf("mortar", StringComparison.OrdinalIgnoreCase) >= 0
            ? BlastClass.Mortar
            : BlastClass.Artillery;

        PendingRounds.Add(new PendingRound
        {
            Center = strike.strikeStartPos,
            MatchRadius = Mathf.Max(20f, strike.strikeRadius + 30f),
            ExpiresAt = now + 20f,
            Class = blastClass
        });
    }

    internal static BlastClass BeginExplosion(Vector3 explosionPosition)
    {
        var blastClass = Find(explosionPosition);
        ActiveExplosions.Add(blastClass);
        return blastClass;
    }

    internal static BlastClass CurrentExplosion
        => ActiveExplosions.Count > 0 ? ActiveExplosions[^1] : BlastClass.None;

    internal static bool IsExplosionActive => ActiveExplosions.Count > 0;

    internal static void EndExplosion()
    {
        if (ActiveExplosions.Count > 0)
            ActiveExplosions.RemoveAt(ActiveExplosions.Count - 1);
    }

    private static BlastClass Find(Vector3 explosionPosition)
    {
        if (_aircraftBombDepth > 0)
            return BlastClass.AircraftBomb;

        var now = Time.time;
        var bestIndex = -1;
        var bestDistance = float.MaxValue;
        for (var i = PendingRounds.Count - 1; i >= 0; i--)
        {
            var pending = PendingRounds[i];
            if (pending.ExpiresAt < now)
            {
                PendingRounds.RemoveAt(i);
                continue;
            }

            var distance = (pending.Center - explosionPosition).sqrMagnitude;
            if (distance <= pending.MatchRadius * pending.MatchRadius && distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return BlastClass.None;

        var result = PendingRounds[bestIndex].Class;
        PendingRounds.RemoveAt(bestIndex);
        return result;
    }
}

[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.Ragdolize), typeof(Vector3))]
internal static class SmallExplosionAiThrowForcePatch
{
    [HarmonyPrefix]
    private static void Prefix(RagdollManager __instance, ref Vector3 direction)
    {
        // Mortars, artillery, and aircraft bombs have an explicit heavy-ordnance
        // class. The unclassified path covers ordinary grenades and gun/tank HE.
        if (!BlastClassifier.IsExplosionActive ||
            BlastClassifier.CurrentExplosion != BlastClass.None)
        {
            return;
        }

        var multiplier = Settings.SmallExplosionAiThrowForceMultiplier.Value;
        if (Mathf.Approximately(multiplier, 1f))
            return;

        var soldier = __instance.GetComponentInParent<Soldier>();
        if (soldier == null || !soldier.IsAI())
            return;

        var nativeMagnitude = direction.magnitude;
        direction *= multiplier;
        AiState.Trace($"Small-explosion AI throw force: {nativeMagnitude:0.00} -> " +
                      $"{direction.magnitude:0.00} ({multiplier:0.00}x)");
    }
}

internal static class EnhancedFragmentation
{
    private static bool _initialized;
    private static float _nativeFragmentRadiusMultiplier = 1f;
    private static int _explosionSequence;

    internal static void PrepareNativeRadius()
    {
        if (!_initialized)
        {
            _nativeFragmentRadiusMultiplier = Mathf.Max(0.01f,
                Corvostudio.Weapons.Explosion.FRAGMENTS_RADIUS_MULT);
            _initialized = true;
        }

        var configuredMultiplier = Settings.EnhancedFragmentationEnabled.Value
            ? Settings.FragmentRadiusMultiplier.Value
            : 1f;
        var desired = _nativeFragmentRadiusMultiplier * configuredMultiplier;
        if (!Mathf.Approximately(Corvostudio.Weapons.Explosion.FRAGMENTS_RADIUS_MULT, desired))
        {
            Corvostudio.Weapons.Explosion.FRAGMENTS_RADIUS_MULT = desired;
            AiState.Trace($"Native fragment radius multiplier: {_nativeFragmentRadiusMultiplier:0.00} -> {desired:0.00}");
        }
    }

    internal static int ApplyExtraFragmentChecks(
        Vector3 position,
        float explosionMaxDamage,
        float maxPenetration,
        float explosionRadius,
        Soldier responsible,
        HitType hitType)
    {
        var checksPerTarget = Settings.ExtraFragmentChecksPerTarget.Value;
        var damageMultiplier = Settings.ExtraFragmentDamageMultiplier.Value;
        if (checksPerTarget <= 0 || damageMultiplier <= 0f || explosionMaxDamage <= 0f)
            return 0;

        var fragmentRadius = explosionRadius *
                             Mathf.Max(1f, Corvostudio.Weapons.Explosion.FRAGMENTS_RADIUS_MULT);
        var fullDamageRadius = explosionRadius *
                               Mathf.Max(0f, Corvostudio.Weapons.Explosion.FULLDAMAGE_RADIUS_MULT);
        if (fragmentRadius <= 0f)
            return 0;

        var creatures = Creature.aliveCreatures;
        if (creatures == null)
            return 0;

        var candidates = new List<Soldier>();
        foreach (var creature in creatures)
        {
            var soldier = creature as Soldier;
            if (soldier == null || !soldier.IsAlive)
                continue;

            var distanceSqr = (soldier.GetCenterOfUnit() - position).sqrMagnitude;
            if (distanceSqr <= fragmentRadius * fragmentRadius &&
                distanceSqr > fullDamageRadius * fullDamageRadius)
            {
                candidates.Add(soldier);
            }
        }

        var sequence = unchecked(++_explosionSequence);
        var fragmentDamage = explosionMaxDamage * damageMultiplier;
        var hits = 0;
        foreach (var soldier in candidates)
        {
            if (soldier == null || !soldier.IsAlive)
                continue;

            var center = soldier.GetCenterOfUnit();
            var distance = Vector3.Distance(position, center);
            var relativeDistance = explosionRadius / Mathf.Max(explosionRadius, distance);
            var chancePerCheck = Mathf.Clamp01(0.65f * relativeDistance * relativeDistance);
            var seed = HashPosition(position, sequence, soldier.GetInstanceID());

            for (var check = 0; check < checksPerTarget && soldier.IsAlive; check++)
            {
                if (Random01(seed, check * 3) > chancePerCheck)
                    continue;

                var target = FragmentAimPoint(position, center, seed, check);
                var start = position + Vector3.up * 0.15f;
                var ray = target - start;
                var rayDistance = ray.magnitude;
                if (rayDistance <= 0.01f ||
                    !Physics.Raycast(start, ray / rayDistance, out var hit, rayDistance + 0.75f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                var hitCollider = hit.collider;
                if (hitCollider == null)
                    continue;

                var hitSoldier = hitCollider.GetComponentInParent<Soldier>();
                if (hitSoldier != soldier)
                    continue;

                var bodyPart = hitCollider.GetComponentInParent<BodyPart>();
                if (bodyPart == null || !bodyPart.CanBeDamaged())
                    continue;

                // Use the game's own body-part explosion path so armor, faction rules,
                // penetration, hit reactions, and damage multipliers stay native.
                bodyPart.TryDamageWithExplosion(maxPenetration, fragmentDamage, position,
                    responsible, hitType);
                hits++;
            }
        }

        if (hits > 0)
        {
            AiState.Trace($"Extra fragments: radius={fragmentRadius:0.0}m, " +
                          $"candidates={candidates.Count}, hits={hits}");
        }

        return hits;
    }

    private static Vector3 FragmentAimPoint(Vector3 explosion, Vector3 center, uint seed, int check)
    {
        var outward = center - explosion;
        outward.y = 0f;
        var side = outward.sqrMagnitude > 0.001f
            ? Vector3.Cross(Vector3.up, outward.normalized)
            : Vector3.right;
        var heightOffset = Mathf.Lerp(-0.55f, 0.65f, Random01(seed, check * 3 + 1));
        var sideOffset = Mathf.Lerp(-0.3f, 0.3f, Random01(seed, check * 3 + 2));
        return center + Vector3.up * heightOffset + side * sideOffset;
    }

    private static uint HashPosition(Vector3 position, int sequence, int soldierId)
    {
        unchecked
        {
            var hash = (uint)Mathf.RoundToInt(position.x * 10f) * 73856093u;
            hash ^= (uint)Mathf.RoundToInt(position.y * 10f) * 19349663u;
            hash ^= (uint)Mathf.RoundToInt(position.z * 10f) * 83492791u;
            hash ^= (uint)sequence * 2654435761u;
            hash ^= (uint)soldierId;
            return Mix(hash);
        }
    }

    private static float Random01(uint seed, int stream)
    {
        var value = Mix(seed ^ unchecked((uint)stream * 2246822519u));
        return (value & 0x00ffffffu) / 16777215f;
    }

    private static uint Mix(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}

[HarmonyPatch(typeof(Corvostudio.Weapons.Explosion), nameof(Corvostudio.Weapons.Explosion.CreateExplosion))]
internal static class LayeredBlastEffectsPatch
{
    private readonly record struct LayerSettings(
        float InjuryRadiusMultiplier,
        float SuppressionRadiusMultiplier,
        float OuterDamage,
        int Suppression);

    [HarmonyPrefix]
    private static void Prefix(Vector3 position, out BlastClass __state)
    {
        __state = BlastClassifier.BeginExplosion(position);
        if (MultiplayerAuthority.CanMutateGameplay())
            EnhancedFragmentation.PrepareNativeRadius();
    }

    [HarmonyPostfix]
    private static void Postfix(
        Vector3 position,
        float explosionMaxDamage,
        float maxPenetration,
        float explosionRadius,
        Soldier responsible,
        HitType hitType,
        bool canDamage,
        BlastClass __state)
    {
        BlastClassifier.EndExplosion();

        if (explosionRadius <= 0f || !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        HeavyOrdnanceCraterEffects.Apply(position, explosionRadius, __state);

        if (!canDamage)
            return;

        if (Settings.EnhancedFragmentationEnabled.Value)
        {
            EnhancedFragmentation.ApplyExtraFragmentChecks(position, explosionMaxDamage,
                maxPenetration, explosionRadius, responsible, hitType);
        }

        if (!Settings.LayeredBlastEffectsEnabled.Value)
            return;

        var blastClass = __state;
        var layer = GetSettings(blastClass);
        var injuryRadius = explosionRadius * layer.InjuryRadiusMultiplier;
        var suppressionRadius = explosionRadius * layer.SuppressionRadiusMultiplier;
        if (injuryRadius <= explosionRadius && suppressionRadius <= explosionRadius)
            return;

        var creatures = Creature.aliveCreatures;
        if (creatures == null)
            return;

        // Damage can remove a unit from the game's live collection, so copy the
        // candidates before applying any effects.
        var candidates = new List<Soldier>();
        foreach (var creature in creatures)
        {
            var soldier = creature as Soldier;
            if (soldier != null && soldier.IsAlive &&
                (soldier.GetCenterOfUnit() - position).sqrMagnitude <= suppressionRadius * suppressionRadius)
            {
                candidates.Add(soldier);
            }
        }

        foreach (var soldier in candidates)
        {
            if (soldier == null || !soldier.IsAlive)
                continue;

            var center = soldier.GetCenterOfUnit();
            var distance = Vector3.Distance(position, center);
            var exposure = GetExposure(position, center, soldier);

            // The native explosion owns the inner lethal zone. This ring begins
            // outside it and tapers to zero at the configured injury radius.
            if (distance > explosionRadius && distance < injuryRadius && layer.OuterDamage > 0f)
            {
                var injuryT = Mathf.InverseLerp(injuryRadius, explosionRadius, distance);
                var damage = layer.OuterDamage * injuryT * exposure;
                if (damage >= 0.5f)
                    soldier.Damage(damage);
            }

            if (distance < suppressionRadius && layer.Suppression > 0 && soldier.IsAlive)
            {
                var suppressionT = distance <= explosionRadius
                    ? 1f
                    : Mathf.InverseLerp(suppressionRadius, explosionRadius, distance);
                var amount = Mathf.RoundToInt(layer.Suppression * suppressionT * exposure);
                if (amount > 0)
                    IncomingFireAwareness.ApplyNonDirectionalSuppression(soldier, amount, responsible);
            }
        }

        AiState.Trace($"Layered {blastClass} blast: native={explosionRadius:0.0}m, " +
                      $"injury={injuryRadius:0.0}m, suppression={suppressionRadius:0.0}m");
    }

    private static LayerSettings GetSettings(BlastClass blastClass)
    {
        return blastClass switch
        {
            BlastClass.Mortar => new LayerSettings(
                Settings.MortarInjuryRadiusMultiplier.Value,
                Settings.MortarSuppressionRadiusMultiplier.Value,
                Settings.MortarOuterDamage.Value,
                Settings.MortarSuppression.Value),
            BlastClass.AircraftBomb => new LayerSettings(
                Settings.AircraftBombInjuryRadiusMultiplier.Value,
                Settings.AircraftBombSuppressionRadiusMultiplier.Value,
                Settings.AircraftBombOuterDamage.Value,
                Settings.AircraftBombSuppression.Value),
            BlastClass.Artillery => new LayerSettings(
                Settings.ArtilleryInjuryRadiusMultiplier.Value,
                Settings.ArtillerySuppressionRadiusMultiplier.Value,
                Settings.ArtilleryOuterDamage.Value,
                Settings.ArtillerySuppression.Value),
            _ => new LayerSettings(
                1f,
                Settings.GeneralExplosionSuppressionRadiusMultiplier.Value,
                0f,
                Settings.GeneralExplosionSuppression.Value)
        };
    }

    private static float GetExposure(Vector3 explosion, Vector3 target, Soldier soldier)
    {
        var start = explosion + Vector3.up * 0.25f;
        if (!Physics.Linecast(start, target, out var hit, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            return 1f;
        }

        var hitSoldier = hit.collider != null ? hit.collider.GetComponentInParent<Soldier>() : null;
        return hitSoldier == soldier ? 1f : Settings.BlastCoverEffectMultiplier.Value;
    }
}
