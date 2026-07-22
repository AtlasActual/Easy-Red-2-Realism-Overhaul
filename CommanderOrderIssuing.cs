using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class CommanderMvp
{
    private static void IssueOrders(
        SideState side,
        IReadOnlyList<SquadInfo> squads,
        int objectiveId,
        Vector3 objective,
        float objectiveRadius,
        bool offensive,
        AxisRuntime? mainAxis,
        AxisRuntime? flankAxis,
        CommanderAntiTankTask? antiTankTask,
        bool attackAuthorized,
        bool smokeBlocked,
        IReadOnlyList<CommanderReportSnapshot> reports)
    {
        if (!offensive)
        {
            // The strongest fresh contact gives the likely attack axis. Cluster
            // selection prefers cover that can see it, so defenders can return fire.
            var defensiveThreat = reports.Count > 0
                ? (Vector3?)SelectSupportByFireTarget(objective, reports)
                : null;
            IssueDefensiveOrders(
                side, squads, objectiveId, objective, objectiveRadius, defensiveThreat);
            return;
        }

        var fallbackDirection = mainAxis?.Direction ?? Flatten(AveragePosition(squads) - objective).normalized;
        if (fallbackDirection.sqrMagnitude < 0.01f)
            fallbackDirection = Vector3.back;

        var supportTarget = SelectSupportByFireTarget(objective, reports);
        var hasSupportPosition = TryFindSupportByFirePosition(
            objective, objectiveRadius, fallbackDirection, supportTarget, squads,
            out var supportPosition);
        var reservePosition = GroundPoint(
            objective + fallbackDirection * Mathf.Max(objectiveRadius + 90f, 125f),
            objective.y,
            out _,
            out _);

        var stagingLaneIndexes = new Dictionary<CommanderRole, int>();
        foreach (var squad in squads.OrderBy(entry => entry.Id))
        {
            if (antiTankTask is { } task && task.SquadId == squad.Id)
            {
                // The AT position search (raycasts per lateral offset) and the
                // native order write are the expensive execution part; only the
                // task assignment itself is decided at tick time.
                side.PendingOrderExecutions.Enqueue(
                    () => IssueAntiTankOrder(squad, task, squads));
                continue;
            }

            if (!side.Roles.TryGetValue(squad.Id, out var role))
            {
                var strengthBroken = squad.Snapshot.EffectiveStrength /
                                     Mathf.Max(1f, squad.Snapshot.PeakStrength) <
                                     CommanderPlannerCore.MinimumSquadStrengthFraction;
                var recoveryPosition = strengthBroken ? reservePosition : squad.Position;
                side.PendingOrderExecutions.Enqueue(() =>
                    IssueHold(squad, objectiveId, CommanderRole.Reserve, CommanderAction.Hold,
                        recoveryPosition, 24f, objective));
                continue;
            }

            var axis = role == CommanderRole.Flank ? flankAxis : mainAxis;
            switch (role)
            {
                case CommanderRole.SupportByFire:
                    side.PendingOrderExecutions.Enqueue(() =>
                    {
                        if (hasSupportPosition && IssueHold(
                                squad,
                                objectiveId,
                                role,
                                CommanderAction.Hold,
                                supportPosition,
                                22f,
                                supportTarget,
                                requireLineOfSight: true,
                                requireAuthoredCover: true))
                        {
                            return;
                        }

                        // A support label without protected cover and a real firing
                        // lane only strands a squad behind an obstruction. Keep it
                        // alive as a reserve until the next operation can allocate a
                        // useful lane.
                        side.Roles[squad.Id] = CommanderRole.Reserve;
                        IssueHold(squad, objectiveId, CommanderRole.Reserve, CommanderAction.Hold,
                            squad.Position, 24f, supportTarget);
                    });
                    break;
                case CommanderRole.Reserve:
                    side.PendingOrderExecutions.Enqueue(() =>
                        IssueHold(squad, objectiveId, role, CommanderAction.Hold,
                            reservePosition, 25f, objective));
                    break;
                case CommanderRole.Assault:
                case CommanderRole.Flank:
                {
                    if (axis == null)
                    {
                        side.PendingOrderExecutions.Enqueue(() =>
                            IssueHold(squad, objectiveId, role, CommanderAction.Hold,
                                squad.Position, 20f, objective));
                        break;
                    }

                    if (attackAuthorized)
                    {
                        var attackDestination = AttackEntryPosition(
                            side, squad.Id, objective, objectiveRadius, squad.Position.y);
                        var attackDirection = Flatten(attackDestination - axis.StagingPosition);
                        side.PendingOrderExecutions.Enqueue(() =>
                            IssueAttack(squad, objectiveId, role, attackDirection,
                                attackDestination, objectiveRadius));
                    }
                    else
                    {
                        var laneIndex = stagingLaneIndexes.GetValueOrDefault(role);
                        stagingLaneIndexes[role] = laneIndex + 1;
                        var stagingBase = axis.StagingPosition + Perpendicular(axis.Direction) *
                                      CenteredLaneOffset(laneIndex, 20f);
                        // Snap the staging lane onto nearby protective cover so a
                        // waiting squad occupies terrain rather than an open field.
                        // Staging troops face the objective, where the enemy is.
                        // The cluster search (raycasts) and the native order write
                        // are the expensive execution part, deferred to the queue;
                        // the lane offset above is the cheap directive computed now.
                        side.PendingOrderExecutions.Enqueue(() =>
                        {
                            var staging = stagingBase;
                            var stagingThreat = Flatten(objective - staging);
                            // Attacker staging cover selection is intentionally left
                            // unchanged: no line-of-sight threat sample is passed here.
                            if (TryFindCommanderCoverCluster(
                                    staging, stagingThreat, squad.Faction, squad.Position.y, null,
                                    out var stagingCluster, out _))
                            {
                                staging = stagingCluster;
                            }
                            IssueHold(squad, objectiveId, role,
                                smokeBlocked ? CommanderAction.Prepare : CommanderAction.Hold,
                                staging, 18f, objective);
                        });
                    }

                    break;
                }
            }
        }
    }

    private static void IssueDefensiveOrders(
        SideState side,
        IReadOnlyList<SquadInfo> squads,
        int objectiveId,
        Vector3 objective,
        float objectiveRadius,
        Vector3? threatPosition)
    {
        // Each squad gets a compact fighting area inside the wider objective
        // position. Giving every squad the full objective radius made their cover
        // inventories overlap almost completely, intermingled units, and dissolved
        // squad cohesion. A squad already occupying useful cover anchors on that
        // fortification instead of being pulled back to a geometric sector point.
        var holdRadius = SquadCohesionCore.StationaryAreaRadius(
            objectiveRadius, squads.Count);
        var objectiveInventoryRadius = Mathf.Max(
            55f, objectiveRadius + DefensiveHoldMarginMeters);
        foreach (var squad in squads.OrderBy(entry => entry.Id))
        {
            var role = side.Roles.TryGetValue(squad.Id, out var assignedRole)
                ? assignedRole
                : CommanderRole.Reserve;
            // The sector ground snap, the occupied-cover-anchor scan, and the cover-
            // cluster search (all raycast-bearing) plus the native order write are
            // the expensive execution part, deferred to the queue at a bounded
            // per-frame budget instead of running for every defending squad
            // synchronously on the tick frame.
            side.PendingOrderExecutions.Enqueue(() =>
            {
                var sector = DefensiveSectorPosition(
                    side, squad.Id, objective, objectiveRadius, squad.Position.y);
                var squadHoldRadius = holdRadius;
                if (TryFindOccupiedDefensiveCoverAnchor(
                        squad, objective, objectiveInventoryRadius, out var occupiedAnchor))
                {
                    // A squad already dug into useful cover keeps its own anchor and
                    // is never pulled back to a geometric sector or a different
                    // cluster.
                    sector = GroundPoint(
                        occupiedAnchor, squad.Position.y, out _, out _);
                }
                else
                {
                    // Snap the bare golden-angle sector onto the densest protective
                    // cover cluster nearby so the squad holds a building or trench
                    // line instead of a field. Defenders face outward from the
                    // objective centre.
                    var outward = Flatten(sector - objective);
                    if (outward.sqrMagnitude < 0.01f)
                        outward = Flatten(squad.Position - objective);
                    if (outward.sqrMagnitude < 0.01f)
                        outward = Vector3.forward;
                    if (TryFindCommanderCoverCluster(
                            sector, outward, squad.Faction, squad.Position.y, threatPosition,
                            out var clusterAnchor, out var clusterCount))
                    {
                        sector = clusterAnchor;
                        squadHoldRadius = SquadCohesionCore.ClusterCohesionRadius(
                            holdRadius, clusterCount);
                    }
                }

                IssueDefensiveAreaHold(squad, objectiveId, role, sector, squadHoldRadius);
                // This trace runs once per defending squad every planning tick; skip
                // the interpolated-string allocation entirely when nobody is
                // listening.
                if (AiDebugTelemetry.CaptureEnabled || Settings.VerboseLogging.Value)
                {
                    AiState.Trace(
                        $"Commander cohesion: squad {squad.Id} center=" +
                        $"({sector.x:0.0},{sector.z:0.0}) radius={squadHoldRadius:0.0}m");
                }
            });
        }
    }

    private static void IssueDefensiveAreaHold(
        SquadInfo squad,
        int objectiveId,
        CommanderRole role,
        Vector3 objective,
        float radius)
    {
        var center = GroundPoint(objective, squad.Position.y, out _, out _);
        var stamp = new OrderStamp(
            objectiveId, role, CommanderAction.Hold, center, Vector3.zero, radius);
        if (TryKeepAcceptedOrder(squad.Id, stamp, ignoreRole: true, out var accepted))
        {
            PublishSquadIntent(squad.Squad, accepted);
            GroundAiDirector.RegisterCommanderDefensiveArea(
                squad.Squad, accepted.Destination, accepted.Radius);
            return;
        }

        PublishSquadIntent(squad.Squad, stamp);
        GroundAiDirector.RegisterCommanderDefensiveArea(squad.Squad, center, radius);

        if (GroundAiDirector.ExecuteCommanderSquadOrder(
                squad.Squad, () => squad.Squad.HoldArea(center, radius, false)))
        {
            LastOrders[squad.Id] = stamp;
        }
    }

    private static void IssueAntiTankOrder(
        SquadInfo squad,
        CommanderAntiTankTask task,
        IReadOnlyList<SquadInfo> squads)
    {
        var reportedTarget = new Vector3(
            task.TargetPosition.X, squad.Position.y, task.TargetPosition.Z);
        var target = GroundPoint(reportedTarget, squad.Position.y, out _, out _);
        var position = FindAntiTankOrderPosition(squad, target, squads, out var canAdvance);

        if (task.Action == CommanderAntiTankAction.Hunt && canAdvance)
        {
            var direction = Flatten(position - squad.Position);
            if (direction.sqrMagnitude >= 1f)
            {
                IssueAttack(squad, task.TargetId, CommanderRole.AntiTank,
                    direction, position, AntiTankOrderRadiusMeters);
                return;
            }
        }

        IssueHold(squad, task.TargetId, CommanderRole.AntiTank, CommanderAction.Hold,
            position, AntiTankOrderRadiusMeters, target,
            requireLineOfSight: true,
            minimumThreatDistance: AntiTankDesiredStandoffMeters);
    }

    private static Vector3 FindAntiTankOrderPosition(
        SquadInfo squad,
        Vector3 target,
        IReadOnlyList<SquadInfo> squads,
        out bool canAdvance)
    {
        canAdvance = false;
        var towardTarget = Flatten(target - squad.Position);
        var distance = towardTarget.magnitude;
        if (!float.IsFinite(distance) || distance < 1f ||
            distance <= AntiTankDesiredStandoffMeters)
        {
            return squad.Position;
        }

        towardTarget /= distance;
        var lateral = Perpendicular(towardTarget);
        var forwardStep = Mathf.Min(
            AntiTankMaximumOrderStepMeters,
            Mathf.Max(0f, distance - AntiTankDesiredStandoffMeters));
        var lateralOffsets = new[]
        {
            AntiTankLateralOffsetMeters,
            -AntiTankLateralOffsetMeters,
            0f
        };

        Vector3? selected = null;
        var selectedScore = float.MaxValue;
        foreach (var lateralOffset in lateralOffsets)
        {
            var requested = squad.Position + towardTarget * forwardStep +
                            lateral * lateralOffset;
            var grounded = GroundPoint(
                requested, squad.Position.y, out var normal, out var groundedSuccessfully);
            if (!groundedSuccessfully ||
                Vector3.Dot(normal.normalized, Vector3.up) < MinimumAntiTankGroundNormal ||
                !HasLineOfSight(grounded, target))
            {
                continue;
            }

            var congestion = squads.Count(other =>
                other.Id != squad.Id && HorizontalDistance(other.Position, grounded) < 25f);
            var score = HorizontalDistance(squad.Position, grounded) + congestion * 40f +
                        (Mathf.Abs(lateralOffset) < 1f ? 8f : 0f);
            if (score >= selectedScore)
                continue;

            selected = grounded;
            selectedScore = score;
        }

        if (selected == null)
            return squad.Position;

        canAdvance = HorizontalDistance(selected.Value, squad.Position) >= 4f;
        return selected.Value;
    }

    private static void IssueArmorOrders(
        SideState side,
        IReadOnlyList<TankInfo> tanks,
        int objectiveId,
        Vector3 objective,
        float objectiveRadius,
        bool offensive,
        AxisRuntime? mainAxis,
        AxisRuntime? flankAxis,
        bool attackAuthorized)
    {
        var fallbackDirection = mainAxis?.Direction ??
                                Flatten(AverageForcePosition(Array.Empty<SquadInfo>(), tanks) - objective).normalized;
        if (fallbackDirection.sqrMagnitude < 0.01f)
            fallbackDirection = Vector3.back;

        var reserveBase = GroundPoint(
            objective + fallbackDirection * Mathf.Max(objectiveRadius + 130f, 170f),
            objective.y, out _, out _);
        var laneIndexes = new Dictionary<ArmorRole, int>();
        foreach (var tank in tanks.OrderBy(tank => tank.Id))
        {
            if (!side.ArmorRoles.TryGetValue(tank.Id, out var role))
                role = ArmorRole.Reserve;

            var laneIndex = laneIndexes.GetValueOrDefault(role);
            laneIndexes[role] = laneIndex + 1;
            var axis = role == ArmorRole.FlankSupport ? flankAxis : mainAxis;
            if (role == ArmorRole.Reserve || axis == null ||
                axis.Candidate.TerrainScore < MinimumTankTerrainScore)
            {
                var reserve = reserveBase + Perpendicular(fallbackDirection) *
                    CenteredLaneOffset(laneIndex, TankLaneSpacingMeters);
                side.PendingOrderExecutions.Enqueue(() =>
                    IssueArmorHold(tank, objectiveId, ArmorRole.Reserve, reserve, 24f));
                continue;
            }

            var lateral = Perpendicular(axis.Direction) *
                          CenteredLaneOffset(laneIndex, TankLaneSpacingMeters);
            var laneOrigin = axis.StagingPosition + lateral;
            var groundedLane = GroundPoint(laneOrigin, tank.Position.y,
                out var laneNormal, out var laneGrounded);
            if (!laneGrounded || Vector3.Dot(laneNormal.normalized, Vector3.up) < 0.72f)
            {
                var reserve = reserveBase + Perpendicular(fallbackDirection) *
                    CenteredLaneOffset(laneIndex, TankLaneSpacingMeters);
                side.PendingOrderExecutions.Enqueue(() =>
                    IssueArmorHold(tank, objectiveId, ArmorRole.Reserve, reserve, 24f));
                continue;
            }

            // The lane/objective geometry above is the cheap directive computed at
            // tick time; only the native destination write (SetSquadDestination via
            // HoldArea/AttackFromSide) is deferred to the execution queue.
            if (!offensive)
            {
                var defensive = objective + axis.Direction * Mathf.Max(30f, objectiveRadius * 0.8f) + lateral;
                side.PendingOrderExecutions.Enqueue(() =>
                    IssueArmorHold(tank, objectiveId, role, defensive, 22f));
            }
            else if (attackAuthorized)
            {
                // Every committed tank used to attack the identical objective
                // center, converging two tanks on one axis onto the same point.
                // Spread each tank's destination across its staging lane offset,
                // bounded so it never wanders outside the objective area.
                var maximumOffset = 0.6f * objectiveRadius;
                var attackOffset = lateral;
                if (maximumOffset <= 0f)
                    attackOffset = Vector3.zero;
                else if (attackOffset.magnitude > maximumOffset)
                    attackOffset = attackOffset.normalized * maximumOffset;
                var attackObjective = GroundPoint(objective + attackOffset, objective.y, out _, out _);
                var attackDirection = Flatten(attackObjective - groundedLane);
                side.PendingOrderExecutions.Enqueue(() =>
                    IssueArmorAttack(tank, objectiveId, role,
                        attackDirection, attackObjective, objectiveRadius));
            }
            else
            {
                var staging = groundedLane + axis.Direction * 25f;
                side.PendingOrderExecutions.Enqueue(() =>
                    IssueArmorHold(tank, objectiveId, role, staging, 20f));
            }
        }
    }

    private static void IssueArmorHold(
        TankInfo tank,
        int objectiveId,
        ArmorRole role,
        Vector3 position,
        float radius)
    {
        var proposed = new OrderStamp(objectiveId, ToCommanderRole(role), CommanderAction.Hold,
            position, Vector3.zero, radius);
        if (TryKeepAcceptedOrder(tank.SquadId, proposed, ignoreRole: false, out var accepted))
        {
            PublishSquadIntent(tank.Squad, accepted);
            return;
        }

        position = GroundPoint(position, tank.Position.y, out _, out _);
        var stamp = proposed with { Destination = position };
        PublishSquadIntent(tank.Squad, stamp);

        if (GroundAiDirector.ExecuteCommanderVehicleOrder(
                tank.Vehicle, tank.Squad, () => tank.Squad.HoldArea(position, radius, false)))
        {
            LastOrders[tank.SquadId] = stamp;
        }
    }

    private static void IssueArmorAttack(
        TankInfo tank,
        int objectiveId,
        ArmorRole role,
        Vector3 direction,
        Vector3 objective,
        float radius)
    {
        var proposed = new OrderStamp(objectiveId, ToCommanderRole(role), CommanderAction.Attack,
            objective, direction, radius);
        if (TryKeepAcceptedOrder(tank.SquadId, proposed, ignoreRole: false, out var accepted))
        {
            PublishSquadIntent(tank.Squad, accepted);
            return;
        }

        direction = Flatten(direction).normalized;
        if (direction.sqrMagnitude < 0.01f)
            return;

        var stamp = new OrderStamp(objectiveId, ToCommanderRole(role), CommanderAction.Attack,
            objective, direction, radius);
        PublishSquadIntent(tank.Squad, stamp);

        if (GroundAiDirector.ExecuteCommanderVehicleOrder(
                tank.Vehicle, tank.Squad,
                () => tank.Squad.AttackFromSide(direction, objective, radius)))
        {
            LastOrders[tank.SquadId] = stamp;
        }
    }

    private static CommanderRole ToCommanderRole(ArmorRole role) => role switch
    {
        ArmorRole.AssaultSupport => CommanderRole.Assault,
        ArmorRole.FlankSupport => CommanderRole.Flank,
        _ => CommanderRole.Reserve
    };

    private static Vector3 Perpendicular(Vector3 direction)
    {
        var perpendicular = Vector3.Cross(Vector3.up, Flatten(direction)).normalized;
        return perpendicular.sqrMagnitude < 0.01f ? Vector3.right : perpendicular;
    }

    private static float CenteredLaneOffset(int index, float spacing)
    {
        if (index <= 0)
            return 0f;
        var step = (index + 1) / 2;
        return step * spacing * (index % 2 == 1 ? -1f : 1f);
    }

    private static bool IssueHold(
        SquadInfo squad,
        int objectiveId,
        CommanderRole role,
        CommanderAction action,
        Vector3 position,
        float radius,
        Vector3 threat,
        bool requireLineOfSight = false,
        float minimumThreatDistance = 0f,
        bool requireAuthoredCover = false)
    {
        var proposed = new OrderStamp(
            objectiveId, role, action, position, Vector3.zero, radius);
        if (TryKeepAcceptedOrder(squad.Id, proposed, ignoreRole: false, out var accepted))
        {
            PublishSquadIntent(squad.Squad, accepted);
            GroundAiDirector.RegisterCommanderDefensiveArea(
                squad.Squad, accepted.Destination, accepted.Radius);
            return true;
        }

        position = GroundPoint(position, squad.Position.y, out _, out _);
        position = FindCommanderCoverPosition(
            squad, position, threat, radius, requireLineOfSight, minimumThreatDistance,
            out var authoredCoverSelected);
        if (requireAuthoredCover && !authoredCoverSelected)
            return false;

        var stamp = new OrderStamp(objectiveId, role, action, position, Vector3.zero, radius);
        PublishSquadIntent(squad.Squad, stamp);
        GroundAiDirector.RegisterCommanderDefensiveArea(squad.Squad, position, radius);

        if (GroundAiDirector.ExecuteCommanderSquadOrder(
                squad.Squad, () => squad.Squad.HoldArea(position, radius, false)))
        {
            LastOrders[squad.Id] = stamp;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Snaps a geometric squad anchor (a golden-angle sector point or a staging
    /// lane) onto the densest protective cover cluster nearby, so squads occupy
    /// buildings and trench lines instead of open fields. Runs only at commander
    /// planning cadence and bounds its native query with a fixed candidate cap.
    /// </summary>
    private static bool TryFindCommanderCoverCluster(
        Vector3 geometricAnchor,
        Vector3 threatDirection,
        string faction,
        float fallbackHeight,
        Vector3? threatPosition,
        out Vector3 snappedAnchor,
        out int clusterCount)
    {
        snappedAnchor = geometricAnchor;
        clusterCount = 0;
        threatDirection = Flatten(threatDirection);
        if (!IsFinite(geometricAnchor) || string.IsNullOrEmpty(faction) ||
            threatDirection.sqrMagnitude < 0.01f)
        {
            return false;
        }

        threatDirection.Normalize();
        var candidates = new List<CoverClusterCandidate>(CommanderClusterCandidateLimit);
        try
        {
            var covers = CoverManager.GetCovers(
                geometricAnchor, CommanderClusterSearchRadiusMeters, faction,
                threatDirection, true);
            if (covers == null)
                return false;

            var examined = 0;
            foreach (var rawCandidate in covers)
            {
                if (++examined > CommanderClusterCandidateLimit)
                    break;

                try
                {
                    var candidate = rawCandidate.TryCast<AiDestination>();
                    if (candidate == null || candidate.WasCollected ||
                        candidate.Pointer == IntPtr.Zero || candidate.IsVehicle() ||
                        candidate.IsCoverDestroyed() || candidate.IsUnsafeCover() ||
                        !candidate.IsCoverAvailable(threatDirection, faction) ||
                        !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(
                            candidate, out var coverPosition) ||
                        !IsFinite(coverPosition))
                    {
                        continue;
                    }

                    // Dug-in and low-wall (crouch/prone-pose) slots protect better
                    // than a standing roadside node, so they weight the cluster center
                    // toward the more survivable fighting positions.
                    var weight = candidate.GetCoverPose() == SoldierPose.Idle
                        ? StandingClusterWeight
                        : ProtectiveClusterWeight;
                    // A slot that can see the attack axis lets the defender return
                    // fire; weight the cluster toward those over equally protected but
                    // blind ones. Only sampled at the 8 s commander cadence, capped by
                    // CommanderClusterCandidateLimit.
                    if (threatPosition.HasValue &&
                        HasLineOfSight(coverPosition, threatPosition.Value))
                    {
                        weight *= ClusterFiringLaneWeightMultiplier;
                    }

                    candidates.Add(new CoverClusterCandidate(
                        new MapPoint(coverPosition.x, coverPosition.z), weight));
                }
                catch (NullReferenceException)
                {
                    // Native cover lists can briefly retain a torn-down destination.
                }
                catch (Il2CppInterop.Runtime.Il2CppException)
                {
                }
                catch (Il2CppInterop.Runtime.ObjectCollectedException)
                {
                }
            }
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

        if (!CoverClusterCore.TrySelect(
                candidates,
                new MapPoint(geometricAnchor.x, geometricAnchor.z),
                CommanderClusterMaxShiftMeters,
                out var center,
                out clusterCount))
        {
            return false;
        }

        snappedAnchor = GroundPoint(
            new Vector3(center.X, geometricAnchor.y, center.Z), fallbackHeight, out _, out _);
        return IsFinite(snappedAnchor);
    }

    private static Vector3 FindCommanderCoverPosition(
        SquadInfo squad,
        Vector3 requested,
        Vector3 threat,
        float orderRadius,
        bool requireLineOfSight,
        float minimumThreatDistance,
        out bool authoredCoverSelected)
    {
        authoredCoverSelected = false;
        var towardThreat = Flatten(threat - requested);
        if (!IsFinite(requested) || !IsFinite(threat) || towardThreat.sqrMagnitude < 0.01f ||
            string.IsNullOrEmpty(squad.Faction))
        {
            return requested;
        }

        towardThreat.Normalize();
        var searchRadius = Mathf.Clamp(
            orderRadius, MinimumCommanderCoverSearchRadius, MaximumCommanderCoverSearchRadius);
        try
        {
            var candidates = CoverManager.GetCovers(
                requested, searchRadius, squad.Faction, towardThreat, true);
            if (candidates == null)
                return requested;

            Vector3? selected = null;
            var selectedScore = float.MaxValue;
            var examined = 0;
            foreach (var rawCandidate in candidates)
            {
                if (++examined > CommanderCoverCandidateLimit)
                    break;

                try
                {
                    var candidate = rawCandidate.TryCast<AiDestination>();
                    if (candidate == null || candidate.WasCollected ||
                        candidate.Pointer == IntPtr.Zero || candidate.IsVehicle() ||
                        candidate.IsCoverDestroyed() || candidate.IsUnsafeCover() ||
                        !candidate.IsCoverAvailable(towardThreat, squad.Faction) ||
                        !ExclusiveCoverAssignmentPatch.TryGetUsableCoverPosition(
                            candidate, out var coverPosition) ||
                        !IsFinite(coverPosition) ||
                        HorizontalDistance(coverPosition, requested) > searchRadius ||
                        minimumThreatDistance > 0f &&
                        HorizontalDistance(coverPosition, threat) < minimumThreatDistance ||
                        requireLineOfSight && !HasLineOfSight(coverPosition, threat))
                    {
                        continue;
                    }

                    var offset = Flatten(coverPosition - requested);
                    var posePenalty = candidate.GetCoverPose() == SoldierPose.Idle
                        ? CommanderStandingCoverPenalty
                        : 0f;
                    var score = offset.sqrMagnitude + posePenalty;
                    if (score > selectedScore + 0.01f ||
                        Mathf.Abs(score - selectedScore) <= 0.01f &&
                        selected is { } existing && !EarlierPosition(coverPosition, existing))
                    {
                        continue;
                    }

                    selected = coverPosition;
                    selectedScore = score;
                }
                catch (NullReferenceException)
                {
                    // Native cover lists can briefly retain a torn-down destination.
                }
                catch (Il2CppInterop.Runtime.Il2CppException)
                {
                }
                catch (Il2CppInterop.Runtime.ObjectCollectedException)
                {
                }
            }

            authoredCoverSelected = selected.HasValue;
            return selected ?? requested;
        }
        catch (NullReferenceException)
        {
            return requested;
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            return requested;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return requested;
        }
    }

    private static bool EarlierPosition(Vector3 candidate, Vector3 existing)
    {
        if (candidate.x < existing.x - 0.01f)
            return true;
        if (candidate.x > existing.x + 0.01f)
            return false;
        if (candidate.z < existing.z - 0.01f)
            return true;
        if (candidate.z > existing.z + 0.01f)
            return false;
        return candidate.y < existing.y;
    }

    private static void IssueAttack(
        SquadInfo squad,
        int objectiveId,
        CommanderRole role,
        Vector3 direction,
        Vector3 objective,
        float radius)
    {
        var proposed = new OrderStamp(
            objectiveId, role, CommanderAction.Attack, objective, direction, radius);
        if (TryKeepAcceptedOrder(squad.Id, proposed, ignoreRole: false, out var accepted))
        {
            PublishSquadIntent(squad.Squad, accepted);
            GroundAiDirector.ClearCommanderStationaryArea(squad.Squad);
            return;
        }

        direction = Flatten(direction).normalized;
        if (direction.sqrMagnitude < 0.01f)
            return;

        var stamp = new OrderStamp(
            objectiveId, role, CommanderAction.Attack, objective, direction, radius);
        PublishSquadIntent(squad.Squad, stamp);
        GroundAiDirector.ClearCommanderStationaryArea(squad.Squad);

        // The game's public method synchronizes the order and constructs the flank waypoint
        // from the squad's travel direction toward the objective.
        if (GroundAiDirector.ExecuteCommanderSquadOrder(
                squad.Squad, () => squad.Squad.AttackFromSide(direction, objective, radius)))
        {
            LastOrders[squad.Id] = stamp;
        }
    }

    private static bool TryKeepAcceptedOrder(
        int squadId,
        OrderStamp proposed,
        bool ignoreRole,
        out OrderStamp accepted)
    {
        accepted = proposed;
        if (!LastOrders.TryGetValue(squadId, out var existing))
            return false;

        var currentDecision = new StableCommanderOrder(
            existing.ObjectiveId, existing.Role, existing.Action);
        var proposedDecision = new StableCommanderOrder(
            proposed.ObjectiveId, proposed.Role, proposed.Action);
        if (CommanderOrderStabilityCore.ShouldReplace(
                currentDecision, proposedDecision, ignoreRole))
        {
            return false;
        }

        accepted = existing;
        return true;
    }

    private static void PublishSquadIntent(Squad squad, OrderStamp accepted)
        => GroundAiDirector.RefreshCommanderSquadIntent(
            squad, accepted.Role, accepted.Destination, Time.time);

    private static Vector3 SelectSupportByFireTarget(
        Vector3 objective,
        IReadOnlyList<CommanderReportSnapshot> reports)
    {
        if (reports.Count == 0)
            return objective;

        var selected = reports
            .OrderByDescending(report =>
                report.Confidence * Mathf.Max(1f, report.EstimatedPower) /
                (1f + Mathf.Max(0f, report.AgeSeconds) * 0.12f))
            .ThenBy(report => report.TargetId)
            .First();
        return new Vector3(selected.Position.X, objective.y, selected.Position.Z);
    }

    private static bool TryFindSupportByFirePosition(
        Vector3 objective,
        float objectiveRadius,
        Vector3 mainDirection,
        Vector3 firingTarget,
        IReadOnlyList<SquadInfo> squads,
        out Vector3 selected)
    {
        selected = default;
        var baseDistance = Mathf.Max(objectiveRadius + 45f, 75f);
        var offsets = new[] { 0f, -15f, 15f, -30f, 30f, -45f, 45f, -60f, 60f };
        var distanceAdjustments = new[] { 0f, 30f, -20f };
        var bestScore = float.MaxValue;
        var found = false;
        foreach (var distanceAdjustment in distanceAdjustments)
        {
            var distance = Mathf.Max(objectiveRadius + 20f, baseDistance + distanceAdjustment);
            foreach (var offset in offsets)
            {
                var direction = Quaternion.Euler(0f, offset, 0f) * mainDirection;
                var candidate = GroundPoint(objective + direction * distance, objective.y,
                    out _, out var grounded);
                if (!grounded || !HasLineOfSight(candidate, firingTarget))
                    continue;

                var congestion = squads.Count(squad =>
                    HorizontalDistance(squad.Position, candidate) < 30f);
                if (congestion > 1)
                    continue;

                var score = Mathf.Abs(distanceAdjustment) + Mathf.Abs(offset) * 0.35f +
                            congestion * 25f;
                if (score >= bestScore)
                    continue;

                selected = candidate;
                bestScore = score;
                found = true;
            }
        }

        return found;
    }

    private static bool HasLineOfSight(Vector3 origin, Vector3 target)
    {
        origin += Vector3.up * 1.5f;
        target += Vector3.up * 1.5f;
        var direction = target - origin;
        var distance = direction.magnitude;
        if (distance < 1f)
            return true;

        return !Physics.Raycast(origin, direction / distance, out var hit, distance,
                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
               hit.distance >= distance - 1f;
    }

    private static Vector3 GroundPoint(
        Vector3 requested,
        float fallbackHeight,
        out Vector3 normal,
        out bool success)
    {
        var origin = new Vector3(requested.x, Mathf.Max(requested.y, fallbackHeight) + 100f, requested.z);
        if (Physics.Raycast(origin, Vector3.down, out var hit, 240f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            normal = hit.normal;
            success = true;
            return hit.point;
        }

        normal = Vector3.up;
        success = false;
        return new Vector3(requested.x, fallbackHeight, requested.z);
    }
}
