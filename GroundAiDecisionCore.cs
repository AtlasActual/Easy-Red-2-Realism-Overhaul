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
    bool NeedsMedicalSafety,
    bool NeedsReloadSafety,
    bool LethalHazard,
    MapPoint Position,
    MapPoint ThreatPosition,
    MapPoint HazardPosition = default,
    ContactMovementSensor ContactMovement = default);

internal readonly record struct TacticalProposal(
    TacticalChannel Channel,
    TacticalAction Action,
    CommandAuthority Priority,
    string Source,
    MapPoint Destination,
    string Constraint);

internal sealed record SoldierTacticalResolution(
    SoldierTacticalSnapshot Snapshot,
    IReadOnlyDictionary<TacticalChannel, TacticalProposal> Winners);

internal static class TacticalArbitrationCore
{
    internal static SoldierTacticalResolution Resolve(
        SoldierTacticalSnapshot snapshot,
        IEnumerable<TacticalProposal>? proposals)
    {
        var winners = new Dictionary<TacticalChannel, TacticalProposal>();
        if (!snapshot.Alive)
            return new SoldierTacticalResolution(snapshot, winners);

        foreach (var proposal in proposals ?? Array.Empty<TacticalProposal>())
        {
            if (string.IsNullOrWhiteSpace(proposal.Source))
                continue;

            if (!winners.TryGetValue(proposal.Channel, out var current) ||
                Compare(proposal, current) < 0)
            {
                winners[proposal.Channel] = proposal;
            }
        }

        return new SoldierTacticalResolution(snapshot, winners);
    }

    private static int Compare(TacticalProposal left, TacticalProposal right)
    {
        var priority = right.Priority.CompareTo(left.Priority);
        if (priority != 0)
            return priority;

        var source = string.Compare(left.Source, right.Source, StringComparison.Ordinal);
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
}

internal static class GroundAuthorityCore
{
    internal static bool CanMutate(bool multiplayerActive, bool isHost)
        => !multiplayerActive || isHost;
}

internal static class AttackerPlannerCore
{
    internal static CommanderPlan Plan(CommanderPlanInput input)
        => CommanderPlannerCore.Plan(input with { OffensiveOperation = true });
}

internal static class DefenderPlannerCore
{
    internal static CommanderPlan Plan(CommanderPlanInput input)
        => CommanderPlannerCore.Plan(input with { OffensiveOperation = false });
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
