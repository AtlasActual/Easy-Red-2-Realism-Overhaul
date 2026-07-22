using System.Text;
using Corvostudio.Weapons;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal enum PenetrationSurface
{
    Foliage,
    Glass,
    Wood,
    Earth,
    ThinMetal,
    Masonry,
    Generic
}

internal readonly struct PenetrationSurfaceProfile
{
    internal PenetrationSurfaceProfile(
        PenetrationSurface surface,
        string name,
        float ordinaryResistance,
        float apResistance,
        float surfaceCost,
        float ordinaryMaximumDepth,
        float apMaximumDepth)
    {
        Surface = surface;
        Name = name;
        OrdinaryResistance = ordinaryResistance;
        ApResistance = apResistance;
        SurfaceCost = surfaceCost;
        OrdinaryMaximumDepth = ordinaryMaximumDepth;
        ApMaximumDepth = apMaximumDepth;
    }

    internal PenetrationSurface Surface { get; }
    internal string Name { get; }
    internal float OrdinaryResistance { get; }
    internal float ApResistance { get; }
    internal float SurfaceCost { get; }
    internal float OrdinaryMaximumDepth { get; }
    internal float ApMaximumDepth { get; }
}

internal readonly record struct BallisticCoverLineEvaluation(
    float ProtectionFraction,
    PenetrationSurface DominantSurface,
    bool HasObstruction);

internal sealed class BulletPenetrationState
{
    internal bool IsArmorPiercing;
    internal float InitialBudget;
    internal float RemainingBudget;
    internal float CumulativeEnergy = 1f;
    internal int PropsCrossed;
    internal int AddedRicochets;
    internal bool RicochetedThisImpact;
    internal PenetrationImpact? PreparedImpact;
    internal RicochetImpact? PreparedRicochet;
    internal float LastSeenAt;

    internal BulletPenetrationState CopyForNextProjectile()
    {
        return new BulletPenetrationState
        {
            IsArmorPiercing = IsArmorPiercing,
            InitialBudget = InitialBudget,
            RemainingBudget = RemainingBudget,
            CumulativeEnergy = CumulativeEnergy,
            PropsCrossed = PropsCrossed,
            AddedRicochets = AddedRicochets,
            LastSeenAt = Time.time
        };
    }
}

internal readonly struct RicochetImpact
{
    internal RicochetImpact(
        Vector3 point,
        Vector3 incomingDirection,
        Vector3 normal,
        float entrySpeed,
        float chance,
        float velocityRetention,
        float scatter,
        string materialName)
    {
        Point = point;
        IncomingDirection = incomingDirection;
        Normal = normal;
        EntrySpeed = entrySpeed;
        Chance = chance;
        VelocityRetention = velocityRetention;
        Scatter = scatter;
        MaterialName = materialName;
    }

    internal Vector3 Point { get; }
    internal Vector3 IncomingDirection { get; }
    internal Vector3 Normal { get; }
    internal float EntrySpeed { get; }
    internal float Chance { get; }
    internal float VelocityRetention { get; }
    internal float Scatter { get; }
    internal string MaterialName { get; }
}

internal readonly struct PenetrationImpact
{
    internal PenetrationImpact(
        Vector3 entryPosition,
        Vector3 entryNormal,
        Vector3 exitPosition,
        Vector3 direction,
        float entrySpeed,
        float resistanceCost,
        float thickness,
        string materialName,
        ImpactType impactType,
        PenetrationSurface surface,
        Transform surfaceTransform)
    {
        EntryPosition = entryPosition;
        EntryNormal = entryNormal;
        ExitPosition = exitPosition;
        Direction = direction;
        EntrySpeed = entrySpeed;
        ResistanceCost = resistanceCost;
        Thickness = thickness;
        MaterialName = materialName;
        ImpactType = impactType;
        Surface = surface;
        SurfaceTransform = surfaceTransform;
    }

    internal Vector3 EntryPosition { get; }
    internal Vector3 EntryNormal { get; }
    internal Vector3 ExitPosition { get; }
    internal Vector3 Direction { get; }
    internal float EntrySpeed { get; }
    internal float ResistanceCost { get; }
    internal float Thickness { get; }
    internal string MaterialName { get; }
    internal ImpactType ImpactType { get; }
    internal PenetrationSurface Surface { get; }
    internal Transform SurfaceTransform { get; }
}

internal readonly struct PenetrationSpawnRequest
{
    internal PenetrationSpawnRequest(
        Vector3 position,
        Vector3 direction,
        float speed,
        BulletData bulletData,
        Soldier shooter,
        BulletPenetrationState state)
    {
        Position = position;
        Direction = direction;
        Speed = speed;
        BulletData = bulletData;
        Shooter = shooter;
        State = state;
    }

    internal Vector3 Position { get; }
    internal Vector3 Direction { get; }
    internal float Speed { get; }
    internal BulletData BulletData { get; }
    internal Soldier Shooter { get; }
    internal BulletPenetrationState State { get; }
}

internal readonly struct ImpactHoleSpawnRequest
{
    internal ImpactHoleSpawnRequest(
        string prefabName,
        Il2CppSystem.Action<GameObject, float> callback,
        Vector3 position,
        Vector3 normal,
        float scale,
        Transform parent)
    {
        PrefabName = prefabName;
        Callback = callback;
        Position = position;
        Normal = normal;
        Scale = scale;
        Parent = parent;
    }

    internal string PrefabName { get; }
    internal Il2CppSystem.Action<GameObject, float> Callback { get; }
    internal Vector3 Position { get; }
    internal Vector3 Normal { get; }
    internal float Scale { get; }
    internal Transform Parent { get; }
}

internal static class BulletPenetration
{
    private const float ExitEpsilon = 0.045f;
    private const float MinimumExitSpeed = 90f;
    private const int MaximumQueuedContinuations = 256;
    // Bullet construction can allocate physics state, effects, and native objects.
    // Keep the critical continuation responsive without allowing a dense burst to
    // construct several projectiles in the same frame.
    private const int MaximumContinuationsSpawnedPerFrame = 2;
    private const int MaximumQueuedImpactHoles = 64;
    private const int MaximumImpactHolesSpawnedPerFrame = 2;
    private const string SmallImpactHolePrefab = "TankHole_Small";
    private const string NormalImpactHolePrefab = "TankHole_Normal";
    private const string ImpactHoleBundle = "er2fxs";

    private static readonly Dictionary<IntPtr, BulletPenetrationState> States = new();
    private static readonly Queue<PenetrationSpawnRequest> SpawnQueue = new();
    private static readonly Queue<ImpactHoleSpawnRequest> ImpactHoleSpawnQueue = new();
    private static readonly List<IntPtr> StaleTokens = new();
    private static readonly Il2CppSystem.Action<GameObject, float> SmallImpactHoleSpawned =
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<GameObject, float>>(
            new Action<GameObject, float>(OnSmallImpactHoleSpawned))!;
    private static readonly Il2CppSystem.Action<GameObject, float> NormalImpactHoleSpawned =
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<GameObject, float>>(
            new Action<GameObject, float>(OnNormalImpactHoleSpawned))!;

    private static readonly string[] HardStopNames =
    {
        "bunker", "pillbox", "casemate", "blockhouse", "fortification",
        "reinforced", "foundation", "bedrock", "boulder", " rock ", "cliff", "mountain",
        "terrain", "landscape", "ground_mesh", "water", "ocean", "sea"
    };

    private static readonly string[] NaturalGroundNames =
    {
        " ground ", "ground_", "_ground", "dirt", "earth", "soil"
    };

    private static readonly string[] DiscreteEarthPropNames =
    {
        "sandbag", "sand_bag"
    };

    private static readonly string[] WaterNames =
    {
        "water", "ocean", " sea "
    };

    private static readonly string[] FoliageNames =
    {
        "foliage", "leaf", "leaves", "bush", "brush", "grass", "hedge",
        "canvas", "cloth", "curtain", "tent", "hay", "straw"
    };

    private static readonly string[] GlassNames =
    {
        "glass", "window", "windshield", "windscreen"
    };

    private static readonly string[] WoodNames =
    {
        "wood", "timber", "plank", "board", "door", "shutter", "crate",
        "furniture", "table", "chair", "cabinet", "tree", "log"
    };

    private static readonly string[] EarthNames =
    {
        "sandbag", "sand_bag", "dirt", "earth", "soil", "trench", "mound",
        "crater", "emplacement"
    };

    private static readonly string[] MetalNames =
    {
        "metal", "steel", "iron", "aluminium", "aluminum", "sheet", "barrel",
        "rail", "fence", "wire", "tin"
    };

    private static readonly string[] MasonryNames =
    {
        "concrete", "cement", "brick", "masonry", "stone", "rubble", "wall",
        "building", "bridge"
    };

    [ThreadStatic]
    private static BulletPenetrationState? _pendingSpawnState;

    [ThreadStatic]
    private static HashSet<IntPtr>? _coverRayColliderTokens;

    [ThreadStatic]
    private static RaycastHit[]? _coverRayHits;

    [ThreadStatic]
    private static RaycastHit[]? _blockingRayHits;

    private static bool _loggedFailure;
    private static float _nextPruneAt;

    internal static void BeginProcessFrame(BulletInstance bullet)
    {
        try
        {
            var token = bullet.Pointer;
            if (token == IntPtr.Zero || States.ContainsKey(token))
                return;

            if (_pendingSpawnState != null)
            {
                States[token] = _pendingSpawnState;
                _pendingSpawnState = null;
                return;
            }

            if (!Settings.BulletPenetrationEnabled.Value || !MultiplayerAuthority.CanMutateGameplay() ||
                !bullet.canDamage)
            {
                return;
            }

            var speed = bullet.curVelocity.magnitude;
            if (speed <= 1f)
                speed = bullet.startVelocity.magnitude;
            if (!TryCreateInitialState(bullet, bullet.bulletData, speed, out var state))
            {
                return;
            }

            States[token] = state;
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
        }
    }

    internal static void PrepareImpact(
        BulletInstance bullet,
        Vector3 hitDirection,
        float impactSpeed,
        BulletData bulletData,
        RaycastHit hit)
    {
        try
        {
            if (!Settings.BulletPenetrationEnabled.Value || !MultiplayerAuthority.CanMutateGameplay() ||
                !bullet.canDamage || bulletData == null)
            {
                return;
            }

            if (!TryGetOrCreateState(bullet, bulletData, impactSpeed, out var state))
                return;

            state.PreparedImpact = null;
            state.PreparedRicochet = null;
            state.RicochetedThisImpact = false;
            state.LastSeenAt = Time.time;

            if (state.PropsCrossed >= Settings.MaximumPropPenetrations.Value)
                return;

            var collider = hit.collider;
            if (collider == null || collider.isTrigger || IsCharacterStop(collider))
                return;

            var vehicleStop = IsVehicleStop(collider);
            PenetrationSurfaceProfile profile;
            if (vehicleStop)
            {
                profile = ProfileFor(PenetrationSurface.ThinMetal);
            }
            else if (!TryClassifySurface(collider, out profile))
            {
                return;
            }

            var velocity = bullet.curVelocity;
            var direction = velocity.sqrMagnitude > 0.01f
                ? velocity.normalized
                : hitDirection.sqrMagnitude > 0.01f
                    ? hitDirection.normalized
                    : Vector3.zero;
            if (direction == Vector3.zero)
                return;

            // At very shallow incidence the base game gets first refusal through its
            // native ricochet path. If it declines, the long oblique material path will
            // normally consume the penetration budget below.
            var incidence = Mathf.Abs(Vector3.Dot(direction, hit.normal));
            var speed = Mathf.Max(impactSpeed, velocity.magnitude);
            PrepareAddedRicochet(state, profile, hit.point, direction, hit.normal, speed, incidence);

            if (incidence < 0.035f || vehicleStop || IsEnvironmentalPenetrationStop(collider))
                return;

            var maximumDepth = state.IsArmorPiercing
                ? profile.ApMaximumDepth
                : profile.OrdinaryMaximumDepth;
            maximumDepth = Mathf.Min(maximumDepth, MaximumDepthAllowedByBudget(state, profile));
            if (maximumDepth <= 0.003f ||
                !TryFindExit(
                    collider,
                    hit.point,
                    direction,
                    maximumDepth,
                    state.IsArmorPiercing,
                    out var exitPoint,
                    out var thickness))
            {
                return;
            }

            var materialResistance = state.IsArmorPiercing
                ? profile.ApResistance
                : profile.OrdinaryResistance;
            var resistanceCost = profile.SurfaceCost + thickness * materialResistance;
            if (!float.IsFinite(resistanceCost) || resistanceCost > state.RemainingBudget + 0.0001f)
                return;

            if (speed < MinimumExitSpeed)
                return;

            state.PreparedImpact = new PenetrationImpact(
                hit.point,
                hit.normal,
                exitPoint + direction * ExitEpsilon,
                direction,
                speed,
                resistanceCost,
                thickness,
                profile.Name,
                ResolveImpactType(collider, profile),
                profile.Surface,
                collider.transform);
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
        }
    }

    internal static void FinishImpact(BulletInstance bullet, BulletData bulletData)
    {
        try
        {
            if (!States.TryGetValue(bullet.Pointer, out var state))
                return;

            var prepared = state.PreparedImpact;
            var preparedRicochet = state.PreparedRicochet;
            state.PreparedImpact = null;
            state.PreparedRicochet = null;
            if (state.RicochetedThisImpact || bulletData == null)
                return;

            if (!prepared.HasValue)
            {
                if (preparedRicochet.HasValue)
                    TryQueueAddedRicochet(bullet, bulletData, state, preparedRicochet.Value);
                return;
            }

            var impact = prepared.Value;
            state.RemainingBudget = Mathf.Max(0f, state.RemainingBudget - impact.ResistanceCost);
            state.PropsCrossed++;

            var budgetFraction = impact.ResistanceCost / Mathf.Max(0.01f, state.InitialBudget);
            var energyLoss = state.IsArmorPiercing
                ? Mathf.Clamp(0.035f + budgetFraction * 0.30f, 0.035f, 0.38f)
                : Mathf.Clamp(0.075f + budgetFraction * 0.52f, 0.075f, 0.72f);
            var retainedEnergy = 1f - energyLoss;
            state.CumulativeEnergy *= retainedEnergy;

            var exitSpeed = impact.EntrySpeed * Mathf.Sqrt(retainedEnergy);
            if (exitSpeed < MinimumExitSpeed || state.RemainingBudget <= 0.001f)
                return;

            var continuedData = CloneBulletData(
                bulletData,
                exitSpeed,
                state.CumulativeEnergy,
                bullet.use_tracer);
            var nextState = state.CopyForNextProjectile();
            var continuationQueued = false;
            if (SpawnQueue.Count < MaximumQueuedContinuations)
            {
                SpawnQueue.Enqueue(new PenetrationSpawnRequest(
                    impact.ExitPosition,
                    impact.Direction,
                    exitSpeed,
                    continuedData,
                    bullet.shooter,
                    nextState));
                continuationQueued = true;
            }

            // OnHit already produced the native entry effect. Recreate the same
            // material family at the back face without calling BulletDamage again.
            if (continuationQueued)
            {
                PlayExitImpactEffect(impact);
                if (state.IsArmorPiercing)
                    PlayArmorPiercingImpactHoles(impact, bulletData.caliber);
            }

            if (Settings.VerboseLogging.Value)
            {
                Plugin.LogSource.LogInfo(
                    $"Bullet penetrated {impact.Thickness:0.000} m of {impact.MaterialName}; " +
                    $"speed={impact.EntrySpeed:0}->{exitSpeed:0} m/s, " +
                    $"budget={state.RemainingBudget:0.00}/{state.InitialBudget:0.00}, " +
                    $"props={state.PropsCrossed}");
            }
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
        }
    }

    internal static bool BeginRicochetAttempt(BulletInstance bullet)
    {
        try
        {
            _pendingSpawnState = null;
            if (!Settings.BulletPenetrationEnabled.Value)
                return true;

            if (States.TryGetValue(bullet.Pointer, out var state))
            {
                // A measured, affordable exit means the round can pass through this
                // prop. Do not let the native ricochet test replace that continuation.
                if (state.PreparedImpact.HasValue)
                    return false;

                var ricochetState = state.CopyForNextProjectile();
                ricochetState.RemainingBudget *= 0.82f;
                ricochetState.CumulativeEnergy *= 0.82f;
                _pendingSpawnState = ricochetState;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
            return true;
        }
    }

    internal static void FinishRicochetAttempt(BulletInstance bullet, bool ricocheted)
    {
        try
        {
            _pendingSpawnState = null;
            if (!ricocheted || !States.TryGetValue(bullet.Pointer, out var state))
                return;

            state.RicochetedThisImpact = true;
            state.PreparedImpact = null;
            state.PreparedRicochet = null;
            if (Settings.VerboseLogging.Value)
                Plugin.LogSource.LogInfo("Native projectile ricochet preserved; penetration continuation suppressed for this impact.");
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
        }
    }

    internal static void ProjectileFinished(BulletInstance bullet, bool finished)
    {
        if (!finished)
            return;

        try
        {
            var token = bullet.Pointer;
            States.Remove(token);
        }
        catch (Exception ex)
        {
            LogFailureOnce(ex);
        }
    }

    internal static void DrainSpawnQueue()
    {
        var spawned = 0;
        while (SpawnQueue.Count > 0 && spawned++ < MaximumContinuationsSpawnedPerFrame)
        {
            var request = SpawnQueue.Dequeue();
            try
            {
                _pendingSpawnState = request.State;
                _ = new BulletInstance(
                    request.Position,
                    request.Direction,
                    request.Speed,
                    request.BulletData,
                    request.Shooter,
                    true,
                    true,
                    null);
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
            }
            finally
            {
                _pendingSpawnState = null;
            }
        }

        DrainImpactHoleSpawnQueue();
        PruneStaleStates();
    }

    internal static void ResetWhenInactive()
    {
        SpawnQueue.Clear();
        ImpactHoleSpawnQueue.Clear();
        States.Clear();
        _pendingSpawnState = null;
    }

    internal static BallisticCoverLineEvaluation EvaluateOrdinaryCoverLine(
        Vector3 bodyPosition,
        Vector3 threatPosition)
    {
        // Cast from the protected body toward the threat, as the existing cover
        // test did. Stopping short of the threat avoids crediting terrain or the
        // target's immediate surroundings as cover for the evaluated soldier.
        var ray = threatPosition - bodyPosition;
        var distance = ray.magnitude;
        const float threatEndpointTolerance = 1.25f;
        var castDistance = distance - threatEndpointTolerance;
        if (castDistance <= 0.1f)
            return default;

        var direction = ray / distance;
        var hits = _coverRayHits ??= new RaycastHit[64];
        var hitCount = Physics.RaycastNonAlloc(
            bodyPosition,
            direction,
            hits,
            castDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        if (hitCount == 0)
            return default;

        var initialBudget = BallisticCoverDecisionCore.RepresentativeOrdinaryRoundBudget *
                            Mathf.Max(0f, Settings.OrdinaryRoundPenetrationStrength.Value);
        var penetrationEnabled = Settings.BulletPenetrationEnabled.Value;
        var maximumPenetrations = Mathf.Max(1, Settings.MaximumPropPenetrations.Value);
        var cumulativeResistance = 0f;
        var penetrableBarriers = 0;
        var dominantResistance = 0f;
        var dominantSurface = PenetrationSurface.Generic;
        var hasObstruction = false;
        var colliderTokens = _coverRayColliderTokens ??= new HashSet<IntPtr>();
        colliderTokens.Clear();

        for (var i = 0; i < hitCount; i++)
        {
            var collider = hits[i].collider;
            if (collider == null || collider.isTrigger || IsCharacterStop(collider))
                continue;

            var token = collider.Pointer;
            if (token != IntPtr.Zero && !colliderTokens.Add(token))
                continue;

            if (IsVehicleStop(collider))
            {
                return new BallisticCoverLineEvaluation(
                    1f, PenetrationSurface.ThinMetal, true);
            }

            if (!TryClassifySurface(collider, out var profile))
                continue;

            hasObstruction = true;
            if (!penetrationEnabled || initialBudget <= 0.001f ||
                IsEnvironmentalPenetrationStop(collider))
            {
                return new BallisticCoverLineEvaluation(1f, profile.Surface, true);
            }

            // Once an ordinary projectile has already crossed the configured
            // number of props, the next recognized barrier is a complete stop.
            if (penetrableBarriers >= maximumPenetrations)
            {
                return new BallisticCoverLineEvaluation(1f, profile.Surface, true);
            }

            var remainingBudget = Mathf.Max(0f, initialBudget - cumulativeResistance);
            var maximumDepth = profile.OrdinaryResistance <= 0.0001f
                ? profile.OrdinaryMaximumDepth
                : Mathf.Max(0f, remainingBudget - profile.SurfaceCost) /
                  profile.OrdinaryResistance;
            maximumDepth = Mathf.Min(maximumDepth, profile.OrdinaryMaximumDepth);
            if (maximumDepth <= 0.003f ||
                !TryFindExit(
                    collider,
                    hits[i].point,
                    direction,
                    maximumDepth,
                    false,
                    out _,
                    out var thickness))
            {
                return new BallisticCoverLineEvaluation(1f, profile.Surface, true);
            }

            var resistance = BallisticCoverDecisionCore.ResistanceCost(
                profile.SurfaceCost,
                profile.OrdinaryResistance,
                thickness);
            cumulativeResistance += resistance;
            penetrableBarriers++;
            if (resistance > dominantResistance)
            {
                dominantResistance = resistance;
                dominantSurface = profile.Surface;
            }

            var protection = BallisticCoverDecisionCore.ProtectionFromResistance(
                initialBudget,
                cumulativeResistance);
            if (protection >= 0.999f)
            {
                return new BallisticCoverLineEvaluation(
                    1f, dominantSurface, true);
            }
        }

        return new BallisticCoverLineEvaluation(
            BallisticCoverDecisionCore.ProtectionFromResistance(
                initialBudget,
                cumulativeResistance),
            dominantSurface,
            hasObstruction);
    }

    private static bool TryGetOrCreateState(
        BulletInstance bullet,
        BulletData bulletData,
        float impactSpeed,
        out BulletPenetrationState state)
    {
        var token = bullet.Pointer;
        if (States.TryGetValue(token, out state!))
            return true;

        if (!TryCreateInitialState(bullet, bulletData, impactSpeed, out state))
            return false;

        States[token] = state;
        return true;
    }

    private static bool TryCreateInitialState(
        BulletInstance bullet,
        BulletData bulletData,
        float projectileSpeed,
        out BulletPenetrationState state)
    {
        state = null!;
        if (bulletData == null || !TryClassifyProjectile(bulletData, out var armorPiercing))
            return false;

        var speed = Mathf.Max(1f, projectileSpeed > 1f ? projectileSpeed : bulletData.speed);
        float budget;
        if (armorPiercing)
        {
            var nativePenetrationScale = Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(20f, bulletData.mmPenetration) / 55f),
                0.72f,
                3.2f);
            budget = 8.5f * nativePenetrationScale * Settings.ArmorPiercingPropPenetrationStrength.Value;
        }
        else
        {
            var massScale = Mathf.Clamp(bulletData.mass_grains / 150f, 0.20f, 2.75f);
            var speedScale = Mathf.Clamp(speed / 800f, 0.25f, 1.65f);
            var energyScale = Mathf.Clamp(Mathf.Pow(massScale * speedScale * speedScale, 0.35f), 0.38f, 1.95f);
            budget = 0.82f * energyScale * Settings.OrdinaryRoundPenetrationStrength.Value;
        }

        state = new BulletPenetrationState
        {
            IsArmorPiercing = armorPiercing,
            InitialBudget = budget,
            RemainingBudget = budget,
            LastSeenAt = Time.time
        };
        return budget > 0.001f;
    }

    private static bool TryClassifyProjectile(BulletData bulletData, out bool armorPiercing)
    {
        armorPiercing = false;
        try
        {
            // Small-arms bullets and large solid shot can share the AP shell enum.
            // Keep ordinary small arms energy-scaled, but give genuinely penetrative
            // or large-caliber solid AP shot the prop-clearing budget. Explosive
            // HEAT/APHE projectiles retain their native impact/detonation behavior.
            var solidApShot = bulletData.shellType == ShellType.AP;
            armorPiercing = solidApShot &&
                (bulletData.IsAPullet() || bulletData.IsHighCaliber() || bulletData.IsVeryHighCaliber());
            if (armorPiercing)
                return true;

            if (bulletData.IsShotgun() || bulletData.IsSmoke() || bulletData.IsFire() ||
                bulletData.IsFlak() || bulletData.IsHe() || bulletData.IsVeryHighCaliber())
            {
                return false;
            }

            return bulletData.mass_grains > 0 && bulletData.speed > 0;
        }
        catch
        {
            return false;
        }
    }

    private static float MaximumDepthAllowedByBudget(
        BulletPenetrationState state,
        PenetrationSurfaceProfile profile)
    {
        var resistance = state.IsArmorPiercing ? profile.ApResistance : profile.OrdinaryResistance;
        return resistance <= 0.0001f
            ? 12f
            : Mathf.Max(0f, state.RemainingBudget - profile.SurfaceCost) / resistance;
    }

    private static bool TryFindExit(
        Collider collider,
        Vector3 entry,
        Vector3 direction,
        float maximumDepth,
        bool allowApTraversal,
        out Vector3 exit,
        out float thickness)
    {
        exit = entry;
        thickness = 0f;

        var probeDistance = maximumDepth + 0.08f;
        var ray = new Ray(entry + direction * probeDistance, -direction);
        if (collider.Raycast(ray, out var exitHit, probeDistance + 0.12f))
        {
            var measuredDepth = Vector3.Dot(exitHit.point - entry, direction);
            if (measuredDepth >= 0.003f && measuredDepth <= maximumDepth + 0.02f &&
                !HasBlockingColliderBetween(collider, entry, direction, measuredDepth, allowApTraversal))
            {
                exit = exitHit.point;
                thickness = measuredDepth;
                return true;
            }
        }

        // Box colliders occasionally decline a reverse ray when the visible face is
        // an extremely thin plane. Large solid/AP rounds also get a conservative
        // bounds fallback for compound penetrable props of any visual type.
        if ((!allowApTraversal && collider.TryCast<BoxCollider>() == null) ||
            !TryBoundsExit(collider.bounds, entry, direction, out exit, out thickness))
        {
            return false;
        }

        return thickness >= 0.003f && thickness <= maximumDepth + 0.02f &&
               !HasBlockingColliderBetween(collider, entry, direction, thickness, allowApTraversal);
    }

    private static void PrepareAddedRicochet(
        BulletPenetrationState state,
        PenetrationSurfaceProfile profile,
        Vector3 point,
        Vector3 incomingDirection,
        Vector3 normal,
        float entrySpeed,
        float incidence)
    {
        if (!Settings.AddedSmallArmsRicochetsEnabled.Value || state.AddedRicochets >= 3 ||
            entrySpeed < MinimumExitSpeed)
        {
            return;
        }

        float maximumIncidence;
        float maximumChance;
        float minimumRetention;
        float maximumRetention;
        float scatter;
        switch (profile.Surface)
        {
            case PenetrationSurface.ThinMetal:
                maximumIncidence = 0.48f;
                maximumChance = 0.82f;
                minimumRetention = 0.62f;
                maximumRetention = 0.87f;
                scatter = 0.045f;
                break;
            case PenetrationSurface.Masonry:
                maximumIncidence = 0.39f;
                maximumChance = 0.64f;
                minimumRetention = 0.52f;
                maximumRetention = 0.78f;
                scatter = 0.075f;
                break;
            case PenetrationSurface.Earth:
                maximumIncidence = 0.27f;
                maximumChance = 0.34f;
                minimumRetention = 0.43f;
                maximumRetention = 0.65f;
                scatter = 0.11f;
                break;
            case PenetrationSurface.Wood:
                maximumIncidence = 0.19f;
                maximumChance = 0.18f;
                minimumRetention = 0.40f;
                maximumRetention = 0.58f;
                scatter = 0.12f;
                break;
            case PenetrationSurface.Generic:
                maximumIncidence = 0.31f;
                maximumChance = 0.40f;
                minimumRetention = 0.48f;
                maximumRetention = 0.70f;
                scatter = 0.09f;
                break;
            case PenetrationSurface.Glass:
                maximumIncidence = 0.13f;
                maximumChance = 0.08f;
                minimumRetention = 0.70f;
                maximumRetention = 0.85f;
                scatter = 0.055f;
                break;
            default:
                return;
        }

        if (incidence >= maximumIncidence)
            return;

        var grazingFraction = 1f - incidence / maximumIncidence;
        var chance = maximumChance * Mathf.Pow(grazingFraction, 0.65f);
        if (state.IsArmorPiercing)
            chance *= 1.08f;
        chance = Mathf.Clamp01(chance * Settings.AddedRicochetChanceMultiplier.Value);
        var retention = Mathf.Lerp(minimumRetention, maximumRetention, grazingFraction);
        state.PreparedRicochet = new RicochetImpact(
            point,
            incomingDirection,
            normal,
            entrySpeed,
            chance,
            retention,
            scatter,
            profile.Name);
    }

    private static bool TryQueueAddedRicochet(
        BulletInstance bullet,
        BulletData bulletData,
        BulletPenetrationState state,
        RicochetImpact impact)
    {
        if (impact.Chance <= 0f || UnityEngine.Random.value > impact.Chance)
            return false;

        var reflected = Vector3.Reflect(impact.IncomingDirection, impact.Normal).normalized;
        var scattered = (reflected + UnityEngine.Random.insideUnitSphere * impact.Scatter).normalized;
        var incomingNormalSign = Vector3.Dot(impact.IncomingDirection, impact.Normal);
        if (scattered == Vector3.zero ||
            Vector3.Dot(scattered, impact.Normal) * incomingNormalSign >= 0f)
        {
            scattered = reflected;
        }

        var velocityRetention = Mathf.Clamp(
            impact.VelocityRetention * UnityEngine.Random.Range(0.96f, 1.025f),
            0.35f,
            0.92f);
        var retainedEnergy = velocityRetention * velocityRetention;
        var exitSpeed = impact.EntrySpeed * velocityRetention;
        if (exitSpeed < MinimumExitSpeed)
            return false;

        state.AddedRicochets++;
        state.RemainingBudget *= retainedEnergy;
        state.CumulativeEnergy *= retainedEnergy;
        var continuedData = CloneBulletData(
            bulletData,
            exitSpeed,
            state.CumulativeEnergy,
            bullet.use_tracer);
        var nextState = state.CopyForNextProjectile();
        if (SpawnQueue.Count < MaximumQueuedContinuations)
        {
            SpawnQueue.Enqueue(new PenetrationSpawnRequest(
                impact.Point + scattered * 0.065f,
                scattered,
                exitSpeed,
                continuedData,
                bullet.shooter,
                nextState));
        }

        if (Settings.VerboseLogging.Value)
        {
            Plugin.LogSource.LogInfo(
                $"Added small-arms ricochet from {impact.MaterialName}; " +
                $"speed={impact.EntrySpeed:0}->{exitSpeed:0} m/s, chance={impact.Chance:P0}");
        }

        return true;
    }

    private static bool TryBoundsExit(
        Bounds bounds,
        Vector3 entry,
        Vector3 direction,
        out Vector3 exit,
        out float thickness)
    {
        var origin = entry + direction * 0.002f;
        var maximum = bounds.max;
        var minimum = bounds.min;
        var distance = float.PositiveInfinity;

        if (direction.x > 0.00001f)
            distance = Mathf.Min(distance, (maximum.x - origin.x) / direction.x);
        else if (direction.x < -0.00001f)
            distance = Mathf.Min(distance, (minimum.x - origin.x) / direction.x);

        if (direction.y > 0.00001f)
            distance = Mathf.Min(distance, (maximum.y - origin.y) / direction.y);
        else if (direction.y < -0.00001f)
            distance = Mathf.Min(distance, (minimum.y - origin.y) / direction.y);

        if (direction.z > 0.00001f)
            distance = Mathf.Min(distance, (maximum.z - origin.z) / direction.z);
        else if (direction.z < -0.00001f)
            distance = Mathf.Min(distance, (minimum.z - origin.z) / direction.z);

        if (!float.IsFinite(distance) || distance < 0f)
        {
            exit = entry;
            thickness = 0f;
            return false;
        }

        thickness = distance + 0.002f;
        exit = entry + direction * thickness;
        return true;
    }

    private static bool HasBlockingColliderBetween(
        Collider source,
        Vector3 entry,
        Vector3 direction,
        float exitDepth,
        bool allowPenetrableOverlaps)
    {
        if (exitDepth <= 0.04f)
            return false;

        var hits = _blockingRayHits ??= new RaycastHit[32];
        var hitCount = Physics.RaycastNonAlloc(
            entry + direction * 0.012f,
            direction,
            hits,
            Mathf.Max(0f, exitDepth - 0.024f),
            ~0,
            QueryTriggerInteraction.Ignore);
        for (var i = 0; i < hitCount; i++)
        {
            var other = hits[i].collider;
            if (other == null || other.Pointer == source.Pointer || BelongsToSameProp(source, other))
                continue;

            if (allowPenetrableOverlaps && IsApTraversableOverlap(other))
                continue;

            return true;
        }

        return false;
    }

    private static bool BelongsToSameProp(Collider first, Collider second)
    {
        var firstBody = first.attachedRigidbody;
        var secondBody = second.attachedRigidbody;
        if (firstBody != null && secondBody != null && firstBody.Pointer == secondBody.Pointer)
            return true;

        var firstImpact = first.GetComponentInParent<BuildingImpact>();
        var secondImpact = second.GetComponentInParent<BuildingImpact>();
        return firstImpact != null && secondImpact != null && firstImpact.Pointer == secondImpact.Pointer;
    }

    private static bool IsApTraversableOverlap(Collider collider)
    {
        if (collider.isTrigger || IsCharacterStop(collider) || IsVehicleStop(collider) ||
            IsEnvironmentalPenetrationStop(collider))
        {
            return false;
        }

        return TryClassifySurface(collider, out _);
    }

    private static bool IsCharacterStop(Collider collider)
    {
        return collider.GetComponentInParent<BodyPart>() != null ||
            collider.GetComponentInParent<ItemHelmet>() != null ||
            collider.GetComponentInParent<Soldier>() != null;
    }

    private static bool IsVehicleStop(Collider collider) =>
        collider.GetComponentInParent<Vehicle>() != null ||
        collider.GetComponentInParent<VehicleDamagablePart>() != null;

    private static bool IsEnvironmentalPenetrationStop(Collider collider)
    {
        if (IsTerrainCollider(collider))
            return true;

        var buildingImpact = collider.GetComponentInParent<BuildingImpact>();
        if (buildingImpact != null &&
            (buildingImpact.impactType == ImpactType.terrain || buildingImpact.impactType == ImpactType.water))
        {
            return true;
        }

        var names = BuildSurfaceDescription(collider);
        if (ContainsAny(names, HardStopNames))
            return true;

        // Natural terrain meshes are effectively bottomless for this system. Do
        // not use a collider-bounds exit to let a projectile emerge through the
        // underside of a soil or dirt mesh. Freestanding sandbags remain measurable
        // earth props and can still be crossed when the projectile has enough energy.
        return ContainsAny(names, NaturalGroundNames) &&
               !ContainsAny(names, DiscreteEarthPropNames);
    }

    private static bool IsTerrainCollider(Collider collider)
    {
        var objectClass = collider.ObjectClass;
        if (objectClass == IntPtr.Zero)
            return false;

        return string.Equals(
                IL2CPP.il2cpp_class_get_name_(objectClass),
                nameof(TerrainCollider),
                StringComparison.Ordinal) &&
            string.Equals(
                IL2CPP.il2cpp_class_get_namespace_(objectClass),
                "UnityEngine",
                StringComparison.Ordinal);
    }

    private static ImpactType ResolveImpactType(
        Collider collider,
        PenetrationSurfaceProfile profile)
    {
        var buildingImpact = collider.GetComponentInParent<BuildingImpact>();
        if (buildingImpact != null)
            return buildingImpact.impactType;

        return profile.Surface switch
        {
            PenetrationSurface.Wood => ImpactType.wood,
            PenetrationSurface.ThinMetal => ImpactType.metal,
            PenetrationSurface.Masonry => ImpactType.cement,
            PenetrationSurface.Earth => ImpactType.terrain,
            _ => ImpactType.generic
        };
    }

    private static void PlayExitImpactEffect(PenetrationImpact impact)
    {
        try
        {
            var effectName = ImpactDatabase.GetImpactID(impact.ImpactType, out var bundle);
            if (string.IsNullOrEmpty(effectName))
                return;

            var exitFace = impact.ExitPosition - impact.Direction * (ExitEpsilon - 0.008f);
            var rotation = Quaternion.FromToRotation(Vector3.up, impact.Direction);
            ResourcesManager.PlayImpactEffect(
                exitFace,
                rotation,
                effectName,
                false,
                false,
                bundle);
        }
        catch (Exception ex)
        {
            // A missing optional effect must never suppress the actual continuation.
            if (Settings.VerboseLogging.Value)
                Plugin.LogSource.LogWarning($"Could not create penetration exit effect: {ex.Message}");
        }
    }

    private static void PlayArmorPiercingImpactHoles(PenetrationImpact impact, float caliber)
    {
        // The native OnHit path can return before it reaches its persistent-hole
        // block when an AP shell crosses a nondamageable structural prop. In that
        // case particles still play, but the entry mark is absent. Spawn both faces
        // only after a continuation was successfully queued so stopped impacts keep
        // the game's original decal behavior.
        if (impact.Surface is PenetrationSurface.Foliage or PenetrationSurface.Glass or PenetrationSurface.Earth)
            return;

        var prefabName = caliber >= 20f ? NormalImpactHolePrefab : SmallImpactHolePrefab;
        var callback = caliber >= 20f ? NormalImpactHoleSpawned : SmallImpactHoleSpawned;
        var scale = caliber >= 20f
            ? Mathf.Clamp(caliber / 75f, 0.55f, 1.35f)
            : Mathf.Clamp(caliber / 12f, 0.35f, 0.85f);

        var entryPosition = impact.EntryPosition + impact.EntryNormal * 0.006f;
        var exitPosition = impact.ExitPosition - impact.Direction * (ExitEpsilon - 0.006f);
        QueueImpactHole(prefabName, callback, entryPosition, impact.EntryNormal, scale, impact.SurfaceTransform);
        QueueImpactHole(prefabName, callback, exitPosition, impact.Direction, scale, impact.SurfaceTransform);
    }

    private static void QueueImpactHole(
        string prefabName,
        Il2CppSystem.Action<GameObject, float> callback,
        Vector3 position,
        Vector3 normal,
        float scale,
        Transform parent)
    {
        if (ImpactHoleSpawnQueue.Count >= MaximumQueuedImpactHoles)
            return;

        ImpactHoleSpawnQueue.Enqueue(new ImpactHoleSpawnRequest(
            prefabName,
            callback,
            position,
            normal,
            scale,
            parent));
    }

    private static void DrainImpactHoleSpawnQueue()
    {
        var spawned = 0;
        while (ImpactHoleSpawnQueue.Count > 0 && spawned++ < MaximumImpactHolesSpawnedPerFrame)
            SpawnImpactHole(ImpactHoleSpawnQueue.Dequeue());
    }

    private static void SpawnImpactHole(ImpactHoleSpawnRequest request)
    {
        try
        {
            if (ResourcesManager.instance == null || request.Normal.sqrMagnitude < 0.01f)
                return;

            var rotation = Quaternion.FromToRotation(Vector3.up, request.Normal.normalized) *
                           Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), Vector3.up);
            var routine = PrefabPool.PopOrInstantiateAsync(
                request.PrefabName,
                request.Position,
                rotation,
                Vector3.one * request.Scale,
                request.Parent,
                request.Callback,
                ImpactHoleBundle,
                string.Empty);
            ResourcesManager.instance.StartCoroutine(routine);
        }
        catch (Exception ex)
        {
            if (Settings.VerboseLogging.Value)
                Plugin.LogSource.LogWarning($"Could not create AP penetration decal: {ex.Message}");
        }
    }

    private static void OnSmallImpactHoleSpawned(GameObject impactHole, float suggestedDespawnTime) =>
        PoolImpactHole(impactHole, SmallImpactHolePrefab, suggestedDespawnTime);

    private static void OnNormalImpactHoleSpawned(GameObject impactHole, float suggestedDespawnTime) =>
        PoolImpactHole(impactHole, NormalImpactHolePrefab, suggestedDespawnTime);

    private static void PoolImpactHole(GameObject impactHole, string prefabName, float suggestedDespawnTime)
    {
        if (impactHole == null)
            return;

        var lifetime = Settings.HitDecalDurationSeconds.Value;
        PrefabPool.PoolObject(impactHole, prefabName, lifetime, 0f);
    }

    private static bool TryClassifySurface(Collider collider, out PenetrationSurfaceProfile profile)
    {
        var buildingImpact = collider.GetComponentInParent<BuildingImpact>();
        if (buildingImpact != null)
        {
            switch (buildingImpact.impactType)
            {
                case ImpactType.wood:
                    profile = ProfileFor(PenetrationSurface.Wood);
                    return true;
                case ImpactType.metal:
                    profile = ProfileFor(PenetrationSurface.ThinMetal);
                    return true;
                case ImpactType.cement:
                    profile = ProfileFor(PenetrationSurface.Masonry);
                    return true;
                case ImpactType.terrain:
                    profile = ProfileFor(PenetrationSurface.Earth);
                    return true;
                case ImpactType.water:
                    profile = default;
                    return false;
            }
        }

        var names = BuildSurfaceDescription(collider);
        if (ContainsAny(names, WaterNames))
        {
            profile = default;
            return false;
        }

        if (ContainsAny(names, HardStopNames))
            profile = ProfileFor(ContainsAny(names, EarthNames) ? PenetrationSurface.Earth : PenetrationSurface.Masonry);
        else if (ContainsAny(names, FoliageNames))
            profile = ProfileFor(PenetrationSurface.Foliage);
        else if (ContainsAny(names, GlassNames))
            profile = ProfileFor(PenetrationSurface.Glass);
        else if (ContainsAny(names, WoodNames))
            profile = ProfileFor(PenetrationSurface.Wood);
        else if (ContainsAny(names, EarthNames))
            profile = ProfileFor(PenetrationSurface.Earth);
        else if (ContainsAny(names, MetalNames))
            profile = ProfileFor(PenetrationSurface.ThinMetal);
        else if (ContainsAny(names, MasonryNames))
            profile = ProfileFor(PenetrationSurface.Masonry);
        else
            profile = ProfileFor(PenetrationSurface.Generic);

        return true;
    }

    private static PenetrationSurfaceProfile ProfileFor(PenetrationSurface surface)
    {
        // These figures are intentionally expressed as an energy budget rather than
        // copied per-material pass counts. AP uses both a larger starting budget and
        // lower resistance against ordinary prop materials.
        return surface switch
        {
            PenetrationSurface.Foliage => new(surface, "foliage/canvas", 0.16f, 0.07f, 0.015f, 1.8f, 10f),
            PenetrationSurface.Glass => new(surface, "glass", 0.28f, 0.10f, 0.025f, 0.75f, 6f),
            PenetrationSurface.Wood => new(surface, "wood", 1.00f, 0.38f, 0.075f, 0.72f, 8f),
            PenetrationSurface.Earth => new(surface, "earth/sandbag", 2.80f, 1.15f, 0.12f, 0.28f, 2.2f),
            PenetrationSurface.ThinMetal => new(surface, "metal", 4.60f, 1.45f, 0.14f, 0.15f, 3.5f),
            PenetrationSurface.Masonry => new(surface, "masonry", 6.20f, 2.05f, 0.18f, 0.14f, 3.2f),
            _ => new(surface, "generic prop", 1.65f, 0.58f, 0.09f, 0.48f, 6f)
        };
    }

    private static string BuildSurfaceDescription(Collider collider)
    {
        var description = new StringBuilder(160);
        try
        {
            var transform = collider.transform;
            for (var depth = 0; transform != null && depth < 6; depth++, transform = transform.parent)
            {
                description.Append(' ').Append(transform.name);
            }

            var physicsMaterial = collider.sharedMaterial;
            if (physicsMaterial != null)
                description.Append(' ').Append(physicsMaterial.name);

            description.Append(' ').Append(LayerMask.LayerToName(collider.gameObject.layer));
        }
        catch
        {
            // Component names are advisory. Unknown props retain the conservative
            // generic-material profile.
        }

        return description.ToString().ToLowerInvariant();
    }

    private static bool ContainsAny(string value, string[] terms)
    {
        for (var i = 0; i < terms.Length; i++)
        {
            if (value.Contains(terms[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static BulletData CloneBulletData(
        BulletData source,
        float speed,
        float cumulativeEnergy,
        bool preserveTracer)
    {
        var scaledPenetration = Mathf.Max(0, Mathf.RoundToInt(source.mmPenetration * cumulativeEnergy));
        var scaledPenetrationDamage = source.penetrationDamage >= 0f
            ? source.penetrationDamage * cumulativeEnergy
            : -1f;
        var clone = new BulletData(
            Mathf.Max(1, Mathf.RoundToInt(speed)),
            source.shellType,
            source.caliber,
            source.mass_grains,
            scaledPenetration,
            preserveTracer,
            source.tracer_prefab,
            source.behaviour,
            source.explosion_radius,
            source.explosion_penetration,
            source.explosion_particle_prefab,
            scaledPenetrationDamage,
            source.explosionDamage);
        clone.firedShells = source.firedShells;
        return clone;
    }

    private static void PruneStaleStates()
    {
        var now = Time.time;
        if (now < _nextPruneAt)
            return;

        _nextPruneAt = now + 2f;
        StaleTokens.Clear();
        foreach (var pair in States)
        {
            if (now - pair.Value.LastSeenAt > 15f)
                StaleTokens.Add(pair.Key);
        }

        for (var i = 0; i < StaleTokens.Count; i++)
            States.Remove(StaleTokens[i]);
    }

    private static void LogFailureOnce(Exception ex)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Plugin.LogSource.LogWarning($"Bullet penetration encountered an error and left the native projectile unchanged: {ex.Message}");
    }
}

internal sealed class BulletPenetrationController : MonoBehaviour
{
    public BulletPenetrationController(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        if (Settings.BulletPenetrationEnabled.Value && MultiplayerAuthority.CanMutateGameplay())
            BulletPenetration.DrainSpawnQueue();
        else
            BulletPenetration.ResetWhenInactive();
    }
}

[HarmonyPatch(typeof(BulletInstance), nameof(BulletInstance.OnHit))]
internal static class BulletInstancePenetrationImpactPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        BulletInstance __instance,
        Vector3 hitDirection,
        float impactSpeed,
        BulletData bulletData,
        RaycastHit hit)
    {
        BulletPenetration.PrepareImpact(__instance, hitDirection, impactSpeed, bulletData, hit);
    }

    [HarmonyPostfix]
    private static void Postfix(BulletInstance __instance, BulletData bulletData)
    {
        BulletPenetration.FinishImpact(__instance, bulletData);
    }
}

[HarmonyPatch(typeof(BulletInstance), nameof(BulletInstance.TryRichochet))]
internal static class BulletInstancePenetrationRicochetPatch
{
    [HarmonyPrefix]
    private static bool Prefix(BulletInstance __instance, ref bool __result)
    {
        var runNativeRicochet = BulletPenetration.BeginRicochetAttempt(__instance);
        if (!runNativeRicochet)
            __result = false;
        return runNativeRicochet;
    }

    [HarmonyPostfix]
    private static void Postfix(BulletInstance __instance, bool __result) =>
        BulletPenetration.FinishRicochetAttempt(__instance, __result);
}

[HarmonyPatch(typeof(BulletInstance), nameof(BulletInstance.ProcessFrame))]
internal static class BulletInstancePenetrationLifecyclePatch
{
    [HarmonyPrefix]
    private static void Prefix(BulletInstance __instance) =>
        BulletPenetration.BeginProcessFrame(__instance);

    [HarmonyPostfix]
    private static void Postfix(BulletInstance __instance, bool __result) =>
        BulletPenetration.ProjectileFinished(__instance, __result);
}
