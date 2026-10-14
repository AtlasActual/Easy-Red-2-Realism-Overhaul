using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// A deliberately small objective-order layer. It spreads autonomous infantry
/// squads across active objectives, keeps one squad on each attack's pressure axis
/// while the others alternate wide flanks, and keeps a garrison on held objectives
/// for both sides when enough squads exist. Native leader bookkeeping still
/// runs; only its objective reselection and order redirection are guarded for
/// assigned attackers. It does not stage attacks or claim command channels.
/// </summary>
internal static class ObjectiveCoordination
{
    private const float PlanningIntervalSeconds = 6f;
    private const float PressureChangeThreshold = 0.02f;
    private const float PressureMemorySeconds = 45f;
    private const int DefensiveCoverCandidateLimit = 64;
    private const float DefensiveCoverAnchorSpacingMeters = 22f;
    // Counter-attack decision gate (plan 041) caller-side thresholds. The shared
    // assessment radius itself lives on ObjectiveCounterAttackPlanCore so this
    // caller and the deterministic tests cannot drift apart on that value.
    private const float CounterAttackNearbyPressureRadiusMeters = 180f;
    private const float CounterAttackRecentLossSeconds = 30f;

    private static readonly Dictionary<int, OrderStamp> LastOrders = new();
    private static readonly Dictionary<int, AttackerTaskAssignment> AttackerTasks = new();
    private static readonly Dictionary<int, float> ObjectiveProgress = new();
    private static readonly Dictionary<int, float> ObjectivePressureUntil = new();
    private static readonly Dictionary<string, int> LastPlanSignatures =
        new(StringComparer.Ordinal);
    // Ownership bookkeeping for the counter-attack gate's "lost recently" rule.
    // Keyed by objective id; the securing faction ("" when contested/neutral)
    // and the time it last actually changed. CollectObjectives updates both
    // idempotently so a defender's and an attacker's pass over the same
    // objective in one planning cycle do not double-stamp the change time.
    private static readonly Dictionary<int, string> ObjectiveOwnerFaction = new();
    private static readonly Dictionary<int, float> ObjectiveOwnerChangedAt = new();

    private static float _nextPlanAt;
    private static bool _failedThisBattle;

    internal static void Update(BattleManager manager, float now)
    {
        if (!Settings.ObjectiveCoordinationEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            !BattleManager.IsBattleActive() ||
            _failedThisBattle ||
            now < _nextPlanAt)
        {
            return;
        }

        _nextPlanAt = now + PlanningIntervalSeconds;
        try
        {
            Plan(manager, now);
        }
        catch (Exception ex)
        {
            _failedThisBattle = true;
            Plugin.LogSource.LogWarning(
                $"Objective coordination disabled for this battle after an unexpected error: {ex.Message}");
        }
    }

    internal static void ResetBattle()
    {
        LastOrders.Clear();
        AttackerTasks.Clear();
        ObjectiveProgress.Clear();
        ObjectivePressureUntil.Clear();
        LastPlanSignatures.Clear();
        ObjectiveOwnerFaction.Clear();
        ObjectiveOwnerChangedAt.Clear();
        _nextPlanAt = 0f;
        _failedThisBattle = false;
    }

    private static void Plan(BattleManager manager, float now)
    {
        // A native-order guard is valid only for this planning snapshot. Squads
        // absent from the next plan immediately return to native task selection.
        AttackerTasks.Clear();
        var battle = BattleManager.GetCurrentBattleData();
        if (battle == null)
            return;

        var squadsByFaction = CollectSquadsByFaction(battle);
        var activeSquadIds = new HashSet<int>();

        foreach (var pair in squadsByFaction)
        {
            foreach (var squad in pair.Value)
                activeSquadIds.Add(squad.Id);

            var objectives = CollectObjectives(manager, battle, pair.Key, now);
            if (objectives.Count == 0)
                continue;

            var attacking = battle.IsInvaderFaction(pair.Key);
            PlanFaction(pair.Key, pair.Value, objectives, attacking, now, battle, squadsByFaction);
        }

        foreach (var staleId in LastOrders.Keys.Where(id => !activeSquadIds.Contains(id)).ToArray())
            LastOrders.Remove(staleId);
    }

    private static Dictionary<string, List<SquadInfo>> CollectSquadsByFaction(BattleData battle)
    {
        var result = new Dictionary<string, List<SquadInfo>>(StringComparer.Ordinal);
        var allSquads = Squad.AllSquads;
        if (allSquads == null)
            return result;

        foreach (var pair in allSquads)
        {
            var squad = pair.Value;
            if (!TryDescribeSquad(squad, battle, out var info))
                continue;

            if (!result.TryGetValue(info.Faction, out var factionSquads))
            {
                factionSquads = new List<SquadInfo>();
                result.Add(info.Faction, factionSquads);
            }

            factionSquads.Add(info);
        }

        foreach (var factionSquads in result.Values)
            factionSquads.Sort((left, right) => left.Id.CompareTo(right.Id));

        return result;
    }

    private static bool TryDescribeSquad(Squad? squad, BattleData battle, out SquadInfo info)
    {
        info = default;
        try
        {
            if (squad == null || !squad.fullySpawned || squad.IsVehicleCrew ||
                !squad.HasAliveAIMembers() ||
                GroundAiDirector.IsExternallyControlledSquad(squad))
            {
                return false;
            }

            var leader = squad.Leader;
            if (leader == null || !leader.CanFight() || !AiOwnership.IsAutonomous(leader))
                return false;

            var faction = leader.faction ?? string.Empty;
            if (string.IsNullOrWhiteSpace(faction) ||
                (!battle.IsInvaderFaction(faction) && !battle.IsDefenderFaction(faction)))
            {
                return false;
            }

            var position = leader.transform.position;
            if (!IsFinite(position))
                return false;

            // Read once at snapshot time (not inside the per-objective planning
            // loops). A quiet cover relocation does not make a squad unavailable
            // to reinforce an objective; active contact and pinning still do.
            var contact = AiState.GetContactState(leader.GetInstanceID());
            var engaged = contact.ContactResponseActive || contact.Pinned;

            info = new SquadInfo(SquadIdentity.GetSquadId(squad), squad, faction, position, leader, engaged);
            return true;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    private static List<ObjectiveInfo> CollectObjectives(
        BattleManager manager,
        BattleData battle,
        string faction,
        float now)
    {
        var result = new List<ObjectiveInfo>();
        var objectives = manager.ActiveObjectives(false).GetEnumerator();
        var iterator = objectives.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (iterator.MoveNext())
        {
            var objective = objectives.Current;
            if (objective == null)
                continue;

            // Native CanAttractFaction rejects secured objectives, including
            // capture points that either side can still contest in this phase.
            // Only extend eligibility for enabled, ordinary capture tasks: a
            // completed destruction task or a script's faction restriction must
            // never become an autonomous hold or counter-attack destination.
            var task = objective.missionTask;
            var activeCaptureTask = task != null && task.IsTaskEnabled() &&
                (Il2CppTypeTests.Is<MissionTaskConquerArea>(task) ||
                 Il2CppTypeTests.Is<MissionTaskPushFrontline>(task));
            if (!ObjectiveEligibilityCore.ShouldInclude(
                    objective.CanAttractFaction(faction),
                    activeCaptureTask,
                    Il2CppTypeTests.Is<MissionObjectiveScripted>(objective)))
                continue;

            var position = objective.GetTaskPosition();
            if (!IsFinite(position))
                continue;

            var id = manager.GetObjectiveUniqueId(objective);
            var friendlySecured = false;
            var enemySecured = false;
            var securedFaction = string.Empty;
            if (objective.IsSecured())
            {
                securedFaction = objective.GetSecuredFaction() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(securedFaction))
                {
                    var factionIsInvader = battle.IsInvaderFaction(faction);
                    var factionIsDefender = battle.IsDefenderFaction(faction);
                    var securedIsInvader = battle.IsInvaderFaction(securedFaction);
                    var securedIsDefender = battle.IsDefenderFaction(securedFaction);
                    friendlySecured =
                        (factionIsInvader && securedIsInvader) ||
                        (factionIsDefender && securedIsDefender);
                    enemySecured =
                        (factionIsInvader && securedIsDefender) ||
                        (factionIsDefender && securedIsInvader);
                }
            }

            // The securing faction is a property of the objective itself, not of
            // the faction currently planning, so both factions observe the same
            // value in the same cycle and TrackObjectiveOwnership's own equality
            // check makes the second faction's call this cycle a no-op.
            TrackObjectiveOwnership(id, securedFaction, now);

            var pressured = SampleObjectivePressure(objective, id, now);
            var radius = float.IsFinite(objective.objectiveRadius)
                ? Mathf.Max(12f, objective.objectiveRadius)
                : 30f;
            result.Add(new ObjectiveInfo(
                id, position, radius, friendlySecured, enemySecured, pressured, objective));
        }

        return result;
    }

    private static bool SampleObjectivePressure(MissionObjective objective, int id, float now)
    {
        try
        {
            var progress = objective.missionTask?.GetTaskCompletationPercentage() ?? 0f;
            if (float.IsFinite(progress))
            {
                if (ObjectiveProgress.TryGetValue(id, out var previous) &&
                    Mathf.Abs(progress - previous) >= PressureChangeThreshold)
                {
                    ObjectivePressureUntil[id] = now + PressureMemorySeconds;
                }

                ObjectiveProgress[id] = progress;
            }
        }
        catch (ObjectCollectedException)
        {
            return false;
        }

        return ObjectivePressureUntil.TryGetValue(id, out var until) && now < until;
    }

    /// <summary>
    /// Records when an objective's securing faction actually changes, so the
    /// counter-attack gate can tell a fresh repulse from a point that has sat
    /// enemy-secured for a while. Idempotent: called once per faction per
    /// CollectObjectives pass, but only the first call in a cycle observes a
    /// real change, so a second faction's identical observation is a no-op. The
    /// first-ever sighting of an objective records its owner without stamping a
    /// change time, since there is no prior baseline to compare against.
    /// </summary>
    private static void TrackObjectiveOwnership(int objectiveId, string securedFaction, float now)
    {
        if (ObjectiveOwnerFaction.TryGetValue(objectiveId, out var previous))
        {
            if (string.Equals(previous, securedFaction, StringComparison.Ordinal))
                return;

            ObjectiveOwnerFaction[objectiveId] = securedFaction;
            ObjectiveOwnerChangedAt[objectiveId] = now;
            return;
        }

        ObjectiveOwnerFaction[objectiveId] = securedFaction;
    }

    private static void PlanFaction(
        string faction,
        List<SquadInfo> squads,
        List<ObjectiveInfo> objectives,
        bool attacking,
        float now,
        BattleData battle,
        Dictionary<string, List<SquadInfo>> squadsByFaction)
    {
        if (squads.Count == 0)
            return;

        var centroid = AveragePosition(squads);
        objectives.Sort((left, right) =>
        {
            // When squads are scarce, retain a garrison on objectives still held
            // before considering a recapture, even if the lost point is closer.
            if (!attacking && left.EnemySecured != right.EnemySecured)
                return left.EnemySecured ? 1 : -1;

            var distanceComparison = HorizontalDistanceSquared(centroid, left.Position)
                .CompareTo(HorizontalDistanceSquared(centroid, right.Position));
            return distanceComparison != 0 ? distanceComparison : left.Id.CompareTo(right.Id);
        });

        var hostileSquads = CollectHostileSquads(battle, faction, squadsByFaction);

        List<ObjectiveInfo> targets;
        if (attacking)
        {
            var selectedIds = new HashSet<int>(ObjectiveAttackPlanCore.SelectTargets(
                squads.Count,
                objectives.Select(objective => new ObjectiveAttackTarget(
                    objective.Id, objective.FriendlySecured)).ToList()));
            targets = objectives.Where(objective => selectedIds.Contains(objective.Id)).ToList();
        }
        else
        {
            targets = objectives.Take(Math.Min(objectives.Count, squads.Count)).ToList();
        }
        var capacities = BuildCapacities(squads.Count, targets, attacking);
        var assignments = AssignSquads(squads, targets, capacities);
        if (!attacking)
        {
            var accepted = ApplyCounterAttackGate(
                squads, targets, objectives, hostileSquads, assignments, now);
            if (accepted.Count != targets.Count)
            {
                targets = accepted;
                capacities = BuildCapacities(squads.Count, targets, attacking);
                // Keep the squads whose strength justified each accepted attack.
                // Only redistribute rejected assignments and surplus capacity.
                assignments = AssignSquads(squads, targets, capacities, assignments);
            }
        }

        var flankCount = 0;

        void IssueAttackWave(List<SquadInfo> assigned, ObjectiveInfo target)
        {
            for (var index = 0; index < assigned.Count; index++)
            {
                var role = ObjectiveAttackPlanCore.SelectRole(index, assigned.Count, target.Id);
                IssueAttack(assigned[index], target, role);
                if (role.IsFlank)
                    flankCount++;
            }
        }

        foreach (var target in targets)
        {
            if (!assignments.TryGetValue(target.Id, out var assigned))
                continue;

            assigned.Sort((left, right) => left.Id.CompareTo(right.Id));

            if (attacking)
            {
                var task = target.NativeObjective.missionTask?.TryCast<SquadTask>();
                if (task != null)
                {
                    foreach (var squad in assigned)
                    {
                        AttackerTasks[squad.Id] = new AttackerTaskAssignment(
                            target.Id, target.NativeObjective, task);
                        squad.Squad.currentTask = task;
                    }
                }
            }

            if (ObjectiveCounterAttackPlanCore.ShouldIssueAttackOrder(
                    attacking, target.EnemySecured, target.FriendlySecured))
            {
                IssueAttackWave(assigned, target);
                continue;
            }

            var defenseAnchors = new List<Vector3>();
            var threatDirection = ComputeThreatDirection(target.Position, hostileSquads);
            for (var index = 0; index < assigned.Count; index++)
            {
                IssueDefense(
                    assigned[index], target, index, assigned.Count, defenseAnchors, threatDirection);
            }
        }

        TracePlanChange(faction, attacking, squads.Count, targets.Count, flankCount, assignments);
    }

    /// <summary>
    /// Keeps native leader bookkeeping on the objective selected for this squad.
    /// A captured task otherwise triggers native reselection, and an offset
    /// attack waypoint otherwise looks like a route to the wrong objective.
    /// Only the scoped native leader task/order helpers consult this guard.
    /// </summary>
    internal static bool TryBindAttackerTask(Squad squad)
    {
        if (!Settings.ObjectiveCoordinationEnabled.Value || _failedThisBattle ||
            Time.time >= _nextPlanAt || !MultiplayerAuthority.CanMutateGameplay() ||
            !BattleManager.IsBattleActive() || squad == null ||
            !AttackerTasks.TryGetValue(SquadIdentity.GetSquadId(squad), out var assignment))
        {
            return false;
        }

        var battle = BattleManager.GetCurrentBattleData();
        if (battle == null || !TryDescribeSquad(squad, battle, out var info) ||
            !battle.IsInvaderFaction(info.Faction) ||
            !LastOrders.TryGetValue(info.Id, out var order) ||
            order.ObjectiveId != assignment.ObjectiveId)
        {
            return false;
        }

        var expectedOrder = order.Order == CoordinatedOrder.Defend
            ? Order.defend
            : Order.attackFromSide;
        if (assignment.Objective == null)
            return false;

        var task = assignment.Objective.missionTask;
        if (squad.order != expectedOrder || task == null || !task.IsTaskEnabled() ||
            task.Pointer != assignment.Task.Pointer)
        {
            return false;
        }

        squad.currentTask = assignment.Task;
        return true;
    }

    private static List<SquadInfo> CollectHostileSquads(
        BattleData battle,
        string faction,
        Dictionary<string, List<SquadInfo>> squadsByFaction)
    {
        var factionIsInvader = battle.IsInvaderFaction(faction);
        var hostile = new List<SquadInfo>();
        foreach (var pair in squadsByFaction)
        {
            if (string.Equals(pair.Key, faction, StringComparison.Ordinal))
                continue;

            // Every key in squadsByFaction is guaranteed invader or defender
            // (TryDescribeSquad rejects anything else), so a side mismatch
            // here is exactly "hostile to the planning faction".
            if (battle.IsInvaderFaction(pair.Key) == factionIsInvader)
                continue;

            hostile.AddRange(pair.Value);
        }

        return hostile;
    }

    /// <summary>
    /// Gates each EnemySecured candidate objective
    /// through <see cref="ObjectiveCounterAttackPlanCore.ShouldCounterAttack"/>
    /// and drops the ones it rejects. Friendly-secured and neutral candidates
    /// are never gated. Rejected objectives simply stop being targets, so
    /// AssignSquads redistributes those squads across the objectives still
    /// held - the "consolidate and try again next cycle" behavior. If gating
    /// would leave no targets at all, every candidate is kept instead so
    /// defenders never freeze when everything active is lost.
    /// </summary>
    private static List<ObjectiveInfo> ApplyCounterAttackGate(
        List<SquadInfo> squads,
        List<ObjectiveInfo> targets,
        List<ObjectiveInfo> allObjectives,
        List<SquadInfo> hostileSquads,
        Dictionary<int, List<SquadInfo>> assignments,
        float now)
    {
        var candidates = new List<CounterAttackCandidate>(targets.Count);
        foreach (var target in targets)
        {
            var passesGate = !target.EnemySecured ||
                AssessCounterAttack(
                    squads, target, allObjectives, hostileSquads, assignments[target.Id], now);
            candidates.Add(new CounterAttackCandidate(target.Id, target.EnemySecured, passesGate));
        }

        var keptIds = ObjectiveCounterAttackPlanCore.SelectCounterAttackTargets(candidates);
        if (keptIds.Count == targets.Count)
            return targets;

        var keptSet = new HashSet<int>(keptIds);
        return targets.Where(target => keptSet.Contains(target.Id)).ToList();
    }

    /// <summary>
    /// Counts local support plus the squads assigned to approach this objective.
    /// Existing attackers remain committed when contact starts; new reinforcements
    /// must be free to maneuver. Other objectives' garrisons are not reinforcements.
    /// </summary>
    private static bool AssessCounterAttack(
        List<SquadInfo> squads,
        ObjectiveInfo target,
        List<ObjectiveInfo> allObjectives,
        List<SquadInfo> hostileSquads,
        List<SquadInfo> assigned,
        float now)
    {
        var radius = ObjectiveCounterAttackPlanCore.CounterAttackAssessmentRadiusMeters;
        var radiusSqr = radius * radius;

        var assignedIds = new HashSet<int>(assigned.Select(squad => squad.Id));
        var support = new List<CounterAttackSquadSupport>(squads.Count);
        foreach (var squad in squads)
        {
            var committed = LastOrders.TryGetValue(squad.Id, out var previous) &&
                            previous.ObjectiveId == target.Id &&
                            previous.Order != CoordinatedOrder.Defend;
            support.Add(new CounterAttackSquadSupport(
                HorizontalDistanceSquared(squad.Position, target.Position) <= radiusSqr,
                assignedIds.Contains(squad.Id), squad.Engaged, committed));
        }
        var strength = ObjectiveCounterAttackPlanCore.AssessStrength(support);

        var nearbyHostiles = 0;
        foreach (var hostile in hostileSquads)
        {
            if (HorizontalDistanceSquared(hostile.Position, target.Position) <= radiusSqr)
                nearbyHostiles++;
        }

        var pressureRadiusSqr =
            CounterAttackNearbyPressureRadiusMeters * CounterAttackNearbyPressureRadiusMeters;
        var nearbyPressure = false;
        foreach (var candidate in allObjectives)
        {
            if (candidate.Id == target.Id || candidate.EnemySecured || !candidate.Pressured)
                continue;

            if (HorizontalDistanceSquared(candidate.Position, target.Position) <= pressureRadiusSqr)
            {
                nearbyPressure = true;
                break;
            }
        }

        var lostRecently = ObjectiveOwnerChangedAt.TryGetValue(target.Id, out var changedAt) &&
                            now - changedAt <= CounterAttackRecentLossSeconds;
        var lostRecentlyWithHostilesNearby =
            ObjectiveCounterAttackPlanCore.ObjectiveLostRecentlyWithHostilesNearby(
                lostRecently, nearbyHostiles);

        return ObjectiveCounterAttackPlanCore.ShouldCounterAttack(
            strength.SupportingSquads, strength.ManeuverSquads, nearbyHostiles, nearbyPressure,
            lostRecentlyWithHostilesNearby);
    }

    private const float ThreatDirectionSearchRadiusMeters = 150f;

    /// <summary>
    /// Averages the bearing of nearby hostile squads from the objective
    /// center. Returns Vector3.zero when none qualify; callers fall back to
    /// the existing StableAngleOffset in that case.
    /// </summary>
    private static Vector3 ComputeThreatDirection(
        Vector3 objectivePosition,
        List<SquadInfo> hostileSquads)
    {
        var sum = Vector3.zero;
        var searchRadiusSqr = ThreatDirectionSearchRadiusMeters * ThreatDirectionSearchRadiusMeters;
        foreach (var enemy in hostileSquads)
        {
            var offset = Flatten(enemy.Position - objectivePosition);
            var distanceSqr = offset.sqrMagnitude;
            if (distanceSqr < 1f || distanceSqr > searchRadiusSqr)
                continue;

            sum += offset.normalized;
        }

        return sum;
    }

    private static Dictionary<int, int> BuildCapacities(
        int squadCount,
        List<ObjectiveInfo> targets,
        bool attacking)
    {
        if (attacking)
        {
            return ObjectiveAttackPlanCore.BuildCapacities(
                squadCount,
                targets.Select(objective => new ObjectiveAttackTarget(
                    objective.Id, objective.FriendlySecured)).ToList());
        }

        var capacityInputs = targets
            .Select(objective => new ObjectiveCapacityInput(
                objective.Id, objective.Pressured, objective.EnemySecured))
            .ToList();
        return ObjectiveCounterAttackPlanCore.BuildCapacities(squadCount, capacityInputs, attacking);
    }

    private static Dictionary<int, List<SquadInfo>> AssignSquads(
        List<SquadInfo> squads,
        List<ObjectiveInfo> targets,
        Dictionary<int, int> capacities,
        Dictionary<int, List<SquadInfo>>? preferredAssignments = null)
    {
        var assignments = targets.ToDictionary(
            objective => objective.Id,
            _ => new List<SquadInfo>());
        var assignedSquads = new HashSet<int>();

        if (preferredAssignments != null)
        {
            foreach (var target in targets)
            {
                foreach (var squad in preferredAssignments[target.Id])
                {
                    if (assignments[target.Id].Count >= capacities[target.Id])
                        break;
                    assignments[target.Id].Add(squad);
                    assignedSquads.Add(squad.Id);
                }
            }
        }

        // Keep a still-valid assignment first. Position changes should not make
        // squads trade objectives every planning tick. Keep engaged garrisons
        // first so free squads can fill newly opened counter-attack slots.
        foreach (var squad in squads.OrderByDescending(squad => squad.Engaged))
        {
            if (assignedSquads.Contains(squad.Id) ||
                !LastOrders.TryGetValue(squad.Id, out var previous) ||
                !assignments.TryGetValue(previous.ObjectiveId, out var destination) ||
                destination.Count >= capacities[previous.ObjectiveId])
            {
                continue;
            }

            destination.Add(squad);
            assignedSquads.Add(squad.Id);
        }

        foreach (var squad in squads)
        {
            if (assignedSquads.Contains(squad.Id))
                continue;

            var target = targets
                .Where(candidate => assignments[candidate.Id].Count < capacities[candidate.Id])
                .OrderBy(candidate => HorizontalDistanceSquared(squad.Position, candidate.Position))
                .ThenBy(candidate => candidate.Id)
                .First();
            assignments[target.Id].Add(squad);
        }

        return assignments;
    }

    private static void IssueAttack(
        SquadInfo squad,
        ObjectiveInfo objective,
        ObjectiveAttackRole role)
    {
        var direction = Flatten(objective.Position - squad.Position);
        if (direction.sqrMagnitude < 1f)
            direction = Vector3.forward;
        else
            direction.Normalize();

        if (role.IsFlank)
            direction = RotateHorizontal(direction, role.Side * role.AngleDegrees);

        var proposed = new OrderStamp(
            objective.Id,
            role.IsFlank
                ? role.Side < 0 ? CoordinatedOrder.FlankLeft : CoordinatedOrder.FlankRight
                : CoordinatedOrder.DirectAttack,
            objective.Position,
            objective.Radius,
            Vector3.zero);
        if (!NeedsOrder(squad.Squad, proposed))
            return;

        try
        {
            squad.Squad.AttackFromSide(direction, objective.Position, objective.Radius);
            LastOrders[squad.Id] = proposed with
            {
                NativeDestination = squad.Squad.moveOrderPosition
            };
            AiState.Trace(
                $"Objective order: squad {squad.Id} " +
                $"{(role.IsFlank ? $"flank {(role.Side < 0 ? "left" : "right")} {role.AngleDegrees:0}°" : "attack")} " +
                $"-> {objective.Id}");
        }
        catch (ObjectCollectedException)
        {
            LastOrders.Remove(squad.Id);
        }
    }

    private static void IssueDefense(
        SquadInfo squad,
        ObjectiveInfo objective,
        int sectorIndex,
        int sectorCount,
        List<Vector3> existingAnchors,
        Vector3 threatDirection)
    {
        var coreGarrison = sectorIndex == 0;

        // towardExpectedThreat is now the real bearing to nearby hostile
        // squads (see ComputeThreatDirection), not derived from the sector's
        // own placement. It falls back to the existing stable per-objective
        // angle only when no hostile squad qualifies.
        var towardExpectedThreat = threatDirection;
        if (towardExpectedThreat.sqrMagnitude < 0.0001f)
        {
            var fallbackAngle = StableAngleOffset(objective.Id);
            towardExpectedThreat = new Vector3(Mathf.Cos(fallbackAngle), 0f, Mathf.Sin(fallbackAngle));
        }
        else
        {
            towardExpectedThreat.Normalize();
        }

        var preferredDestination = objective.Position;
        if (!coreGarrison && sectorCount > 1)
        {
            // Placed out near the objective edge, not in a tight inner ring.
            // Build clamps this per sector afterwards: forward sectors keep
            // nearly all of it, flank and rear sectors are pulled back to half
            // the objective radius. A narrower ring here would make the
            // containment rule non-binding and leave the defense clustered.
            var ringRadius = Mathf.Min(70f, objective.Radius * 0.9f);
            var surplusCount = sectorCount - 1;
            var surplusIndex = sectorIndex - 1;
            var threatAngle = Mathf.Atan2(towardExpectedThreat.z, towardExpectedThreat.x);
            const float arcSpanRadians = Mathf.PI; // 180-degree arc facing the threat.
            var angle = surplusCount <= 1
                ? threatAngle
                : threatAngle - arcSpanRadians / 2f +
                  arcSpanRadians * surplusIndex / (surplusCount - 1);
            preferredDestination +=
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
        }

        var threatDirectionPoint = new MapPoint(towardExpectedThreat.x, towardExpectedThreat.z);
        var area = ObjectiveDefenseAreaCore.Build(
            new MapPoint(objective.Position.x, objective.Position.z),
            objective.Radius,
            new MapPoint(preferredDestination.x, preferredDestination.z),
            coreGarrison,
            threatDirectionPoint);
        var destination = new Vector3(area.Center.X, objective.Position.y, area.Center.Z);
        var holdRadius = area.HoldRadius;

        // The core garrison deliberately remains centered on the capture point.
        // Native HoldArea and the soldier-level defensive inventory still place
        // its members in authored cover inside that area; only the squad's center
        // is protected from being dragged behind a dense building or trench line.
        var coverAnchor = destination;
        var coverAnchored = !coreGarrison && TryFindDefensiveCoverAnchor(
                squad.Faction,
                objective,
                destination,
                towardExpectedThreat,
                holdRadius,
                existingAnchors,
                out coverAnchor);
        if (coverAnchored)
        {
            area = ObjectiveDefenseAreaCore.Build(
                new MapPoint(objective.Position.x, objective.Position.z),
                objective.Radius,
                new MapPoint(coverAnchor.x, coverAnchor.z),
                coreGarrison: false,
                threatDirectionPoint);
            destination = new Vector3(area.Center.X, objective.Position.y, area.Center.Z);
            holdRadius = area.HoldRadius;
        }

        existingAnchors.Add(destination);
        var proposed = new OrderStamp(
            objective.Id,
            CoordinatedOrder.Defend,
            destination,
            holdRadius,
            Vector3.zero);
        if (!NeedsOrder(squad.Squad, proposed))
            return;

        try
        {
            // HoldArea synchronously calls the game's SendUnitsToCovers routine.
            // Authorize that one squad-level handoff so the individual CoverPosition
            // writes survive our exclusive-cover guard. Once HoldArea returns, the
            // ordinary guard owns the positions again and prevents cover churn.
            ContactResponse.ExecuteOwnedSquadCoverWrites(
                squad.Squad,
                () => squad.Squad.HoldArea(destination, holdRadius, false));
            LastOrders[squad.Id] = proposed with
            {
                NativeDestination = squad.Squad.moveOrderPosition
            };
            AiState.Trace(
                $"Objective order: squad {squad.Id} defend -> {objective.Id} " +
                (coverAnchored ? "cover-cluster" : "sector-fallback"));
        }
        catch (ObjectCollectedException)
        {
            LastOrders.Remove(squad.Id);
        }
    }

    private static bool TryFindDefensiveCoverAnchor(
        string faction,
        ObjectiveInfo objective,
        Vector3 idealPosition,
        Vector3 towardExpectedThreat,
        float holdRadius,
        List<Vector3> existingAnchors,
        out Vector3 anchor)
    {
        anchor = idealPosition;
        try
        {
            var searchRadius = Mathf.Clamp(objective.Radius * 0.6f, 24f, 50f);
            var covers = CoverManager.GetCovers(
                idealPosition,
                searchRadius,
                faction,
                towardExpectedThreat,
                true);
            if (covers == null)
                return false;

            var maximumAnchorOffset = ObjectiveDefenseAreaCore.MaximumAnchorOffset(objective.Radius);
            var maximumAnchorOffsetSqr = maximumAnchorOffset * maximumAnchorOffset;
            var candidates = new List<DefensiveCoverCandidate>();
            var examined = 0;
            foreach (var rawCover in covers)
            {
                if (++examined > DefensiveCoverCandidateLimit)
                    break;

                try
                {
                    var cover = rawCover.TryCast<AiDestination>();
                    if (cover == null || cover.WasCollected || cover.Pointer == IntPtr.Zero ||
                        cover.IsVehicle() || cover.IsCoverDestroyed() || cover.IsUnsafeCover() ||
                        !cover.IsCoverAvailable(towardExpectedThreat, faction) ||
                        !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(
                            cover, out var position) ||
                        !IsFinite(position) ||
                        HorizontalDistanceSquared(position, objective.Position) >
                        maximumAnchorOffsetSqr)
                    {
                        continue;
                    }

                    candidates.Add(new DefensiveCoverCandidate(
                        cover.Pointer,
                        position,
                        cover.GetCoverPose() != SoldierPose.Idle));
                }
                catch (NullReferenceException)
                {
                }
                catch (Il2CppException)
                {
                }
                catch (ObjectCollectedException)
                {
                }
            }

            if (candidates.Count == 0)
                return false;

            var clusterRadius = Mathf.Clamp(holdRadius, 16f, 28f);
            var clusterRadiusSqr = clusterRadius * clusterRadius;
            DefensiveCoverCandidate? best = null;
            var bestScore = float.MaxValue;
            foreach (var candidate in candidates)
            {
                var nearbySlots = 0;
                foreach (var other in candidates)
                {
                    if (HorizontalDistanceSquared(candidate.Position, other.Position) <=
                        clusterRadiusSqr)
                    {
                        nearbySlots++;
                    }
                }

                var score =
                    Mathf.Sqrt(HorizontalDistanceSquared(candidate.Position, idealPosition)) *
                    8f;
                score -= Mathf.Min(nearbySlots, 8) * 55f;
                if (!candidate.HasAuthoredPose)
                    score += 100f;

                foreach (var existing in existingAnchors)
                {
                    var separation = Mathf.Sqrt(
                        HorizontalDistanceSquared(candidate.Position, existing));
                    if (separation < DefensiveCoverAnchorSpacingMeters)
                    {
                        score +=
                            (DefensiveCoverAnchorSpacingMeters - separation) * 30f;
                    }
                }

                if (score < bestScore - 0.01f ||
                    (Mathf.Abs(score - bestScore) <= 0.01f &&
                     (!best.HasValue || candidate.Id.ToInt64() < best.Value.Id.ToInt64())))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (!best.HasValue)
                return false;

            anchor = best.Value.Position;
            return true;
        }
        catch (NullReferenceException)
        {
            return false;
        }
        catch (Il2CppException)
        {
            return false;
        }
        catch (ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool NeedsOrder(Squad squad, OrderStamp proposed)
    {
        var squadId = SquadIdentity.GetSquadId(squad);
        if (!LastOrders.TryGetValue(squadId, out var existing) ||
            existing.ObjectiveId != proposed.ObjectiveId ||
            existing.Order != proposed.Order ||
            HorizontalDistanceSquared(existing.Destination, proposed.Destination) > 1f ||
            Mathf.Abs(existing.Radius - proposed.Radius) > 0.5f)
        {
            return true;
        }

        var expectedOrder = proposed.Order == CoordinatedOrder.Defend
            ? Order.defend
            : Order.attackFromSide;
        return SquadOrderContinuityCore.ShouldReissue(
            planStampMatches: true,
            nativeOrderMatches: squad.order == expectedOrder);
    }

    private static void TracePlanChange(
        string faction,
        bool attacking,
        int squadCount,
        int objectiveCount,
        int flankCount,
        Dictionary<int, List<SquadInfo>> assignments)
    {
        unchecked
        {
            var signature = squadCount * 397 ^ objectiveCount;
            foreach (var pair in assignments.OrderBy(pair => pair.Key))
                signature = signature * 31 + pair.Key * 17 + pair.Value.Count;

            if (LastPlanSignatures.TryGetValue(faction, out var previous) && previous == signature)
                return;

            LastPlanSignatures[faction] = signature;
            AiState.Trace(
                $"Objective plan: {faction} {(attacking ? "attack" : "defense")} " +
                $"squads={squadCount} objectives={objectiveCount} flanks={flankCount}");
        }
    }

    private static Vector3 AveragePosition(List<SquadInfo> squads)
    {
        var total = Vector3.zero;
        foreach (var squad in squads)
            total += squad.Position;
        return total / squads.Count;
    }

    private static Vector3 RotateHorizontal(Vector3 direction, float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var cosine = Mathf.Cos(radians);
        var sine = Mathf.Sin(radians);
        return new Vector3(
            direction.x * cosine - direction.z * sine,
            0f,
            direction.x * sine + direction.z * cosine).normalized;
    }

    private static float StableAngleOffset(int objectiveId)
    {
        unchecked
        {
            var hash = (uint)objectiveId * 2654435761u;
            return (hash % 360u) * Mathf.Deg2Rad;
        }
    }

    private static Vector3 Flatten(Vector3 value) => new(value.x, 0f, value.z);

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        var delta = Flatten(first - second);
        return delta.sqrMagnitude;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private readonly record struct SquadInfo(
        int Id,
        Squad Squad,
        string Faction,
        Vector3 Position,
        Soldier Leader,
        bool Engaged);

    private readonly record struct ObjectiveInfo(
        int Id,
        Vector3 Position,
        float Radius,
        bool FriendlySecured,
        bool EnemySecured,
        bool Pressured,
        MissionObjective NativeObjective);

    private readonly record struct AttackerTaskAssignment(
        int ObjectiveId,
        MissionObjective Objective,
        SquadTask Task);

    private readonly record struct DefensiveCoverCandidate(
        IntPtr Id,
        Vector3 Position,
        bool HasAuthoredPose);

    private readonly record struct OrderStamp(
        int ObjectiveId,
        CoordinatedOrder Order,
        Vector3 Destination,
        float Radius,
        Vector3 NativeDestination);

    private enum CoordinatedOrder
    {
        DirectAttack,
        FlankLeft,
        FlankRight,
        Defend
    }
}
