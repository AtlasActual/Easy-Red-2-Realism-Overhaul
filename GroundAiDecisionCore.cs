using System;
using System.Collections.Generic;
using System.Linq;

namespace ER2RealismOverhaul;

internal enum StrategicPosture
{
    Attack,
    Defend
}

internal enum CommandChannel
{
    SquadOrders,
    InfantryAssignment,
    VehicleOrders,
    AircraftOrders,
    SupportRequest
}

internal enum CommandAuthority
{
    NativeFallback = 0,
    CommanderIntent = 100,
    ImmediateCombat = 200,
    ProtectedFortification = 300,
    CriticalSuppression = 400,
    RequiredSafety = 500,
    LethalEmergency = 600,
    PlayerOrScript = 700
}

internal readonly record struct CommandLeaseKey(CommandChannel Channel, int EntityId);

internal readonly record struct CommandLeaseRequest(
    CommandChannel Channel,
    int EntityId,
    string Owner,
    CommandAuthority Authority,
    int ObjectiveRevision,
    string Role,
    MapPoint Destination,
    string Constraints,
    float ValidUntil);

internal readonly record struct CommandLease(
    CommandLeaseKey Key,
    string Owner,
    CommandAuthority Authority,
    int ObjectiveRevision,
    string Role,
    MapPoint Destination,
    string Constraints,
    float ValidUntil,
    long Generation)
{
    internal bool IsValid(float now)
        => !float.IsNaN(now) && !float.IsInfinity(now) &&
           (float.IsPositiveInfinity(ValidUntil) || ValidUntil >= now);
}

/// <summary>
/// Pure, deterministic command ownership registry. Runtime adapters store only
/// stable integer entity IDs here, keeping Unity objects out of the planner.
/// </summary>
internal sealed class CommandLeaseRegistryCore
{
    private readonly Dictionary<CommandLeaseKey, CommandLease> _leases = new();
    private long _generation;

    internal int Count => _leases.Count;

    internal bool TryAcquire(CommandLeaseRequest request, float now, out CommandLease lease)
    {
        lease = default;
        if (!Valid(request, now))
            return false;

        var key = new CommandLeaseKey(request.Channel, request.EntityId);
        if (_leases.TryGetValue(key, out var current) && !current.IsValid(now))
            _leases.Remove(key);

        if (_leases.TryGetValue(key, out current))
        {
            // Objective revisions are monotonic. This rejects delayed commander
            // work after a capture or battle-phase transition.
            if (request.ObjectiveRevision < current.ObjectiveRevision)
                return false;

            // External ownership is a latch, not a short priority boost. It has to
            // be explicitly ended before an AI system can reacquire the channel.
            if (current.Authority == CommandAuthority.PlayerOrScript &&
                !string.Equals(current.Owner, request.Owner, StringComparison.Ordinal))
            {
                return false;
            }

            if (request.ObjectiveRevision == current.ObjectiveRevision &&
                request.Authority < current.Authority)
            {
                return false;
            }

            // Equal-priority ownership is sticky. This prevents two feature loops
            // from alternating ownership every update.
            if (request.ObjectiveRevision == current.ObjectiveRevision &&
                request.Authority == current.Authority &&
                !string.Equals(request.Owner, current.Owner, StringComparison.Ordinal))
            {
                return false;
            }

            // A planner heartbeat renews the same ownership decision without
            // creating a new command generation. New generations are reserved for
            // actual changes of objective, role, destination, constraints, owner,
            // or authority so downstream executors do not observe false churn.
            if (SameDecision(current, request))
            {
                var validUntil = Math.Max(current.ValidUntil, request.ValidUntil);
                lease = current with { ValidUntil = validUntil };
                _leases[key] = lease;
                return true;
            }
        }

        lease = new CommandLease(
            key,
            request.Owner,
            request.Authority,
            request.ObjectiveRevision,
            request.Role ?? string.Empty,
            request.Destination,
            request.Constraints ?? string.Empty,
            request.ValidUntil,
            ++_generation);
        _leases[key] = lease;
        return true;
    }

    internal bool TryGet(CommandChannel channel, int entityId, float now, out CommandLease lease)
    {
        var key = new CommandLeaseKey(channel, entityId);
        if (_leases.TryGetValue(key, out lease) && lease.IsValid(now))
            return true;

        _leases.Remove(key);
        lease = default;
        return false;
    }

    internal bool IsCurrent(CommandLease lease, float now)
        => TryGet(lease.Key.Channel, lease.Key.EntityId, now, out var current) &&
           current.Generation == lease.Generation &&
           current.ObjectiveRevision == lease.ObjectiveRevision &&
           string.Equals(current.Owner, lease.Owner, StringComparison.Ordinal);

    internal bool Release(CommandChannel channel, int entityId, string? owner = null)
    {
        var key = new CommandLeaseKey(channel, entityId);
        if (!_leases.TryGetValue(key, out var current) ||
            owner != null && !string.Equals(owner, current.Owner, StringComparison.Ordinal))
        {
            return false;
        }

        return _leases.Remove(key);
    }

    internal void ReleaseEntity(int entityId)
    {
        foreach (var key in _leases.Keys.Where(key => key.EntityId == entityId).ToArray())
            _leases.Remove(key);
    }

    internal void ReleaseOlderThanRevision(int objectiveRevision)
    {
        foreach (var key in _leases.Where(pair => pair.Value.ObjectiveRevision < objectiveRevision)
                     .Select(pair => pair.Key).ToArray())
        {
            _leases.Remove(key);
        }
    }

    internal int ReleaseOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return 0;

        var keys = _leases.Where(pair =>
                string.Equals(pair.Value.Owner, owner, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in keys)
            _leases.Remove(key);
        return keys.Length;
    }

    internal void Clear() => _leases.Clear();

    internal void CopyActive(float now, List<CommandLease> destination)
    {
        destination.Clear();
        List<CommandLeaseKey>? expired = null;
        foreach (var pair in _leases)
        {
            if (pair.Value.IsValid(now))
                destination.Add(pair.Value);
            else
                (expired ??= new List<CommandLeaseKey>()).Add(pair.Key);
        }

        if (expired != null)
        {
            foreach (var key in expired)
                _leases.Remove(key);
        }

        destination.Sort((left, right) =>
        {
            var channel = left.Key.Channel.CompareTo(right.Key.Channel);
            return channel != 0 ? channel : left.Key.EntityId.CompareTo(right.Key.EntityId);
        });
    }

    private static bool SameDecision(CommandLease current, CommandLeaseRequest request)
        => current.ObjectiveRevision == request.ObjectiveRevision &&
           current.Authority == request.Authority &&
           string.Equals(current.Owner, request.Owner, StringComparison.Ordinal) &&
           string.Equals(current.Role, request.Role ?? string.Empty, StringComparison.Ordinal) &&
           current.Destination.Equals(request.Destination) &&
           string.Equals(current.Constraints, request.Constraints ?? string.Empty,
               StringComparison.Ordinal);

    private static bool Valid(CommandLeaseRequest request, float now)
        => request.EntityId != 0 &&
           !string.IsNullOrWhiteSpace(request.Owner) &&
           request.ObjectiveRevision >= 0 &&
           !float.IsNaN(now) && !float.IsInfinity(now) &&
           (float.IsPositiveInfinity(request.ValidUntil) ||
            !float.IsNaN(request.ValidUntil) && !float.IsInfinity(request.ValidUntil) &&
            request.ValidUntil >= now);
}

internal readonly record struct StableDefensiveOrder(
    int ObjectiveId,
    MapPoint Destination,
    float Radius);

internal static class DefensiveOrderStabilityCore
{
    internal static bool ShouldReplace(
        StableDefensiveOrder? current,
        StableDefensiveOrder proposed,
        float destinationTolerance = 10f,
        float radiusTolerance = 4f)
    {
        if (current == null)
            return true;

        var existing = current.Value;
        if (existing.ObjectiveId != proposed.ObjectiveId)
            return true;

        var deltaX = existing.Destination.X - proposed.Destination.X;
        var deltaZ = existing.Destination.Z - proposed.Destination.Z;
        var tolerance = Math.Max(0f, destinationTolerance);
        return deltaX * deltaX + deltaZ * deltaZ > tolerance * tolerance ||
               Math.Abs(existing.Radius - proposed.Radius) > Math.Max(0f, radiusTolerance);
    }
}

internal readonly record struct StableCommanderOrder(
    int ObjectiveId,
    CommanderRole Role,
    CommanderAction Action);

internal static class CommanderOrderStabilityCore
{
    internal static bool ShouldReplace(
        StableCommanderOrder? current,
        StableCommanderOrder proposed,
        bool ignoreRole = false)
    {
        if (current == null)
            return true;

        var existing = current.Value;
        if (existing.ObjectiveId != proposed.ObjectiveId ||
            !ignoreRole && existing.Role != proposed.Role)
        {
            return true;
        }

        // Prepare and Hold are the same stationary command phase. Planner
        // heartbeats may refine their ideal point, but a settled squad must not be
        // sent to a new point until the operation actually enters or leaves attack.
        return IsAttack(existing.Action) != IsAttack(proposed.Action);
    }

    private static bool IsAttack(CommanderAction action)
        => action == CommanderAction.Attack;
}

internal static class ObjectiveFormationCore
{
    private const float GoldenAngleDegrees = 137.50777f;

    internal static MapPoint DefensiveSector(
        MapPoint objective,
        float objectiveRadius,
        int stableSlot)
        => OffsetOnRing(objective, objectiveRadius, stableSlot, 0.45f, 5f, 28f);

    internal static MapPoint AttackEntry(
        MapPoint objective,
        float objectiveRadius,
        int stableSlot)
        => OffsetOnRing(objective, objectiveRadius, stableSlot, 0.35f, 3f, 20f);

    private static MapPoint OffsetOnRing(
        MapPoint objective,
        float objectiveRadius,
        int stableSlot,
        float radiusFraction,
        float minimumOffset,
        float maximumOffset)
    {
        if (!objective.IsFinite || !float.IsFinite(objectiveRadius))
            return objective;

        var radius = Math.Max(0f, objectiveRadius);
        var offset = Math.Clamp(radius * radiusFraction, minimumOffset,
            Math.Min(maximumOffset, Math.Max(minimumOffset, radius * 0.65f)));
        var angle = MathF.PI / 180f *
                    ((Math.Max(0, stableSlot) * GoldenAngleDegrees + 21f) % 360f);
        return new MapPoint(
            objective.X + MathF.Cos(angle) * offset,
            objective.Z + MathF.Sin(angle) * offset);
    }
}

internal static class SquadCohesionCore
{
    internal const float CommanderMemberRadiusMeters = 30f;
    private const float MinimumStationaryAreaRadiusMeters = 14f;
    private const float MaximumStationaryAreaRadiusMeters = 24f;

    // A squad anchored on a dense building or trench line may spread along that
    // whole fortification. Growing the stationary area up to this ceiling makes
    // "cohesion" mean "same building/trench," not "same 24 m disc of grass," while
    // still keeping the compact 14 m floor for lone or sparse positions.
    internal const float MaximumClusterCohesionRadiusMeters = 36f;
    private const int DenseClusterCandidateCount = 12;

    internal static float StationaryAreaRadius(float objectiveRadius, int squadCount)
    {
        if (!float.IsFinite(objectiveRadius))
            return MinimumStationaryAreaRadiusMeters;

        var usableRadius = Math.Max(0f, objectiveRadius);
        var divisor = MathF.Sqrt(Math.Max(1, squadCount));
        return Math.Clamp(
            usableRadius * 0.75f / divisor,
            MinimumStationaryAreaRadiusMeters,
            MaximumStationaryAreaRadiusMeters);
    }

    internal static float ClusterCohesionRadius(float baseRadius, int clusterCandidateCount)
    {
        if (!float.IsFinite(baseRadius))
            return MinimumStationaryAreaRadiusMeters;

        var usableBase = Math.Max(MinimumStationaryAreaRadiusMeters, baseRadius);
        // Sparse areas keep the compact fighting radius; growth is reserved for a
        // genuine cluster the squad can actually occupy shoulder to shoulder.
        if (clusterCandidateCount < CoverClusterCore.MinimumClusterCandidates)
            return usableBase;

        var span = DenseClusterCandidateCount -
                   (CoverClusterCore.MinimumClusterCandidates - 1);
        var progress = Math.Clamp(
            (clusterCandidateCount - (CoverClusterCore.MinimumClusterCandidates - 1)) /
            (float)span,
            0f,
            1f);
        var grown = usableBase +
                    (MaximumClusterCohesionRadiusMeters - usableBase) * progress;
        return Math.Clamp(
            grown,
            MinimumStationaryAreaRadiusMeters,
            MaximumClusterCohesionRadiusMeters);
    }

    internal static bool AllowsCover(
        MapPoint cover,
        MapPoint leader,
        float maximumRadius = CommanderMemberRadiusMeters)
    {
        if (!cover.IsFinite || !leader.IsFinite || !float.IsFinite(maximumRadius))
            return false;

        var allowed = Math.Max(0f, maximumRadius);
        var x = (double)cover.X - leader.X;
        var z = (double)cover.Z - leader.Z;
        return x * x + z * z <= (double)allowed * allowed;
    }
}

internal enum TacticalStance
{
    Standing,
    Crouched,
    Prone
}

internal static class TacticalPoseStabilityCore
{
    // A downward safety reaction may happen immediately. A more exposed stance is
    // deliberately slower so independent native/tactical updates cannot animate a
    // soldier between prone and crouched every frame.
    internal const float MinimumHoldSeconds = 3.5f;

    internal static float RenewHoldUntil(
        float now,
        float holdUntil,
        bool lowerPoseStillOwned)
    {
        if (!lowerPoseStillOwned || !float.IsFinite(now))
            return holdUntil;

        var renewed = now + MinimumHoldSeconds;
        return float.IsNaN(holdUntil) || renewed > holdUntil
            ? renewed
            : holdUntil;
    }

    internal static bool ShouldAccept(
        TacticalStance current,
        TacticalStance proposed,
        float now,
        float holdUntil,
        bool lowerPoseStillOwned)
    {
        if (!float.IsFinite(now) || !float.IsFinite(holdUntil) || current == proposed)
            return false;

        if (ProtectionRank(proposed) > ProtectionRank(current))
            return true;

        return !lowerPoseStillOwned && now >= holdUntil;
    }

    private static int ProtectionRank(TacticalStance stance)
        => stance switch
        {
            TacticalStance.Prone => 2,
            TacticalStance.Crouched => 1,
            _ => 0
        };
}

/// <summary>
/// Pure rule for who owns a soldier's pose while he holds a cover position. The
/// cover-evaluation posture (for example prone on a prone-protective wall) must own
/// the pose for as long as he is holding cover against a known threat. It must NOT be
/// tied to the short contact-halt timer: when it was, the pose source alternated
/// between the evaluation pose and the generic crouch fallback every time the enemy
/// briefly left line of sight, and the asymmetric pose latch (instant drop to prone,
/// slow rise to crouch) turned that alternation into a sustained prone&lt;-&gt;crouch loop.
/// A defensive anchor keeps ownership even when the native cover flag is not reported;
/// an unanchored soldier keeps it while he remains on usable cover.
/// </summary>
internal static class CoverPostureOwnershipCore
{
    internal static bool CoverPoseOwned(
        bool hasThreatMemory,
        bool onUsableCover,
        bool defensiveHold)
        => hasThreatMemory && (defensiveHold || onUsableCover);
}

/// <summary>
/// A soldier already prone in the open under suppression must not be raised to a
/// crouch while the suppression band stays active — rising in the open while still
/// under fire is never the correct reaction, and it is what produced the visible
/// prone&lt;-&gt;crouch rhythm (instant drop to prone on the next pin, forced crouch on
/// release, repeat). Crouch remains correct on usable cover (a parapet protects while
/// still allowing him to fire) and as the downward reaction from standing.
/// </summary>
internal static class SuppressionRecoveryPoseCore
{
    // coverEvaluationOwnsProne: the cover-posture ownership is active AND the cover
    // evaluation measured this slot as only protecting a prone soldier. Raising him
    // to crouch under suppression would then expose him above cover the evaluation
    // already ruled prone-only, so the suppression band must defer to it instead of
    // fighting it every frame (plan 012).
    internal static TacticalStance Resolve(
        bool onUsableCover,
        TacticalStance current,
        bool coverEvaluationOwnsProne)
        => (!onUsableCover && current == TacticalStance.Prone) ||
           (onUsableCover && coverEvaluationOwnsProne)
            ? TacticalStance.Prone
            : TacticalStance.Crouched;
}

internal enum TacticalChannel
{
    Movement,
    Pose,
    FirePermission,
    Aim
}

internal enum TacticalAction
{
    Native,
    Hold,
    Move,
    Stand,
    Crouch,
    Prone,
    AllowFire,
    InhibitFire,
    AimAt
}

internal readonly record struct ContactMovementSensor(
    bool HasActionableContact,
    bool HasRecentContact,
    bool HasCommittedCoverMove,
    bool HasStableCoverHold,
    bool HasTimedCoverHold,
    bool CanClaimReachedCover,
    bool HasEngagementHold,
    bool NeedsDefensivePositionControl = false);

internal readonly record struct SoldierTacticalSnapshot(
    int SoldierId,
    int SquadId,
    int ObjectiveRevision,
    StrategicPosture Posture,
    bool PlayerLed,
    bool ScriptOwned,
    bool Alive,
    bool Mounted,
    bool Suppressed,
    bool NeedsReloadSafety,
    bool LethalHazard,
    MapPoint Position,
    MapPoint ThreatPosition,
    MapPoint HazardPosition = default,
    ContactMovementSensor ContactMovement = default,
    bool Autonomous = false,
    bool HasPlayerHoldOrder = false,
    bool HasProtectedAssignment = false,
    bool TankThreat = false,
    bool HasCommanderIntent = false,
    MapPoint CommanderIntentDestination = default);

/// <summary>
/// Identifies which system submitted a tactical proposal. Member order IS the
/// same-authority tie-break rank (lower value wins when two proposals for the
/// same channel share the same CommandAuthority).
/// </summary>
internal enum ProposalSource
{
    None = 0,
    External,
    PlayerHold,
    Hazard,
    ActionSafety,
    Suppression,
    ProtectedAssignment,
    DefensivePosition,
    CoverHold,
    Contact,
    TankFear,
    Commander,
    Native
}

internal readonly record struct TacticalProposal(
    TacticalChannel Channel,
    TacticalAction Action,
    CommandAuthority Priority,
    ProposalSource Source,
    MapPoint Destination,
    string Constraint);

internal sealed class SoldierTacticalResolution
{
    internal SoldierTacticalSnapshot Snapshot { get; set; }
    internal Dictionary<TacticalChannel, TacticalProposal> Winners { get; }

    internal SoldierTacticalResolution(
        SoldierTacticalSnapshot snapshot,
        Dictionary<TacticalChannel, TacticalProposal>? winners = null)
    {
        Snapshot = snapshot;
        Winners = winners ?? new Dictionary<TacticalChannel, TacticalProposal>();
    }
}

/// <summary>
/// Read-only projection used by diagnostics. Tactical proposal destinations are
/// semantic inputs (for example, a threat position or an empty commander marker),
/// not guaranteed locomotion targets. Only the live executor destination may be
/// presented as the route the soldier is actually following.
/// </summary>
internal readonly record struct MovementDebugProjection(
    ProposalSource Source,
    TacticalAction Action,
    CommandAuthority Authority,
    string Constraint,
    bool HasExecutorDestination,
    MapPoint ExecutorDestination);

internal static class MovementDebugProjectionCore
{
    internal static MovementDebugProjection Project(
        SoldierTacticalResolution? resolution,
        bool hasExecutorDestination,
        MapPoint executorDestination)
    {
        var source = ProposalSource.Native;
        var action = TacticalAction.Native;
        var authority = CommandAuthority.NativeFallback;
        var constraint = string.Empty;
        if (resolution != null &&
            resolution.Winners.TryGetValue(TacticalChannel.Movement, out var winner))
        {
            source = winner.Source;
            action = winner.Action;
            authority = winner.Priority;
            constraint = winner.Constraint;
        }
        var destinationIsValid = hasExecutorDestination && executorDestination.IsFinite;
        return new MovementDebugProjection(
            source,
            action,
            authority,
            constraint,
            destinationIsValid,
            destinationIsValid ? executorDestination : default);
    }
}

internal enum AiDebugScope
{
    All,
    Allies,
    Enemies
}

/// <summary>
/// Keeps the overlay's allegiance policy deterministic and fail-closed. Runtime
/// code supplies the game's hostility result; this core only decides whether a
/// classified actor belongs in the selected view.
/// </summary>
internal static class AiDebugAllegianceCore
{
    internal static bool Includes(
        AiDebugScope scope,
        bool hasReferenceFaction,
        bool candidateFactionKnown,
        bool isEnemy)
    {
        if (scope == AiDebugScope.All)
            return true;
        if (!hasReferenceFaction || !candidateFactionKnown)
            return false;
        return scope == AiDebugScope.Enemies ? isEnemy : !isEnemy;
    }
}

internal static class TacticalArbitrationCore
{
    internal static SoldierTacticalResolution Resolve(
        SoldierTacticalSnapshot snapshot,
        IEnumerable<TacticalProposal>? proposals)
    {
        var resolution = new SoldierTacticalResolution(snapshot);
        ResolveInto(resolution, proposals);
        return resolution;
    }

    // Runtime AI updates reuse this object for the same soldier.  Keeping the
    // allocation-free form separate preserves the simple enumerable API used by
    // deterministic planner tests and other one-shot callers.
    internal static SoldierTacticalResolution ResolveInto(
        SoldierTacticalResolution resolution,
        SoldierTacticalSnapshot snapshot,
        IReadOnlyList<TacticalProposal> proposals)
    {
        resolution.Snapshot = snapshot;
        resolution.Winners.Clear();
        if (!snapshot.Alive)
            return resolution;

        for (var index = 0; index < proposals.Count; index++)
            Consider(resolution.Winners, proposals[index]);

        return resolution;
    }

    private static void ResolveInto(
        SoldierTacticalResolution resolution,
        IEnumerable<TacticalProposal>? proposals)
    {
        resolution.Winners.Clear();
        if (!resolution.Snapshot.Alive)
            return;

        foreach (var proposal in proposals ?? Array.Empty<TacticalProposal>())
            Consider(resolution.Winners, proposal);
    }

    private static void Consider(
        Dictionary<TacticalChannel, TacticalProposal> winners,
        TacticalProposal proposal)
    {
        if (proposal.Source == ProposalSource.None)
            return;

        if (!winners.TryGetValue(proposal.Channel, out var current) ||
            Compare(proposal, current) < 0)
        {
            winners[proposal.Channel] = proposal;
        }
    }

    private static int Compare(TacticalProposal left, TacticalProposal right)
    {
        var priority = right.Priority.CompareTo(left.Priority);
        if (priority != 0)
            return priority;

        var source = left.Source.CompareTo(right.Source);
        if (source != 0)
            return source;

        var action = left.Action.CompareTo(right.Action);
        if (action != 0)
            return action;

        var x = left.Destination.X.CompareTo(right.Destination.X);
        return x != 0 ? x : left.Destination.Z.CompareTo(right.Destination.Z);
    }
}

internal static class CombatMovementPolicyCore
{
    internal static bool NeedsLocalCombatControl(ContactMovementSensor sensor)
        => sensor.HasActionableContact ||
           sensor.HasRecentContact ||
           sensor.HasCommittedCoverMove ||
           sensor.HasTimedCoverHold ||
           sensor.CanClaimReachedCover ||
           sensor.HasEngagementHold;

    internal static bool NeedsProtectedCoverControl(ContactMovementSensor sensor)
        => sensor.HasStableCoverHold;

    internal static bool NeedsDefensivePositionControl(ContactMovementSensor sensor)
        => sensor.NeedsDefensivePositionControl;

    internal static TacticalAction SelectLocalAction(ContactMovementSensor sensor)
        // Contact by itself is a reason to halt, observe, and return fire. Movement
        // is only the selected local action after this system has committed the
        // soldier to a specific cover destination.
        => sensor.HasCommittedCoverMove
            ? TacticalAction.Move
            : TacticalAction.Hold;

    internal static bool ShouldAuthorizeAttackBound(
        bool hasAttackRoute,
        bool coveringFireEstablished,
        bool maximumHaltReached,
        bool underDirectFire,
        bool pinned,
        bool onUsableCover,
        float coverHoldUntil,
        float now)
    {
        if (!hasAttackRoute || underDirectFire || pinned ||
            float.IsNaN(now) || float.IsInfinity(now) ||
            float.IsNaN(coverHoldUntil))
        {
            return false;
        }

        if (onUsableCover)
        {
            if (now < coverHoldUntil)
                return false;

            // A deadline may unstick an exposed assault, but it is never enough by
            // itself to pull a soldier out of a useful fighting position. Leaving
            // real cover requires coordinated covering fire.
            return coveringFireEstablished;
        }

        return coveringFireEstablished || maximumHaltReached;
    }
}

internal static class ExternalMovementPolicyCore
{
    internal static bool AllowsPlayerHoldCover(
        bool playerLed,
        bool scriptOwned,
        bool autonomousSoldier,
        bool validPlayerHold)
        => playerLed && !scriptOwned && autonomousSoldier && validPlayerHold;
}

/// <summary>
/// Settings values consulted while generating tactical proposals. Every
/// Settings.*.Value read inside the old GroundAiDirector.CollectProposals body
/// is represented here so ProposalGenerationCore stays pure.
/// </summary>
internal readonly record struct TacticalPolicyOptions(
    bool ContactResponseEnabled,
    bool TankFearEnabled);

/// <summary>
/// Pure per-soldier movement/pose/fire-permission proposal generator. A
/// line-for-line translation of the former GroundAiDirector.CollectProposals:
/// same branch order, same constraint strings, same authorities. Reads only
/// its snapshot and options arguments — no Settings, no Unity, no Soldier.
/// </summary>
internal static class ProposalGenerationCore
{
    internal static void Collect(
        SoldierTacticalSnapshot snapshot,
        TacticalPolicyOptions options,
        List<TacticalProposal> destination)
    {
        destination.Clear();
        // A mounted crewman is inside the tank, not a foot soldier fleeing one — the
        // proposal would otherwise pull his own vehicle's crew out of their seats.
        var tankThreat = options.TankFearEnabled && snapshot.TankThreat && !snapshot.Mounted;
        destination.Add(new TacticalProposal(
            TacticalChannel.Movement, TacticalAction.Native, CommandAuthority.NativeFallback,
            ProposalSource.Native, default, string.Empty));

        var playerHoldCover = ExternalMovementPolicyCore.AllowsPlayerHoldCover(
            snapshot.PlayerLed,
            snapshot.ScriptOwned,
            snapshot.Autonomous,
            snapshot.HasPlayerHoldOrder);
        if (playerHoldCover)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement,
                snapshot.ContactMovement.HasCommittedCoverMove
                    ? TacticalAction.Move
                    : TacticalAction.Hold,
                CommandAuthority.PlayerOrScript,
                ProposalSource.PlayerHold,
                snapshot.Position,
                "occupy protected positions inside player hold area"));
        }
        else if (snapshot.PlayerLed || snapshot.ScriptOwned)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Native, CommandAuthority.PlayerOrScript,
                ProposalSource.External, default, "preserve player/script squad order"));
        }
        else if (snapshot.LethalHazard)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Move, CommandAuthority.LethalEmergency,
                ProposalSource.Hazard, snapshot.HazardPosition, "temporary emergency override"));
        }
        else if (snapshot.NeedsReloadSafety)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold, CommandAuthority.RequiredSafety,
                ProposalSource.ActionSafety, snapshot.Position, "complete medical or reload action safely"));
        }
        else if (snapshot.Suppressed)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold, CommandAuthority.CriticalSuppression,
                ProposalSource.Suppression, snapshot.Position, "pin in protection"));
        }

        if (snapshot.HasProtectedAssignment)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Move, CommandAuthority.ProtectedFortification,
                ProposalSource.ProtectedAssignment, default, "retain fortification or weapon lease"));
        }

        if (!snapshot.HasProtectedAssignment &&
            CombatMovementPolicyCore.NeedsDefensivePositionControl(
                snapshot.ContactMovement))
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold,
                CommandAuthority.ProtectedFortification,
                ProposalSource.DefensivePosition, snapshot.Position,
                "take one useful defensive position and remain there"));
        }

        if (options.ContactResponseEnabled && !tankThreat &&
            CombatMovementPolicyCore.NeedsProtectedCoverControl(snapshot.ContactMovement))
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Hold,
                CommandAuthority.ProtectedFortification,
                ProposalSource.CoverHold, snapshot.Position, "retain reached fortified cover"));
        }

        if (options.ContactResponseEnabled && !tankThreat &&
            CombatMovementPolicyCore.NeedsLocalCombatControl(snapshot.ContactMovement))
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement,
                CombatMovementPolicyCore.SelectLocalAction(snapshot.ContactMovement),
                CommandAuthority.ImmediateCombat,
                ProposalSource.Contact, snapshot.ThreatPosition, "contact response"));
        }

        if (tankThreat)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Move, CommandAuthority.ImmediateCombat,
                ProposalSource.TankFear, snapshot.ThreatPosition, "armor threat"));
        }

        if (snapshot.HasCommanderIntent)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Movement, TacticalAction.Move, CommandAuthority.CommanderIntent,
                ProposalSource.Commander, snapshot.CommanderIntentDestination,
                "accepted squad intent"));
        }

        if (snapshot.Suppressed)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Pose, TacticalAction.Crouch, CommandAuthority.CriticalSuppression,
                ProposalSource.Suppression, default, "reduce exposure"));
        }

        if (snapshot.NeedsReloadSafety)
        {
            destination.Add(new TacticalProposal(
                TacticalChannel.Pose, TacticalAction.Prone, CommandAuthority.RequiredSafety,
                ProposalSource.ActionSafety, default, "protect required action"));
            destination.Add(new TacticalProposal(
                TacticalChannel.FirePermission, TacticalAction.InhibitFire, CommandAuthority.RequiredSafety,
                ProposalSource.ActionSafety, default, "do not interrupt required action"));
        }
    }
}

internal enum MovementProgressDecision
{
    Reset,
    Observe,
    Progressed,
    Halt
}

internal readonly record struct MovementProgressInput(
    bool ShouldMonitor,
    bool WaitingForPath,
    bool DestinationChanged,
    float PhysicalTravelMeters,
    float SecondsWithoutProgress);

/// <summary>
/// Package-free locomotion stall policy. Reported path distance is deliberately
/// excluded: a repath can alter it while the character remains physically stuck.
/// </summary>
internal static class MovementProgressWatchdogCore
{
    internal const float ProgressEpsilonMeters = 0.35f;
    internal const float DestinationChangeMeters = 1.5f;
    internal const float StallSeconds = 2.75f;
    internal const float PathRequestStallSeconds = 5f;
    internal const float RecoveryHoldSeconds = 4f;

    internal static float RecoverySeconds(int consecutiveFailures)
        => RecoveryHoldSeconds * Math.Clamp(consecutiveFailures, 1, 3);

    internal static MovementProgressDecision Evaluate(MovementProgressInput input)
    {
        if (!input.ShouldMonitor ||
            !float.IsFinite(input.PhysicalTravelMeters) ||
            !float.IsFinite(input.SecondsWithoutProgress))
        {
            return MovementProgressDecision.Reset;
        }

        if (input.DestinationChanged ||
            input.PhysicalTravelMeters >= ProgressEpsilonMeters)
        {
            return MovementProgressDecision.Progressed;
        }

        var limit = input.WaitingForPath ? PathRequestStallSeconds : StallSeconds;
        return input.SecondsWithoutProgress >= limit
            ? MovementProgressDecision.Halt
            : MovementProgressDecision.Observe;
    }
}

internal readonly record struct DefensivePositionOwnershipInput(
    bool AlreadyOwned,
    bool EligibleDefender,
    bool InsideAssignedArea,
    bool SameSquad,
    bool SameObjectiveRevision);

internal static class DefensivePositionOwnershipCore
{
    internal static bool ShouldOwn(DefensivePositionOwnershipInput input)
    {
        if (!input.EligibleDefender)
            return false;

        // Crossing into the assigned area is the one acquisition gate. Once the
        // position has been acquired, native destination/order flicker and a few
        // metres of incidental displacement cannot give locomotion back. Only an
        // actual squad/objective/ownership transition releases it.
        return input.AlreadyOwned
            ? input.SameSquad && input.SameObjectiveRevision
            : input.InsideAssignedArea;
    }
}

internal static class StrategicPostureCore
{
    internal static StrategicPosture FromObjectiveOwnership(bool factionOwnsObjective)
        => factionOwnsObjective ? StrategicPosture.Defend : StrategicPosture.Attack;

    internal static StrategicPosture Resolve(
        bool ownershipKnown,
        bool factionOwnsObjective,
        StrategicPosture? previousPosture,
        StrategicPosture ambiguousFallback)
    {
        if (ownershipKnown)
            return FromObjectiveOwnership(factionOwnsObjective);

        // A contested objective may temporarily report no secured owner. Treating
        // that transient state as enemy ownership makes both sides assault it and
        // tears down every defensive position. Retain the last valid posture.
        return previousPosture ?? ambiguousFallback;
    }
}

internal readonly record struct ObjectiveCandidate(int Id, float Score, bool FriendlySecured);

internal static class ObjectiveSelectionCore
{
    internal const float FriendlySecuredPenaltyMeters = 140f;

    // Returns the chosen candidate Id, or null when candidates is empty.
    internal static int? Select(
        IReadOnlyList<ObjectiveCandidate> candidates,
        bool attackerSide,
        int? currentObjectiveId)
    {
        if (candidates.Count == 0)
            return null;

        if (!attackerSide)
            return SelectByScore(candidates);

        var unsecured = candidates.Where(candidate => !candidate.FriendlySecured).ToArray();
        if (unsecured.Length == 0)
            return SelectByScore(candidates);

        // Flicker guard: once the side has committed to an unsecured objective, a
        // captured objective that transiently reads "unsecured" while contested
        // cannot pull the side back off it.
        if (currentObjectiveId is { } currentId &&
            unsecured.Any(candidate => candidate.Id == currentId))
            return currentId;

        return SelectByScore(unsecured);
    }

    private static int? SelectByScore(IReadOnlyList<ObjectiveCandidate> candidates)
    {
        int? bestId = null;
        var bestScore = float.MaxValue;
        var bestTieId = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var effectiveScore = candidate.Score +
                (candidate.FriendlySecured ? FriendlySecuredPenaltyMeters : 0f);
            if (effectiveScore < bestScore - 0.01f ||
                MathF.Abs(effectiveScore - bestScore) <= 0.01f && candidate.Id < bestTieId)
            {
                bestId = candidate.Id;
                bestScore = effectiveScore;
                bestTieId = candidate.Id;
            }
        }

        return bestId;
    }
}

internal static class GroundAuthorityCore
{
    internal static bool CanMutate(bool multiplayerActive, bool isHost)
        => !multiplayerActive || isHost;
}

internal enum ArmorRoleAssignment
{
    AssaultSupport,
    FlankSupport,
    Reserve
}

internal readonly record struct ArmorTankState(
    int Id,
    float HullFraction,
    float Suppression,
    float EffectivePower);

/// <summary>
/// Persisted allocation state carried between planning ticks. Roles are frozen
/// (sticky) unless <see cref="ArmorRoleAllocationCore"/> detects one of its
/// explicit rebuild triggers — never as a side effect of a continuously varying
/// suppression or power reading.
/// </summary>
internal sealed record ArmorRoleAllocationState(
    IReadOnlyDictionary<int, ArmorRoleAssignment> Roles,
    IReadOnlyCollection<int> ReserveForCause,
    bool MainAxisUsable,
    bool FlankAxisUsable,
    int DesiredReserveCount)
{
    internal static readonly ArmorRoleAllocationState Empty = new(
        new Dictionary<int, ArmorRoleAssignment>(),
        Array.Empty<int>(),
        false,
        false,
        0);
}

/// <summary>
/// Pure armor role allocator. Roles rebuild only when the tank roster changes,
/// a tank's damage/suppression reserve eligibility flips, axis usability
/// changes, or the reserve top-up count changes — no term in that trigger
/// varies continuously with suppression or EffectivePower, so a transient
/// suppression spike cannot rotate tanks through Reserve or swap their axis.
/// </summary>
internal static class ArmorRoleAllocationCore
{
    internal const float ReserveHullEntryThreshold = 0.45f;
    internal const float ReserveSuppressionEntryThreshold = 0.65f;
    internal const float ReserveHullExitThreshold = 0.55f;
    internal const float ReserveSuppressionExitThreshold = 0.45f;
    internal const float ReserveFraction = 0.20f;
    internal const int MinimumTanksForReserveTopUp = 3;

    internal static ArmorRoleAllocationState Allocate(
        IReadOnlyList<ArmorTankState>? tanks,
        ArmorRoleAllocationState previous,
        bool mainAxisUsable,
        bool flankAxisUsable)
    {
        tanks ??= Array.Empty<ArmorTankState>();
        if (tanks.Count == 0)
            return ArmorRoleAllocationState.Empty;

        var previousReserveForCause = previous.ReserveForCause ?? Array.Empty<int>();
        var causeReserveIds = new HashSet<int>();
        var tankSetChanged = tanks.Count != previous.Roles.Count;
        var eligibilityFlipped = false;
        foreach (var tank in tanks)
        {
            var wasCauseReserve = previousReserveForCause.Contains(tank.Id);
            var stillCauseReserve = wasCauseReserve
                ? !(tank.HullFraction >= ReserveHullExitThreshold &&
                    tank.Suppression <= ReserveSuppressionExitThreshold)
                : tank.HullFraction < ReserveHullEntryThreshold ||
                  tank.Suppression > ReserveSuppressionEntryThreshold;

            if (stillCauseReserve)
                causeReserveIds.Add(tank.Id);
            if (stillCauseReserve != wasCauseReserve)
                eligibilityFlipped = true;
            if (!previous.Roles.ContainsKey(tank.Id))
                tankSetChanged = true;
        }

        var desiredReserveCount = tanks.Count >= MinimumTanksForReserveTopUp
            ? (int)Math.Ceiling(tanks.Count * ReserveFraction)
            : 0;
        var axisUsabilityChanged = mainAxisUsable != previous.MainAxisUsable ||
                                    flankAxisUsable != previous.FlankAxisUsable;
        var reserveTopUpChanged = desiredReserveCount != previous.DesiredReserveCount;
        var needsRebuild = previous.Roles.Count == 0 || tankSetChanged || eligibilityFlipped ||
                           axisUsabilityChanged || reserveTopUpChanged;

        if (!needsRebuild)
        {
            return previous with
            {
                ReserveForCause = causeReserveIds,
                MainAxisUsable = mainAxisUsable,
                FlankAxisUsable = flankAxisUsable,
                DesiredReserveCount = desiredReserveCount
            };
        }

        var roles = new Dictionary<int, ArmorRoleAssignment>();
        var available = new List<ArmorTankState>();
        foreach (var tank in tanks.OrderBy(tank => tank.Id))
        {
            if (causeReserveIds.Contains(tank.Id))
                roles[tank.Id] = ArmorRoleAssignment.Reserve;
            else
                available.Add(tank);
        }

        var additionalReserves = Math.Max(0, desiredReserveCount - causeReserveIds.Count);
        foreach (var tank in available
                     .OrderBy(tank => tank.EffectivePower)
                     .ThenBy(tank => tank.Id)
                     .Take(additionalReserves)
                     .ToArray())
        {
            roles[tank.Id] = ArmorRoleAssignment.Reserve;
            available.Remove(tank);
        }

        if (!mainAxisUsable && !flankAxisUsable)
        {
            foreach (var tank in available)
                roles[tank.Id] = ArmorRoleAssignment.Reserve;
        }
        else
        {
            var committedIndex = 0;
            foreach (var tank in available.OrderBy(tank => tank.Id))
            {
                var useFlank = flankAxisUsable && (!mainAxisUsable || committedIndex % 2 == 1);
                roles[tank.Id] = useFlank
                    ? ArmorRoleAssignment.FlankSupport
                    : ArmorRoleAssignment.AssaultSupport;
                committedIndex++;
            }
        }

        return new ArmorRoleAllocationState(
            roles, causeReserveIds, mainAxisUsable, flankAxisUsable, desiredReserveCount);
    }
}

internal static class AttackerPlannerCore
{
    internal static CommanderPlan Plan(CommanderPlanInput input)
        => CommanderPlannerCore.Plan(input with { OffensiveOperation = true });
}

internal static class DefenderPlannerCore
{
    internal static CommanderPlan Plan(CommanderPlanInput input)
    {
        var plan = CommanderPlannerCore.Plan(input with { OffensiveOperation = false });
        var positions = (input.Squads ?? Array.Empty<CommanderSquadSnapshot>())
            .GroupBy(squad => squad.Id)
            .ToDictionary(group => group.Key, group => group.First().Position);
        var directives = plan.Directives
            .Select(directive => directive with
            {
                Action = CommanderAction.Hold,
                AxisId = null,
                Destination = positions.TryGetValue(directive.SquadId, out var position)
                    ? position
                    : input.ObjectivePosition
            })
            .ToArray();

        // Defenders occupy persistent objective sectors. Attack axes belong only
        // to the attacker planner and must never leak into defensive directives.
        return plan with
        {
            MainAxisId = null,
            FlankAxisId = null,
            AttackAuthorized = false,
            Directives = directives
        };
    }
}

internal static class StaticWeaponAssignmentCore
{
    internal static bool SeatPreventsTransit(
        bool seatExists,
        int occupantSoldierId,
        int assignedSoldierId)
        => !seatExists ||
           occupantSoldierId != 0 && occupantSoldierId != assignedSoldierId;

    internal static bool ShouldReassertDestination(
        bool mountedOnAssignedWeapon,
        bool destinationTargetsAssignedWeapon,
        bool emergencyInterrupted)
        => !mountedOnAssignedWeapon && !destinationTargetsAssignedWeapon &&
           !emergencyInterrupted;
}

internal readonly record struct DefenderSquadCandidate(
    int SquadId,
    float EffectiveStrength,
    int CombatReadyOnFoot,
    bool PlannedReserve,
    bool ExternallyOwned);

internal readonly record struct DefenderCrewCandidate(
    int SoldierId,
    int SquadId,
    bool IsLeader,
    bool IsMedic,
    bool IsRadioman,
    bool HasHandheldAntiTank,
    bool Reachable);

internal readonly record struct DefensiveWeaponCandidate(
    int WeaponId,
    bool Viable,
    bool ArmorPiercing,
    float Caliber,
    float AmmunitionScore,
    float ThreatCoverage,
    float InfantryApproachCoverage);

internal readonly record struct DefensiveWeaponAssignment(int WeaponId, int SoldierId, int SquadId);

internal sealed record DefenderAllocationPlan(
    IReadOnlyList<int> ReserveSquadIds,
    IReadOnlyList<DefensiveWeaponAssignment> WeaponAssignments,
    IReadOnlyList<int> UnstaffedWeaponIds);

internal static class DefenderAllocationCore
{
    internal static DefenderAllocationPlan Allocate(
        IReadOnlyList<DefenderSquadCandidate>? squads,
        IReadOnlyList<DefenderCrewCandidate>? crews,
        IReadOnlyList<DefensiveWeaponCandidate>? weapons,
        bool armorReported)
    {
        var availableSquads = (squads ?? Array.Empty<DefenderSquadCandidate>())
            .Where(squad => squad.SquadId != 0 && !squad.ExternallyOwned &&
                            squad.CombatReadyOnFoot > 0 && float.IsFinite(squad.EffectiveStrength))
            .GroupBy(squad => squad.SquadId)
            .Select(group => group.OrderByDescending(squad => squad.EffectiveStrength).First())
            .ToArray();
        var reserveCount = availableSquads.Length >= 2
            ? Math.Max(1, (int)Math.Ceiling(availableSquads.Length * CommanderPlannerCore.ReserveFraction))
            : 0;
        var reserve = availableSquads
            .OrderByDescending(squad => squad.PlannedReserve)
            .ThenByDescending(squad => squad.EffectiveStrength)
            .ThenBy(squad => squad.SquadId)
            .Take(reserveCount)
            .Select(squad => squad.SquadId)
            .ToHashSet();

        var squadsById = availableSquads.ToDictionary(squad => squad.SquadId);
        var onFootRemaining = availableSquads.ToDictionary(
            squad => squad.SquadId, squad => squad.CombatReadyOnFoot);
        var specialistRemaining = new Dictionary<(int SquadId, int Kind), int>();
        var crewPool = (crews ?? Array.Empty<DefenderCrewCandidate>())
            .Where(crew => crew.SoldierId != 0 && crew.SquadId != 0 && crew.Reachable &&
                           !crew.IsLeader && squadsById.ContainsKey(crew.SquadId) &&
                           !reserve.Contains(crew.SquadId))
            .GroupBy(crew => crew.SoldierId)
            .Select(group => group.OrderBy(crew => crew.SquadId).First())
            .ToArray();

        foreach (var squadId in squadsById.Keys)
        {
            specialistRemaining[(squadId, 0)] = crewPool.Count(crew => crew.SquadId == squadId && crew.IsMedic);
            specialistRemaining[(squadId, 1)] = crewPool.Count(crew => crew.SquadId == squadId && crew.IsRadioman);
            specialistRemaining[(squadId, 2)] = crewPool.Count(crew => crew.SquadId == squadId && crew.HasHandheldAntiTank);
        }

        var orderedWeapons = (weapons ?? Array.Empty<DefensiveWeaponCandidate>())
            .Where(weapon => weapon.WeaponId != 0 && weapon.Viable &&
                             float.IsFinite(weapon.Caliber) &&
                             float.IsFinite(weapon.AmmunitionScore) &&
                             float.IsFinite(weapon.ThreatCoverage) &&
                             float.IsFinite(weapon.InfantryApproachCoverage))
            .GroupBy(weapon => weapon.WeaponId)
            .Select(group => group.First())
            .OrderByDescending(weapon => armorReported && weapon.ArmorPiercing)
            .ThenByDescending(weapon => armorReported && weapon.ArmorPiercing ? weapon.Caliber : 0f)
            .ThenByDescending(weapon => armorReported ? weapon.ThreatCoverage : weapon.InfantryApproachCoverage)
            .ThenByDescending(weapon => weapon.AmmunitionScore)
            .ThenBy(weapon => weapon.WeaponId)
            .ToArray();

        var assignments = new List<DefensiveWeaponAssignment>();
        var assignedSoldiers = new HashSet<int>();
        var unstaffed = new List<int>();
        foreach (var weapon in orderedWeapons)
        {
            DefenderCrewCandidate? selected = null;
            foreach (var crew in crewPool
                         .Where(crew => !assignedSoldiers.Contains(crew.SoldierId) &&
                                        onFootRemaining[crew.SquadId] > 3 &&
                                        SpecialistCanLeave(crew, specialistRemaining))
                         .OrderBy(crew => SpecialistCount(crew))
                         .ThenByDescending(crew => onFootRemaining[crew.SquadId])
                         .ThenBy(crew => crew.SquadId)
                         .ThenBy(crew => crew.SoldierId))
            {
                selected = crew;
                break;
            }

            if (selected is not { } gunner)
            {
                unstaffed.Add(weapon.WeaponId);
                continue;
            }

            assignments.Add(new DefensiveWeaponAssignment(
                weapon.WeaponId, gunner.SoldierId, gunner.SquadId));
            assignedSoldiers.Add(gunner.SoldierId);
            onFootRemaining[gunner.SquadId]--;
            DecrementSpecialists(gunner, specialistRemaining);
        }

        return new DefenderAllocationPlan(
            reserve.OrderBy(id => id).ToArray(), assignments, unstaffed);
    }

    private static int SpecialistCount(DefenderCrewCandidate crew)
        => (crew.IsMedic ? 1 : 0) + (crew.IsRadioman ? 1 : 0) +
           (crew.HasHandheldAntiTank ? 1 : 0);

    private static bool SpecialistCanLeave(
        DefenderCrewCandidate crew,
        IReadOnlyDictionary<(int SquadId, int Kind), int> remaining)
        => (!crew.IsMedic || remaining[(crew.SquadId, 0)] > 1) &&
           (!crew.IsRadioman || remaining[(crew.SquadId, 1)] > 1) &&
           (!crew.HasHandheldAntiTank || remaining[(crew.SquadId, 2)] > 1);

    private static void DecrementSpecialists(
        DefenderCrewCandidate crew,
        IDictionary<(int SquadId, int Kind), int> remaining)
    {
        if (crew.IsMedic)
            remaining[(crew.SquadId, 0)]--;
        if (crew.IsRadioman)
            remaining[(crew.SquadId, 1)]--;
        if (crew.HasHandheldAntiTank)
            remaining[(crew.SquadId, 2)]--;
    }
}

internal readonly record struct FortifiedCoverSlot(
    int SlotId,
    MapPoint Position,
    float Protection,
    float FiringLaneQuality,
    float RouteExposure,
    float DistanceMeters,
    bool Viable);

internal sealed record FortifiedPosition(
    int PositionId,
    IReadOnlyList<int> SlotIds,
    MapPoint Center,
    float Score);

internal static class FortifiedPositionCore
{
    internal const float MinimumSlotSeparationMeters = 1.75f;
    internal const float ReplacementDelaySeconds = 15f;
    internal const float ReplacementImprovementRatio = 1.25f;

    internal static float Score(FortifiedCoverSlot slot, float searchRadius, int capacity = 1)
    {
        if (!slot.Viable || !slot.Position.IsFinite || !float.IsFinite(searchRadius) || searchRadius <= 0f ||
            !float.IsFinite(slot.Protection) || !float.IsFinite(slot.FiringLaneQuality) ||
            !float.IsFinite(slot.RouteExposure) || !float.IsFinite(slot.DistanceMeters))
        {
            return float.NegativeInfinity;
        }

        var protection = Math.Clamp(slot.Protection, 0f, 1f);
        var firing = Math.Clamp(slot.FiringLaneQuality, 0f, 1f);
        var exposure = Math.Clamp(slot.RouteExposure, 0f, 1f);
        var distance = Math.Clamp(slot.DistanceMeters / searchRadius, 0f, 1f);
        var capacityScore = Math.Clamp((Math.Max(1, capacity) - 1f) / 5f, 0f, 1f);
        return protection * 0.42f + firing * 0.32f + capacityScore * 0.10f +
               (1f - exposure) * 0.10f + (1f - distance) * 0.06f;
    }

    internal static bool ShouldReplace(
        float currentScore,
        float alternativeScore,
        float secondsWithoutRelevantFiringLane,
        bool currentDestroyedOrUnsafe)
    {
        if (currentDestroyedOrUnsafe)
            return float.IsFinite(alternativeScore);
        if (!float.IsFinite(currentScore) || !float.IsFinite(alternativeScore) ||
            !float.IsFinite(secondsWithoutRelevantFiringLane))
        {
            return false;
        }

        return secondsWithoutRelevantFiringLane >= ReplacementDelaySeconds &&
               alternativeScore >= Math.Max(0f, currentScore) * ReplacementImprovementRatio;
    }

    internal static IReadOnlyList<FortifiedPosition> Group(
        IReadOnlyList<FortifiedCoverSlot>? slots,
        float groupingDistance,
        float searchRadius)
    {
        if (slots == null || !float.IsFinite(groupingDistance) || groupingDistance <= 0f)
            return Array.Empty<FortifiedPosition>();

        var viable = slots.Where(slot => slot.Viable && slot.Position.IsFinite)
            .GroupBy(slot => slot.SlotId).Select(group => group.First())
            .OrderBy(slot => slot.SlotId).ToArray();
        var unvisited = viable.Select(slot => slot.SlotId).ToHashSet();
        var byId = viable.ToDictionary(slot => slot.SlotId);
        var result = new List<FortifiedPosition>();
        var rangeSqr = groupingDistance * groupingDistance;

        while (unvisited.Count > 0)
        {
            var root = unvisited.Min();
            unvisited.Remove(root);
            var queue = new Queue<int>();
            queue.Enqueue(root);
            var members = new List<FortifiedCoverSlot>();
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                var slot = byId[id];
                members.Add(slot);
                foreach (var otherId in unvisited.ToArray())
                {
                    var other = byId[otherId];
                    var dx = slot.Position.X - other.Position.X;
                    var dz = slot.Position.Z - other.Position.Z;
                    if (dx * dx + dz * dz > rangeSqr)
                        continue;
                    unvisited.Remove(otherId);
                    queue.Enqueue(otherId);
                }
            }

            var center = new MapPoint(
                members.Average(slot => slot.Position.X),
                members.Average(slot => slot.Position.Z));
            var score = members.Average(slot => Score(slot, searchRadius, members.Count));
            result.Add(new FortifiedPosition(
                members.Min(slot => slot.SlotId),
                members.Select(slot => slot.SlotId).OrderBy(id => id).ToArray(),
                center,
                score));
        }

        return result.OrderByDescending(position => position.Score)
            .ThenBy(position => position.PositionId).ToArray();
    }
}

internal sealed class SupportRequestBrokerCore
{
    private readonly HashSet<(int ObjectiveRevision, int SideId, int RequestId)> _accepted = new();

    internal bool TryAccept(int objectiveRevision, int sideId, int requestId)
    {
        if (objectiveRevision < 0 || sideId == 0 || requestId == 0)
            return false;
        return _accepted.Add((objectiveRevision, sideId, requestId));
    }

    internal void AdvanceRevision(int objectiveRevision)
    {
        _accepted.RemoveWhere(entry => entry.ObjectiveRevision < objectiveRevision);
    }

    internal void Clear() => _accepted.Clear();
}
