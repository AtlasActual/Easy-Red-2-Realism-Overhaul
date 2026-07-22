using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class CommanderMvp
{
    private static void PlanSide(
        BattleManager manager,
        BattleData battle,
        SideState side,
        List<SquadInfo> squads,
        List<TankInfo> tanks,
        List<AircraftInfo> aircraft,
        float now,
        HashSet<int> nextOwned)
    {
        if (squads.Count == 0 && tanks.Count == 0)
        {
            CancelSideAssets(side);
            side.ResetOperation();
            return;
        }

        ContactReports.Clear();
        ContactKnowledge.CollectCommanderReports(battle, side.InvaderSide, now, ContactReports);

        var objective = SelectObjective(manager, battle, side, squads, tanks, ContactReports, now);
        if (objective == null)
        {
            CancelSideAssets(side);
            side.ResetOperation();
            return;
        }

        var objectiveId = manager.GetObjectiveUniqueId(objective);
        var objectivePosition = objective.GetTaskPosition();
        if (!IsFinite(objectivePosition))
        {
            CancelSideAssets(side);
            side.ResetOperation();
            return;
        }

        var objectiveRadius = Mathf.Max(10f, objective.objectiveRadius);
        var securedFaction = SecuredFactionFor(objective);
        var securedBySide = !string.IsNullOrEmpty(securedFaction) &&
                            (side.InvaderSide
                                ? battle.IsInvaderFaction(securedFaction)
                                : battle.IsDefenderFaction(securedFaction));
        var previousPosture = side.ObjectiveId == objectiveId
            ? side.Offensive ? StrategicPosture.Attack : StrategicPosture.Defend
            : (StrategicPosture?)null;
        var posture = StrategicPostureCore.Resolve(
            !string.IsNullOrEmpty(securedFaction),
            securedBySide,
            previousPosture,
            side.InvaderSide ? StrategicPosture.Attack : StrategicPosture.Defend);
        var offensiveOperation = posture == StrategicPosture.Attack;
        var factions = squads.Select(entry => entry.Faction)
            .Concat(tanks.Select(entry => entry.Faction))
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var faction in factions)
        {
            GroundAiDirector.RecordPosture(
                faction,
                objectiveId,
                offensiveOperation ? StrategicPosture.Attack : StrategicPosture.Defend,
                now);
        }

        // Fresh defensive reinforcements keep their native objective route until
        // they reach the defended ground. Claiming them at fullySpawned used to
        // replace that route at the first planning tick and strand them behind the
        // objective. Existing ownership is retained only while defending the same
        // objective, so an objective transition cannot bypass the arrival gate.
        var continuingDefense = side.ObjectiveId == objectiveId && !side.Offensive;
        var commandedSquads = offensiveOperation
            ? squads.ToArray()
            : squads.Where(squad => ShouldAssumeDefensiveCommand(
                    squad, objectivePosition, objectiveRadius, continuingDefense))
                .ToArray();

        if (side.ObjectiveId != objectiveId || side.Offensive != offensiveOperation)
        {
            CancelSideAssets(side);
            // Order stamps belong to one objective/posture revision. Keeping them
            // through a capture can make a newly defending squad retain its old
            // assault destination (or vice versa).
            foreach (var squad in squads)
                LastOrders.Remove(squad.Id);
            foreach (var tank in tanks)
                LastOrders.Remove(tank.SquadId);
            side.BeginOperation(objectiveId, offensiveOperation, now);
        }

        var relevantReports = BuildPlannerReports(ContactReports, objectivePosition, objectiveRadius, now);
        // Built once per side-planning tick so every report resolves against a
        // dictionary lookup instead of rescanning all creatures/vehicles per report.
        BuildReportTargetIndex(out var soldiersById, out var vehiclesById);
        var antiTankReports = BuildAntiTankReports(
            battle, side, ContactReports, objectivePosition, objectiveRadius, now,
            soldiersById, vehiclesById);
        // Defensive anti-tank troops keep their fortifications; nearby static guns
        // are staffed by StaticAntiTankStaffing instead of pulling a whole squad out
        // of the objective area to hunt a report.
        var antiTankTask = offensiveOperation
            ? CommanderPlannerCore.SelectAntiTankTask(
                commandedSquads.Select(entry => entry.Snapshot).ToArray(),
                commandedSquads.Where(entry => entry.HasAntiTank)
                    .Select(entry => entry.Id).ToArray(),
                antiTankReports)
            : null;
        var maneuverSquads = antiTankTask is { } task
            ? commandedSquads.Where(entry => entry.Id != task.SquadId).ToArray()
            : commandedSquads;
        var axes = BuildAxes(commandedSquads, tanks, relevantReports, objectivePosition, objectiveRadius);
        var smokeRequired = ShouldRequireSmoke(relevantReports, objectivePosition, objectiveRadius) &&
                            offensiveOperation && !side.AttackLaunched;
        var smokeReady = !smokeRequired || side.SmokeBypassed ||
                         side.SmokeReadyAt >= 0f && now >= side.SmokeReadyAt;

        var plannerInput = new CommanderPlanInput(
            ToMapPoint(objectivePosition),
            offensiveOperation,
            smokeRequired,
            smokeReady,
            maneuverSquads.Select(entry => entry.Snapshot).ToArray(),
            relevantReports,
            axes.Select(axis => axis.Candidate).ToArray(),
            Settings.AttackerAggressiveness.Value);
        var provisional = offensiveOperation
            ? AttackerPlannerCore.Plan(plannerInput)
            : DefenderPlannerCore.Plan(plannerInput);
        var operationalIds = provisional.Directives
            .Select(directive => directive.SquadId)
            .OrderBy(id => id)
            .ToArray();
        var signature = string.Join(",", operationalIds);

        var savedAxesStillExist = side.MainAxisId == null ||
                                  axes.Any(axis => axis.Candidate.Id == side.MainAxisId.Value);
        savedAxesStillExist &= side.FlankAxisId == null ||
                               axes.Any(axis => axis.Candidate.Id == side.FlankAxisId.Value);
        if (side.OperationalSignature != signature || side.Roles.Count == 0 || !savedAxesStillExist)
        {
            side.Roles.Clear();
            foreach (var directive in provisional.Directives)
                side.Roles[directive.SquadId] = directive.Role;
            side.MainAxisId = provisional.MainAxisId;
            side.FlankAxisId = provisional.FlankAxisId;
            side.OperationalSignature = signature;
        }

        var bestAxis = SelectBestAxis(axes);
        var mainAxis = FindAxis(axes, side.MainAxisId);
        if (mainAxis == null || bestAxis != null &&
            (mainAxis.Candidate.TerrainScore < MinimumSelectedTerrainScore ||
             RuntimeAxisScore(bestAxis) - RuntimeAxisScore(mainAxis) > 0.20f))
        {
            mainAxis = bestAxis;
            side.MainAxisId = mainAxis?.Candidate.Id;
            side.FlankAxisId = null;
        }

        var flankAxis = FindAxis(axes, side.FlankAxisId);
        if (flankAxis == null || flankAxis.Candidate.Id == mainAxis?.Candidate.Id ||
            flankAxis.Candidate.TerrainScore < MinimumSelectedTerrainScore)
        {
            flankAxis = SelectSeparatedAxis(axes, mainAxis);
            side.FlankAxisId = flankAxis?.Candidate.Id;
        }
        AllocateArmorRoles(side, tanks, mainAxis, flankAxis);
        var operational = maneuverSquads.Where(entry => side.Roles.ContainsKey(entry.Id)).ToArray();
        var gate = EvaluateGate(side, operational, tanks, relevantReports, mainAxis, flankAxis,
            offensiveOperation, smokeRequired, now);

        // Complete lethal preparation first, then lay the smoke immediately before
        // releasing the assault. The old order let smoke decay during the HE mission.
        var artilleryBlocked = false;
        if ((gate.AttackAuthorized || gate.SmokeBlocked) && offensiveOperation && !side.AttackLaunched)
        {
            artilleryBlocked = CoordinateArtilleryPreparation(
                battle, side, squads, ContactReports, objectivePosition, objectiveRadius, now);
            if (artilleryBlocked)
                gate = gate with { AttackAuthorized = false };
        }
        else if (!offensiveOperation && now >= side.NextArtilleryAllowedAt)
        {
            var defensiveTarget = SelectArtilleryReport(
                ContactReports, objectivePosition, objectiveRadius, now);
            if (defensiveTarget != null)
                TryRequestLethalArtillery(battle, side, squads, defensiveTarget, now);
        }

        if (gate.SmokeBlocked && !artilleryBlocked)
        {
            if (side.SmokeBlockedAt < 0f)
                side.SmokeBlockedAt = now;

            var artilleryChannelBusy = squads.Count > 0 &&
                                       ArtilleryFamilyBusy(squads[0].Faction);

            if (side.SmokeReadyAt < 0f && now >= side.NextSmokeAllowedAt && mainAxis != null &&
                TryRequestSmoke(side, squads, mainAxis, objectivePosition, now))
            {
                side.SmokeBlockedAt = now;
            }

            if (side.SmokeReadyAt >= 0f && now >= side.SmokeReadyAt)
            {
                gate = gate with { AttackAuthorized = true, SmokeBlocked = false };
            }
            else if (side.SmokeReadyAt < 0f && !IsActiveSmokeRequest(side) &&
                     !artilleryChannelBusy &&
                     now - side.SmokeBlockedAt >= SmokeBypassSeconds)
            {
                side.SmokeBypassed = true;
                gate = gate with { AttackAuthorized = true, SmokeBlocked = false };
                AiState.Trace($"Commander {side.Name}: smoke unavailable; manoeuvre gate released after staging");
            }
        }
        else if (!gate.SmokeBlocked)
        {
            side.SmokeBlockedAt = -1f;
        }

        if (gate.AttackAuthorized)
            side.AttackLaunched = true;

        // The gate decides whether it is safe to launch, not whether an assault
        // already in contact should be cancelled by one noisy planning sample.
        // The whole assault force — infantry and armor alike — remains committed
        // until the operation changes; damaged, heavily suppressed, or
        // route-blocked units are still assigned to reserve below.
        var attackAuthorized = gate.AttackAuthorized ||
                               offensiveOperation && side.AttackLaunched;

        foreach (var squad in commandedSquads)
        {
            nextOwned.Add(squad.Id);
            var role = side.Roles.TryGetValue(squad.Id, out var assignedRole)
                ? assignedRole
                : CommanderRole.Reserve;
            var initialDestination = LastOrders.TryGetValue(squad.Id, out var acceptedOrder)
                ? acceptedOrder.Destination
                : offensiveOperation
                    ? DirectiveDestination(provisional, squad.Id, objectivePosition)
                    : DefensiveSectorPosition(side, squad.Id, objectivePosition, objectiveRadius,
                        squad.Position.y);
            var leaseRole = LastOrders.TryGetValue(squad.Id, out acceptedOrder)
                ? acceptedOrder.Role
                : role;
            GroundAiDirector.LeaseCommanderSquad(
                squad.Squad,
                GroundAiDirector.CurrentObjectiveRevision(squad.Faction),
                leaseRole,
                initialDestination,
                now);
        }
        foreach (var tank in tanks)
        {
            nextOwned.Add(tank.SquadId);
            OwnedArmorVehicles[tank.SquadId] = tank.Vehicle;
            var tankDestination = LastOrders.TryGetValue(tank.SquadId, out var acceptedTankOrder)
                ? acceptedTankOrder.Destination
                : objectivePosition;
            var tankRole = LastOrders.TryGetValue(tank.SquadId, out acceptedTankOrder)
                ? acceptedTankOrder.Role
                : CommanderRole.SupportByFire;
            GroundAiDirector.LeaseCommanderSquad(
                tank.Squad,
                GroundAiDirector.CurrentObjectiveRevision(tank.Faction),
                tankRole,
                tankDestination,
                now);
            var armorRole = side.ArmorRoles.TryGetValue(tank.Id, out var assignedArmorRole)
                ? assignedArmorRole.ToString()
                : ArmorRole.Reserve.ToString();
            GroundAiDirector.LeaseCommanderVehicle(
                tank.Vehicle,
                GroundAiDirector.CurrentObjectiveRevision(tank.Faction),
                armorRole,
                tankDestination,
                now);
        }

        // This tick's order executions replace whatever this side still has queued
        // from its previous planning tick wholesale (it should normally already be
        // empty at the 2/frame drain budget against an 8s planning cadence).
        side.PendingOrderExecutions.Clear();
        IssueOrders(side, commandedSquads, objectiveId, objectivePosition, objectiveRadius,
            offensiveOperation, mainAxis, flankAxis, antiTankTask,
            attackAuthorized, gate.SmokeBlocked, relevantReports);
        IssueArmorOrders(side, tanks, objectiveId, objectivePosition,
            objectiveRadius, offensiveOperation, mainAxis, flankAxis, attackAuthorized);
        TaskAircraft(
            battle,
            side,
            aircraft,
            ContactReports,
            objectivePosition,
            objectiveRadius,
            now,
            soldiersById,
            vehiclesById);

        var roleCounts = side.Roles.Values.GroupBy(role => role)
            .ToDictionary(group => group.Key, group => group.Count());
        var antiTankSummary = antiTankTask is { } assignedAntiTank
            ? $"{assignedAntiTank.Action}:{assignedAntiTank.SquadId}->{assignedAntiTank.TargetId}"
            : "none";
        AiState.Trace(
            $"Commander {side.Name}: objective={objectiveId}, posture=" +
            $"{(offensiveOperation ? "Attack" : "Defend")}, squads={operational.Length}, " +
            $"roles=A{RoleCount(roleCounts, CommanderRole.Assault)}/" +
            $"F{RoleCount(roleCounts, CommanderRole.Flank)}/" +
            $"S{RoleCount(roleCounts, CommanderRole.SupportByFire)}/" +
            $"R{RoleCount(roleCounts, CommanderRole.Reserve)}, " +
            $"ratio={gate.StrengthRatio:0.00}, suppression={gate.AverageSuppression:0.00}, " +
            $"reports={relevantReports.Count}, AT={antiTankSummary}, " +
            $"arriving={squads.Count - commandedSquads.Length}, " +
            $"armor={tanks.Count}, aircraft={aircraft.Count}, " +
            $"attackGate={gate.AttackAuthorized}, attackLatched={attackAuthorized}, " +
            $"smokeWait={gate.SmokeBlocked}, artilleryWait={artilleryBlocked}");
    }

    private static MissionObjective? SelectObjective(
        BattleManager manager,
        BattleData battle,
        SideState side,
        IReadOnlyList<SquadInfo> squads,
        IReadOnlyList<TankInfo> tanks,
        IReadOnlyList<ContactReport> reports,
        float now)
    {
        var centroid = AverageForcePosition(squads, tanks);
        var candidates = new List<ObjectiveCandidate>();
        var objectivesById = new Dictionary<int, MissionObjective>();

        var objectives = manager.ActiveObjectives(false).GetEnumerator();
        var objectiveIterator = objectives.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (objectiveIterator.MoveNext())
        {
            var objective = objectives.Current;
            if (objective == null || !CanAttractAnyFaction(objective, squads, tanks))
                continue;

            var position = objective.GetTaskPosition();
            if (!IsFinite(position))
                continue;

            var securedFaction = SecuredFactionFor(objective);
            var friendlySecured = !string.IsNullOrEmpty(securedFaction) &&
                                  (side.InvaderSide
                                      ? battle.IsInvaderFaction(securedFaction)
                                      : battle.IsDefenderFaction(securedFaction));
            var score = HorizontalDistance(centroid, position);
            foreach (var report in reports)
            {
                if (ReportAgeInvalid(report, now) || !IsFinite(report.LastKnownPosition))
                    continue;
                var distance = HorizontalDistance(report.LastKnownPosition, position);
                if (distance < 160f)
                    score -= Mathf.Clamp01(report.Confidence) * (1f - distance / 160f) * 45f;
            }

            var id = manager.GetObjectiveUniqueId(objective);
            candidates.Add(new ObjectiveCandidate(id, score, friendlySecured));
            objectivesById[id] = objective;
        }

        var currentObjectiveId = side.ObjectiveId >= 0 ? side.ObjectiveId : (int?)null;
        var selectedId = ObjectiveSelectionCore.Select(candidates, side.InvaderSide, currentObjectiveId);
        return selectedId is { } selectedObjectiveId &&
               objectivesById.TryGetValue(selectedObjectiveId, out var selectedObjective)
            ? selectedObjective
            : null;
    }

    // GetSecuredFaction() reports which faction secures the objective, not whether it
    // is secured — for conquest and scripted tasks it returns the invader faction even
    // before capture. Only IsSecured() reflects the scenario's actual capture state.
    private static string SecuredFactionFor(MissionObjective objective)
    {
        try
        {
            return objective.IsSecured()
                ? objective.GetSecuredFaction() ?? string.Empty
                : string.Empty;
        }
        catch (NullReferenceException)
        {
            return string.Empty;
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
            return string.Empty;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return string.Empty;
        }
    }

    private static bool CanAttractAnyFaction(
        MissionObjective objective,
        IReadOnlyList<SquadInfo> squads,
        IReadOnlyList<TankInfo> tanks)
    {
        var factions = squads.Select(squad => squad.Faction)
            .Concat(tanks.Select(tank => tank.Faction))
            .Distinct(StringComparer.Ordinal);
        foreach (var faction in factions)
        {
            if (!string.IsNullOrEmpty(faction) && objective.CanAttractFaction(faction))
                return true;
        }

        return false;
    }

    private static List<CommanderReportSnapshot> BuildPlannerReports(
        IReadOnlyList<ContactReport> reports,
        Vector3 objective,
        float objectiveRadius,
        float now)
    {
        var result = new List<CommanderReportSnapshot>();
        var maximumDistance = objectiveRadius + 160f;
        foreach (var report in reports)
        {
            if (report.Kind == ContactKind.Aircraft || ReportAgeInvalid(report, now) ||
                report.Confidence < CommanderPlannerCore.MinimumReportConfidence ||
                !IsFinite(report.LastKnownPosition) ||
                HorizontalDistance(report.LastKnownPosition, objective) > maximumDistance)
            {
                continue;
            }

            var vehicle = report.Kind == ContactKind.GroundVehicle;
            var targetId = report.TargetSquadId != 0 ? report.TargetSquadId : report.TargetId;
            result.Add(new CommanderReportSnapshot(
                targetId,
                ToMapPoint(report.LastKnownPosition),
                vehicle ? CommanderContactType.GroundVehicle : CommanderContactType.Infantry,
                Mathf.Max(0f, now - report.ObservedAt),
                Mathf.Clamp01(report.Confidence),
                (vehicle ? 8f : 4f) * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(report.Confidence))));
        }

        return result;
    }

    private static List<CommanderReportSnapshot> BuildAntiTankReports(
        BattleData battle,
        SideState side,
        IReadOnlyList<ContactReport> reports,
        Vector3 objective,
        float objectiveRadius,
        float now,
        Dictionary<int, Soldier> soldiersById,
        Dictionary<int, Vehicle> vehiclesById)
    {
        var result = new List<CommanderReportSnapshot>();
        var maximumDistance = objectiveRadius + 160f;
        foreach (var report in reports)
        {
            var age = now - report.ObservedAt;
            if (report.Kind != ContactKind.GroundVehicle ||
                !float.IsFinite(age) || age < 0f ||
                age > CommanderPlannerCore.MaximumAntiTankReportAgeSeconds ||
                report.Confidence < CommanderPlannerCore.MinimumAntiTankReportConfidence ||
                !IsFinite(report.LastKnownPosition) ||
                HorizontalDistance(report.LastKnownPosition, objective) > maximumDistance ||
                !TryResolveReportTarget(
                    report, battle, side.InvaderSide, soldiersById, vehiclesById,
                    out var target, out _))
            {
                continue;
            }

            try
            {
                var vehicle = target.TryCast<Vehicle>();
                if (vehicle == null || vehicle.GetComponent<VehicleTank>() == null)
                    continue;

                var confidence = Mathf.Clamp01(report.Confidence);
                var targetId = report.TargetSquadId != 0
                    ? report.TargetSquadId
                    : report.TargetId;
                result.Add(new CommanderReportSnapshot(
                    targetId,
                    ToMapPoint(report.LastKnownPosition),
                    CommanderContactType.GroundVehicle,
                    age,
                    confidence,
                    8f * Mathf.Lerp(0.5f, 1f, confidence)));
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // The report stays in shared knowledge, but not in the live AT task list.
            }
        }

        return result;
    }

    private static List<AxisRuntime> BuildAxes(
        IReadOnlyList<SquadInfo> squads,
        IReadOnlyList<TankInfo> tanks,
        IReadOnlyList<CommanderReportSnapshot> reports,
        Vector3 objective,
        float objectiveRadius)
    {
        var centroid = AverageForcePosition(squads, tanks);
        var friendlyDirection = Flatten(centroid - objective);
        if (friendlyDirection.sqrMagnitude < 0.01f)
            friendlyDirection = Vector3.back;
        friendlyDirection.Normalize();

        var stagingDistance = Mathf.Max(objectiveRadius + 45f, 75f);
        var offsets = new[] { 0f, -25f, 25f, -45f, 45f, -60f, 60f, -75f, 75f };
        var axes = new List<AxisRuntime>(offsets.Length);
        for (var index = 0; index < offsets.Length; index++)
        {
            var direction = Quaternion.Euler(0f, offsets[index], 0f) * friendlyDirection;
            var requested = objective + direction * stagingDistance;
            var grounded = GroundPoint(requested, objective.y, out var normal, out var groundedSuccessfully);
            var terrain = ScoreTerrainRoute(grounded, objective, normal, groundedSuccessfully);
            var congestion = ScoreCongestion(grounded, objective, squads, tanks);
            var exposure = ScoreExposure(grounded, objective, reports);
            var bearing = Mathf.Repeat(Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg, 360f);
            axes.Add(new AxisRuntime(
                new CommanderAxisCandidate(
                    index,
                    ToMapPoint(grounded),
                    bearing,
                    terrain,
                    congestion,
                    exposure),
                grounded,
                direction.normalized));
        }

        return axes;
    }

    private static float ScoreTerrainRoute(
        Vector3 staging,
        Vector3 objective,
        Vector3 stagingNormal,
        bool groundedSuccessfully)
    {
        if (!groundedSuccessfully)
            return 0.15f;

        var score = Mathf.InverseLerp(0.55f, 1f, Vector3.Dot(stagingNormal.normalized, Vector3.up));
        var previousHeight = staging.y;
        for (var sample = 1; sample <= 3; sample++)
        {
            var point = Vector3.Lerp(staging, objective, sample / 4f);
            var grounded = GroundPoint(point, Mathf.Lerp(staging.y, objective.y, sample / 4f),
                out var normal, out var success);
            if (!success)
            {
                score -= 0.18f;
                continue;
            }

            score -= Mathf.InverseLerp(5f, 16f, Mathf.Abs(grounded.y - previousHeight)) * 0.18f;
            score -= Mathf.InverseLerp(0.75f, 0.35f, Vector3.Dot(normal.normalized, Vector3.up)) * 0.12f;
            previousHeight = grounded.y;
        }

        return Mathf.Clamp01(score);
    }

    private static float ScoreCongestion(
        Vector3 staging,
        Vector3 objective,
        IReadOnlyList<SquadInfo> squads,
        IReadOnlyList<TankInfo> tanks)
    {
        var score = 0f;
        foreach (var squad in squads)
        {
            if (HorizontalDistance(squad.Position, staging) < 38f)
                score += 0.18f;
            if (DistanceToSegment(squad.Position, staging, objective) < 22f)
                score += 0.08f;
        }

        foreach (var tank in tanks)
        {
            if (HorizontalDistance(tank.Position, staging) < 48f)
                score += 0.24f;
            if (DistanceToSegment(tank.Position, staging, objective) < TankLaneSpacingMeters)
                score += 0.12f;
        }

        return Mathf.Clamp01(score);
    }

    private static float ScoreExposure(
        Vector3 staging,
        Vector3 objective,
        IReadOnlyList<CommanderReportSnapshot> reports)
    {
        var score = 0f;
        foreach (var report in reports)
        {
            var position = new Vector3(report.Position.X, objective.y, report.Position.Z);
            var distance = DistanceToSegment(position, staging, objective);
            if (distance >= 70f)
                continue;

            var weight = report.Type == CommanderContactType.GroundVehicle ? 0.34f : 0.22f;
            score += weight * Mathf.Clamp01(report.Confidence) * (1f - distance / 70f);
        }

        return Mathf.Clamp01(score);
    }

    private static void AllocateArmorRoles(
        SideState side,
        IReadOnlyList<TankInfo> tanks,
        AxisRuntime? mainAxis,
        AxisRuntime? flankAxis)
    {
        var mainUsable = mainAxis != null &&
                         mainAxis.Candidate.TerrainScore >= MinimumTankTerrainScore;
        var flankUsable = flankAxis != null &&
                          flankAxis.Candidate.TerrainScore >= MinimumTankTerrainScore;
        var tankStates = tanks
            .Select(tank => new ArmorTankState(
                tank.Id, tank.HullFraction, tank.Suppression, tank.EffectivePower))
            .ToArray();
        side.ArmorAllocation = ArmorRoleAllocationCore.Allocate(
            tankStates, side.ArmorAllocation, mainUsable, flankUsable);

        side.ArmorRoles.Clear();
        foreach (var pair in side.ArmorAllocation.Roles)
            side.ArmorRoles[pair.Key] = ToLocalArmorRole(pair.Value);
    }

    private static ArmorRole ToLocalArmorRole(ArmorRoleAssignment assignment) => assignment switch
    {
        ArmorRoleAssignment.AssaultSupport => ArmorRole.AssaultSupport,
        ArmorRoleAssignment.FlankSupport => ArmorRole.FlankSupport,
        _ => ArmorRole.Reserve
    };

    private static AttackGate EvaluateGate(
        SideState side,
        IReadOnlyList<SquadInfo> operational,
        IReadOnlyList<TankInfo> tanks,
        IReadOnlyList<CommanderReportSnapshot> reports,
        AxisRuntime? mainAxis,
        AxisRuntime? flankAxis,
        bool offensive,
        bool smokeRequired,
        float now)
    {
        var committedStrength = 0f;
        var weightedSuppression = 0f;
        var hasAttackRole = false;
        var attackElementCount = 0;
        var terrainUsable = true;
        foreach (var squad in operational)
        {
            var role = side.Roles[squad.Id];
            if (role != CommanderRole.Reserve)
            {
                committedStrength += squad.Snapshot.EffectiveStrength;
                weightedSuppression += squad.Snapshot.EffectiveStrength * squad.Snapshot.Suppression;
            }

            if (role == CommanderRole.Assault)
            {
                hasAttackRole |= mainAxis != null;
                if (mainAxis != null)
                    attackElementCount++;
                terrainUsable &= mainAxis != null &&
                                  mainAxis.Candidate.TerrainScore >= MinimumSelectedTerrainScore;
            }
            else if (role == CommanderRole.Flank)
            {
                hasAttackRole |= flankAxis != null;
                if (flankAxis != null)
                    attackElementCount++;
                terrainUsable &= flankAxis != null &&
                                  flankAxis.Candidate.TerrainScore >= MinimumSelectedTerrainScore;
            }
        }

        foreach (var tank in tanks)
        {
            if (!side.ArmorRoles.TryGetValue(tank.Id, out var role) || role == ArmorRole.Reserve)
                continue;

            committedStrength += tank.EffectivePower;
            weightedSuppression += tank.EffectivePower * tank.Suppression;
            var axis = role == ArmorRole.FlankSupport ? flankAxis : mainAxis;
            hasAttackRole |= axis != null;
            if (axis != null)
                attackElementCount++;
            terrainUsable &= axis != null &&
                              axis.Candidate.TerrainScore >= MinimumTankTerrainScore;
        }

        var enemyPower = reports.Sum(report => Mathf.Max(0f, report.EstimatedPower));
        var ratio = committedStrength / Mathf.Max(1f, enemyPower);
        var averageSuppression = committedStrength > 0f
            ? weightedSuppression / committedStrength
            : 1f;
        // An armor-only force can never accumulate enough EffectivePower to clear
        // the full infantry-and-armor ratio against a continuously re-spotted
        // enemy tank, and it has no second infantry element to satisfy the
        // attackElementCount floor. Both carve-outs apply to any all-armor force,
        // not only a single lone tank.
        var armorOnlyForce = operational.Count == 0 && tanks.Count >= 1;
        var loneTankElement = attackElementCount == 1 && armorOnlyForce;
        var requiredRatio = CommanderPlannerCore.MinimumAttackRatioFor(
            Settings.AttackerAggressiveness.Value);
        if (armorOnlyForce)
            requiredRatio = Mathf.Min(requiredRatio, 1.0f);
        var candidate = offensive && (attackElementCount >= 2 || loneTankElement) &&
                        hasAttackRole && terrainUsable &&
                        ratio >= requiredRatio &&
                        averageSuppression <= CommanderPlannerCore.MaximumAverageSuppressionFor(
                            Settings.AttackerAggressiveness.Value);
        var smokeReady = !smokeRequired || side.SmokeBypassed ||
                         side.SmokeReadyAt >= 0f && now >= side.SmokeReadyAt;
        var smokeBlocked = candidate && smokeRequired && !smokeReady;
        return new AttackGate(candidate && !smokeBlocked, smokeBlocked, ratio, averageSuppression);
    }

    private static bool ShouldAssumeDefensiveCommand(
        SquadInfo squad,
        Vector3 objective,
        float objectiveRadius,
        bool allowExistingOwnership)
    {
        FindReachedDefensiveFortification(
            squad, objective, objectiveRadius + DefensiveHoldMarginMeters,
            out var onCover, out var onStaticWeapon);
        return DefensivePositioningCore.ShouldAssumeCommand(
            offensive: false,
            alreadyOwned: allowExistingOwnership && GroundAiDirector.OwnsSquad(squad.Squad),
            occupiesFortification: onCover || onStaticWeapon,
            squadPosition: ToMapPoint(squad.Position),
            objective: ToMapPoint(objective),
            objectiveRadius: objectiveRadius,
            arrivalMargin: DefensiveArrivalMarginMeters);
    }

    private static bool ShouldPreserveDefensiveFortification(
        SquadInfo squad,
        Vector3 objective,
        float holdRadius)
    {
        FindReachedDefensiveFortification(
            squad, objective, holdRadius, out var onCover, out var onStaticWeapon);
        return DefensivePositioningCore.ShouldPreserveFortification(
            squad.Squad.order == Order.defend,
            onCover,
            onStaticWeapon);
    }

    private static void FindReachedDefensiveFortification(
        SquadInfo squad,
        Vector3 objective,
        float radius,
        out bool onCover,
        out bool onStaticWeapon)
    {
        onCover = false;
        onStaticWeapon = false;
        try
        {
            for (var index = 0; index < squad.Squad.CountMembers; index++)
            {
                var member = squad.Squad.GetMember(index);
                if (member == null || !member.CanFight())
                    continue;

                var memberPosition = member.transform.position;
                if (!DefensivePositioningCore.IsInsideArea(
                        ToMapPoint(memberPosition), ToMapPoint(objective), radius))
                {
                    continue;
                }

                onCover |= ContactResponse.IsOnUsableCover(member);
                if (member.IsOnVehicle())
                {
                    var vehicle = member.GetCurrentVehicle();
                    onStaticWeapon |= vehicle != null && vehicle.IsStatic();
                }

                if (onCover && onStaticWeapon)
                    return;
            }
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
    }

    private static bool TryFindOccupiedDefensiveCoverAnchor(
        SquadInfo squad,
        Vector3 objective,
        float radius,
        out Vector3 anchor)
    {
        anchor = default;
        var total = Vector3.zero;
        var count = 0;
        try
        {
            for (var index = 0; index < squad.Squad.CountMembers; index++)
            {
                var member = squad.Squad.GetMember(index);
                if (member == null || !member.CanFight() || member.IsOnVehicle() ||
                    !ContactResponse.IsOnUsableCover(member))
                {
                    continue;
                }

                var position = member.transform.position;
                if (!IsFinite(position) ||
                    !DefensivePositioningCore.IsInsideArea(
                        ToMapPoint(position), ToMapPoint(objective), radius))
                {
                    continue;
                }

                total += position;
                count++;
            }
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

        if (count == 0)
            return false;

        anchor = total / count;
        return IsFinite(anchor);
    }

    private static Vector3 DirectiveDestination(
        CommanderPlan plan,
        int squadId,
        Vector3 fallback)
    {
        var directive = plan.Directives.FirstOrDefault(candidate => candidate.SquadId == squadId);
        return directive.SquadId == squadId && directive.Destination.IsFinite
            ? new Vector3(directive.Destination.X, fallback.y, directive.Destination.Z)
            : fallback;
    }

    private static Vector3 DefensiveSectorPosition(
        SideState side,
        int squadId,
        Vector3 objective,
        float objectiveRadius,
        float fallbackHeight)
    {
        var point = ObjectiveFormationCore.DefensiveSector(
            ToMapPoint(objective), objectiveRadius, side.GetPositionSlot(squadId));
        return GroundPoint(
            new Vector3(point.X, objective.y, point.Z), fallbackHeight, out _, out _);
    }

    private static Vector3 AttackEntryPosition(
        SideState side,
        int squadId,
        Vector3 objective,
        float objectiveRadius,
        float fallbackHeight)
    {
        var point = ObjectiveFormationCore.AttackEntry(
            ToMapPoint(objective), objectiveRadius, side.GetPositionSlot(squadId));
        return GroundPoint(
            new Vector3(point.X, objective.y, point.Z), fallbackHeight, out _, out _);
    }

    private static bool ReportAgeInvalid(ContactReport report, float now)
    {
        var age = now - report.ObservedAt;
        return !float.IsFinite(age) || age < 0f ||
               age > CommanderPlannerCore.MaximumReportAgeSeconds;
    }

    private static Vector3 AveragePosition(IReadOnlyList<SquadInfo> squads)
    {
        if (squads.Count == 0)
            return Vector3.zero;

        var total = Vector3.zero;
        foreach (var squad in squads)
            total += squad.Position;
        return total / squads.Count;
    }

    private static Vector3 AverageForcePosition(
        IReadOnlyList<SquadInfo> squads,
        IReadOnlyList<TankInfo> tanks)
    {
        if (squads.Count == 0 && tanks.Count == 0)
            return Vector3.zero;

        var total = Vector3.zero;
        foreach (var squad in squads)
            total += squad.Position;
        foreach (var tank in tanks)
            total += tank.Position;
        return total / (squads.Count + tanks.Count);
    }

    private static AxisRuntime? FindAxis(IReadOnlyList<AxisRuntime> axes, int? id)
    {
        if (id == null)
            return null;
        return axes.FirstOrDefault(axis => axis.Candidate.Id == id.Value);
    }

    private static AxisRuntime? SelectBestAxis(IReadOnlyList<AxisRuntime> axes)
    {
        return axes
            .OrderByDescending(RuntimeAxisScore)
            .ThenBy(axis => axis.Candidate.Id)
            .FirstOrDefault();
    }

    private static AxisRuntime? SelectSeparatedAxis(
        IReadOnlyList<AxisRuntime> axes,
        AxisRuntime? mainAxis)
    {
        if (mainAxis == null)
            return null;

        return axes
            .Where(axis => axis.Candidate.Id != mainAxis.Candidate.Id &&
                           BearingSeparation(axis.Candidate.BearingDegrees,
                               mainAxis.Candidate.BearingDegrees) >=
                           CommanderPlannerCore.MinimumAxisSeparationDegrees)
            .OrderByDescending(RuntimeAxisScore)
            .ThenBy(axis => axis.Candidate.Id)
            .FirstOrDefault();
    }

    private static float RuntimeAxisScore(AxisRuntime axis)
        => Mathf.Clamp01(axis.Candidate.TerrainScore) -
           Mathf.Clamp01(axis.Candidate.CongestionScore) -
           Mathf.Clamp01(axis.Candidate.ExposureScore);

    private static float BearingSeparation(float first, float second)
    {
        var delta = Mathf.Abs(Mathf.DeltaAngle(first, second));
        return Mathf.Min(delta, 360f - delta);
    }
}
