using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class CommanderMvp
{
    private static void RefreshSupportRequest(SideState side, float now)
    {
        var stamp = side.ActiveSupport;
        if (stamp == null)
            return;

        try
        {
            var squad = stamp.Squad;
            if (squad == null || HasPlayerMember(squad) || HasScriptAssignedMember(squad) ||
                ScriptLockedSquads.Contains(stamp.SquadId) || !squad.RadioRequestActive() ||
                squad.radioRequest != stamp.Request)
            {
                CancelSupportRequest(side);
                return;
            }

            if (squad.RadioRequestActiveAndConfirmed())
            {
                MarkSupportConfirmed(side, stamp, now);
                return;
            }

            if (squad.RadioRequestWaiting() && now - stamp.IssuedAt > SupportRequestTimeoutSeconds)
                CancelSupportRequest(side);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            side.ActiveSupport = null;
        }
    }

    internal static void OnRadioRequestConfirmed(Squad? squad, float now)
    {
        if (squad == null)
            return;

        MarkMatchingSupportConfirmed(Invaders, squad, now);
        MarkMatchingSupportConfirmed(Defenders, squad, now);
    }

    private static void MarkMatchingSupportConfirmed(SideState side, Squad squad, float now)
    {
        var stamp = side.ActiveSupport;
        if (stamp == null || stamp.SquadId != ContactKnowledge.GetSquadId(squad) ||
            stamp.Request != squad.radioRequest)
        {
            return;
        }

        MarkSupportConfirmed(side, stamp, now);
    }

    private static void MarkSupportConfirmed(SideState side, SupportRequestStamp stamp, float now)
    {
        if (stamp.ConfirmedAt >= 0f)
            return;

        stamp.ConfirmedAt = now;
        if (stamp.Request == RadioRequest.artillerySmoke)
            side.SmokeReadyAt = now + SmokeScreenDelaySeconds;
        else
            side.ArtilleryReadyAt = now + ArtilleryPreparationDelaySeconds;
        AiState.Trace($"Commander {side.Name}: radio request accepted by native support AI");
    }

    private static void CancelSupportForSquad(SideState side, int squadId)
    {
        if (side.ActiveSupport?.SquadId == squadId)
            CancelSupportRequest(side);
    }

    private static void CancelSupportRequest(SideState side)
    {
        var stamp = side.ActiveSupport;
        side.ActiveSupport = null;
        if (stamp == null)
            return;

        try
        {
            if (stamp.Squad != null && stamp.Squad.RadioRequestActive() &&
                stamp.Squad.radioRequest == stamp.Request)
            {
                stamp.Squad.CancelRadioRequest();
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // The request disappeared with its owning squad.
        }
    }

    private static void CancelSideAssets(SideState side)
    {
        CancelSupportRequest(side);
        CancelAircraftTasks(side);
        // A side losing its assets (no commanded units, releasing ownership, phase
        // reset) must not let stale queued order executions fire later against a
        // squad this side no longer commands.
        side.PendingOrderExecutions.Clear();
    }

    internal static Soldier? ValidateCommanderArtillerySelection(string faction, Soldier? selected)
    {
        var pendingCommanderRequest = !string.IsNullOrEmpty(_supportSelectionFaction) &&
                                      ResourcesManager.IsSameFaction(
                                          _supportSelectionFaction, faction);
        if (string.IsNullOrEmpty(faction) ||
            (!pendingCommanderRequest &&
             !SideHasActiveSupportForFaction(Invaders, faction) &&
             !SideHasActiveSupportForFaction(Defenders, faction)))
        {
            return selected;
        }

        try
        {
            // Easy Red 2 returns the first matching crewman from its global
            // creature list. That made the same gun absorb every commander
            // fire mission, and a protected first match cancelled the request
            // even when another safe gun was available.
            return SelectSafeArtilleryCrewman(faction, advanceRotation: true) ??
                   (selected != null && SafeArtilleryCrewman(selected, faction) ? selected : null);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return null;
        }
    }

    private static bool SideHasActiveSupportForFaction(SideState side, string faction)
    {
        var active = side.ActiveSupport;
        return active != null && ResourcesManager.IsSameFaction(active.Faction, faction);
    }

    private static bool TryRequestSmoke(
        SideState side,
        IReadOnlyList<SquadInfo> squads,
        AxisRuntime mainAxis,
        Vector3 objective,
        float now)
    {
        if (side.ActiveSupport != null)
            return false;

        var screen = GroundPoint(objective + mainAxis.Direction * 30f, objective.y,
            out _, out _);
        foreach (var squad in squads
                     .OrderBy(entry => SmokeRolePriority(side.Roles.GetValueOrDefault(entry.Id)))
                     .ThenBy(entry => entry.Id))
        {
            try
            {
                if (!squad.Squad.HasRadioman() || squad.Squad.RadioRequestActive() ||
                    !squad.Squad.SomeTimePassedSinceLastRadioRequest() ||
                    HorizontalDistance(squad.Position, screen) < Settings.SmokeMinimumDistance.Value)
                {
                    continue;
                }

                if (!AiState.CooldownReady(AiState.NextSmokeAttempt, squad.Id, now))
                    continue;

                var radioman = squad.Squad.GetRadioman();
                if (radioman == null || !radioman.CanFight())
                    continue;
                if (ArtilleryFamilyBusy(squad.Faction) ||
                    !SafeArtilleryAvailable(squad.Faction))
                {
                    continue;
                }

                if (!IssueSupportRequest(
                        side, squad, RadioRequest.artillerySmoke, screen, now))
                {
                    continue;
                }

                AiState.NextSmokeAttempt[squad.Id] = now + Settings.SmokeCooldownSeconds.Value;
                side.NextSmokeAllowedAt = now + SideSmokeSpacingSeconds;
                AiState.Trace($"Commander {side.Name}: squad {squad.Id} requested a deliberate smoke screen");
                return true;
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Continue to another live squad.
            }
        }

        return false;
    }

    private static bool CoordinateArtilleryPreparation(
        BattleData battle,
        SideState side,
        IReadOnlyList<SquadInfo> squads,
        IReadOnlyList<ContactReport> reports,
        Vector3 objective,
        float objectiveRadius,
        float now)
    {
        if (squads.Count == 0 || side.ArtilleryBypassed)
            return false;
        if (side.ArtilleryReadyAt >= 0f)
            return now < side.ArtilleryReadyAt;
        if (side.ActiveSupport != null)
            return true;
        if (now < side.NextArtilleryAllowedAt)
            return false;

        var target = SelectArtilleryReport(reports, objective, objectiveRadius, now);
        if (target == null)
        {
            side.ArtilleryBlockedAt = -1f;
            return false;
        }

        if (side.ArtilleryBlockedAt < 0f)
            side.ArtilleryBlockedAt = now;
        if (TryRequestLethalArtillery(battle, side, squads, target, now))
        {
            side.ArtilleryBlockedAt = now;
            return true;
        }

        if (now - side.ArtilleryBlockedAt < ArtilleryBypassSeconds)
            return true;

        side.ArtilleryBypassed = true;
        AiState.Trace($"Commander {side.Name}: artillery unavailable; attack gate released after staging");
        return false;
    }

    private static ContactReport? SelectArtilleryReport(
        IReadOnlyList<ContactReport> reports,
        Vector3 objective,
        float objectiveRadius,
        float now)
    {
        ContactReport? best = null;
        var bestScore = float.MinValue;
        foreach (var report in reports)
        {
            if (report.Kind == ContactKind.Aircraft || ReportAgeInvalid(report, now) ||
                report.Confidence < CommanderPlannerCore.MinimumReportConfidence ||
                !IsFinite(report.LastKnownPosition))
            {
                continue;
            }

            var distance = HorizontalDistance(report.LastKnownPosition, objective);
            if (distance > objectiveRadius + 150f)
                continue;

            var age = Mathf.Max(0f, now - report.ObservedAt);
            var score = (report.Kind == ContactKind.GroundVehicle ? 2.2f : 1f) +
                        Mathf.Clamp01(report.Confidence) * 2f -
                        age / CommanderPlannerCore.MaximumReportAgeSeconds -
                        distance / Mathf.Max(100f, objectiveRadius + 150f);
            if (score > bestScore)
            {
                best = report;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool TryRequestLethalArtillery(
        BattleData battle,
        SideState side,
        IReadOnlyList<SquadInfo> squads,
        ContactReport report,
        float now)
    {
        if (side.ActiveSupport != null)
            return false;

        if (!TryCollectFriendlyBattleSidePositions(
                battle, side.InvaderSide, out var friendlyPositions))
        {
            return false;
        }

        var solution = CommanderPlannerCore.SelectFireMissionAim(
            ToMapPoint(report.LastKnownPosition), friendlyPositions,
            ArtilleryFriendlyClearanceMeters, ArtilleryMaximumRetargetShiftMeters);
        if (solution == null)
        {
            AiState.Trace($"Commander {side.Name}: artillery cancelled; no safe aim point near report");
            return false;
        }

        var aim = report.LastKnownPosition;
        if (solution.Value.ShiftMeters > 0f)
        {
            aim = GroundPoint(
                new Vector3(solution.Value.Aim.X, aim.y, solution.Value.Aim.Z),
                aim.y, out _, out _);
        }

        foreach (var squad in squads
                     .OrderBy(entry => SmokeRolePriority(side.Roles.GetValueOrDefault(entry.Id)))
                     .ThenBy(entry => entry.Id))
        {
            try
            {
                if (string.IsNullOrEmpty(squad.Faction) ||
                    ArtilleryFamilyBusy(squad.Faction) || !SafeArtilleryAvailable(squad.Faction) ||
                    !squad.Squad.HasRadioman() || squad.Squad.RadioRequestActive() ||
                    !squad.Squad.SomeTimePassedSinceLastRadioRequest())
                {
                    continue;
                }

                var radioman = squad.Squad.GetRadioman();
                if (radioman == null || !radioman.CanFight())
                    continue;

                var request = report.Kind == ContactKind.GroundVehicle
                    ? RadioRequest.artilleryAPHE
                    : RadioRequest.artilleryHE;
                if (!IssueSupportRequest(side, squad, request, aim, now))
                    continue;

                side.NextArtilleryAllowedAt = now + SideArtillerySpacingSeconds;
                AiState.Trace($"Commander {side.Name}: squad {squad.Id} requested " +
                              $"{(request == RadioRequest.artilleryAPHE ? "APHE" : "HE")} preparation" +
                              (solution.Value.ShiftMeters > 0f
                                  ? $"; aim shifted {solution.Value.ShiftMeters:0}m away from " +
                                    $"{solution.Value.FriendliesAtReportedTarget} friendlies"
                                  : string.Empty));
                return true;
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // Continue to another live requester.
            }
        }

        return false;
    }

    private static bool IssueSupportRequest(
        SideState side,
        SquadInfo requester,
        RadioRequest request,
        Vector3 target,
        float now)
    {
        _supportSelectionFaction = requester.Faction;
        try
        {
            requester.Squad.GiveRadioRequest(target, request, false);
        }
        finally
        {
            _supportSelectionFaction = string.Empty;
        }

        if (!requester.Squad.RadioRequestActive() || requester.Squad.radioRequest != request)
            return false;

        side.ActiveSupport = new SupportRequestStamp(
            requester.Id, requester.Squad, requester.Faction, request, target, now);
        return true;
    }

    private static bool IsActiveSmokeRequest(SideState side)
        => side.ActiveSupport?.Request == RadioRequest.artillerySmoke;

    private static bool SafeArtilleryAvailable(string faction)
    {
        if (string.IsNullOrEmpty(faction))
            return false;

        try
        {
            return SelectSafeArtilleryCrewman(faction, advanceRotation: false) != null;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool SafeArtilleryCrewman(Soldier crewman, string faction)
    {
        if (crewman == null || !crewman.IsAlive || !AiOwnership.IsAutonomous(crewman) ||
            !crewman.CanFight() ||
            !ResourcesManager.IsSameFaction(crewman.faction, faction))
        {
            return false;
        }

        var sync = crewman.GetComponent<SyncSoldier>();
        if (sync != null && sync.IsControlledByAPlayer())
            return false;

        var gun = crewman.GetCurrentVehicle();
        if (gun == null || !gun.IsArtillery() || !gun.IsActive() ||
            !gun.IsOperative() || !gun.CanFight() || gun.life <= 0 || gun.IsDisabled() ||
            gun.PlayerIsInside() || gun.PlayerIsDriving() || VehicleHasPlayerOccupant(gun) ||
            VehicleHasScriptedOccupant(gun))
        {
            return false;
        }

        var crew = crewman.joinedSquad ?? gun.GetSquadInside();
        return crew != null && !HasPlayerMember(crew) && !HasScriptAssignedMember(crew) &&
               !ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(crew));
    }

    private static Soldier? SelectSafeArtilleryCrewman(string faction, bool advanceRotation)
    {
        var alive = Creature.aliveCreatures;
        if (alive == null)
            return null;

        // Multiple soldiers can man the same piece. Select a single proxy for
        // each gun so consecutive missions rotate across artillery pieces, not
        // merely across seats on the first one.
        var candidatesByGun = new Dictionary<int, Soldier>();
        foreach (var creature in alive)
        {
            var crewman = creature as Soldier;
            if (crewman == null || !crewman.IsOnVehicle() ||
                !RadioManager.IsNearRadio(crewman.transform.position) ||
                !SafeArtilleryCrewman(crewman, faction))
            {
                continue;
            }

            var gun = crewman.GetCurrentVehicle();
            if (gun == null)
                continue;

            var gunId = gun.GetInstanceID();
            if (!candidatesByGun.TryGetValue(gunId, out var existing) ||
                crewman.GetInstanceID() < existing.GetInstanceID())
            {
                candidatesByGun[gunId] = crewman;
            }
        }

        if (candidatesByGun.Count == 0)
            return null;

        var orderedCandidates = candidatesByGun.OrderBy(pair => pair.Key).ToArray();
        var candidateIds = orderedCandidates.Select(pair => pair.Key).ToArray();
        LastArtilleryGunByFaction.TryGetValue(faction, out var lastSelectedId);
        var index = ArtilleryCrewSelectionCore.SelectNextCandidateIndex(candidateIds, lastSelectedId);
        if (index < 0)
            return null;

        var selected = orderedCandidates[index].Value;
        if (advanceRotation)
            LastArtilleryGunByFaction[faction] = candidateIds[index];
        return selected;
    }

    internal static bool ShouldRetainArtilleryCrew(Squad? squad, Vehicle? vehicle = null)
    {
        if (squad == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || HasPlayerOwnership(squad))
        {
            return false;
        }

        try
        {
            for (var index = 0; index < squad.CountMembers; index++)
            {
                var crewman = squad.GetMember(index);
                if (crewman == null || !crewman.IsOnVehicle())
                    continue;

                var gun = crewman.GetCurrentVehicle();
                if (gun == null || (vehicle != null && gun != vehicle))
                    continue;

                if (SafeArtilleryCrewman(crewman, crewman.faction))
                    return true;
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }

        return false;
    }

    private static bool ArtilleryFamilyBusy(string faction)
    {
        var all = Squad.AllSquads;
        if (all == null)
            return true;

        foreach (var pair in all)
        {
            var squad = pair.Value;
            var leader = squad?.Leader;
            if (squad == null || leader == null ||
                !ResourcesManager.IsSameFaction(leader.faction, faction) ||
                !squad.RadioRequestActive())
            {
                continue;
            }

            var request = squad.radioRequest;
            if (request == RadioRequest.artilleryHE || request == RadioRequest.artilleryAPHE ||
                request == RadioRequest.artillerySmoke)
            {
                return true;
            }
        }

        return false;
    }

    private static bool FriendlyNearBattleSide(
        Vector3 position,
        BattleData battle,
        bool invaderSide,
        float radius)
    {
        if (!TryCollectFriendlyBattleSidePositions(battle, invaderSide, out var positions))
            return true;

        var radiusSquared = radius * radius;
        return positions.Any(point =>
        {
            var dx = point.X - position.x;
            var dz = point.Z - position.z;
            return dx * dx + dz * dz <= radiusSquared;
        });
    }

    private static bool TryCollectFriendlyBattleSidePositions(
        BattleData battle,
        bool invaderSide,
        out List<MapPoint> positions)
    {
        positions = new List<MapPoint>();
        try
        {
            var alive = Creature.aliveCreatures;
            if (alive != null)
            {
                foreach (var creature in alive)
                {
                    var soldier = creature as Soldier;
                    if (soldier == null || !soldier.IsAlive || !soldier.CanFight() ||
                        !FactionBelongsToSide(battle, invaderSide, soldier.faction))
                    {
                        continue;
                    }

                    var center = soldier.GetCenterOfUnit();
                    if (IsFinite(center))
                        positions.Add(ToMapPoint(center));
                }
            }

            var all = Vehicle.allVehicles;
            if (all != null)
            {
                foreach (var vehicle in all)
                {
                    if (vehicle == null || !vehicle.IsActive() || vehicle.life <= 0 ||
                        !FactionBelongsToSide(battle, invaderSide, vehicle.GetVehicleFaction()))
                    {
                        continue;
                    }

                    var center = vehicle.GetCenterOfUnit();
                    if (IsFinite(center))
                        positions.Add(ToMapPoint(center));
                }
            }

            return true;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            positions.Clear();
            return false;
        }
    }

    private static bool FactionBelongsToSide(BattleData battle, bool invaderSide, string faction)
        => !string.IsNullOrEmpty(faction) &&
           (invaderSide ? battle.IsInvaderFaction(faction) : battle.IsDefenderFaction(faction));

    private static bool ShouldRequireSmoke(
        IReadOnlyList<CommanderReportSnapshot> reports,
        Vector3 objective,
        float objectiveRadius)
    {
        var exposure = 0f;
        foreach (var report in reports)
        {
            var position = new Vector3(report.Position.X, objective.y, report.Position.Z);
            var distance = HorizontalDistance(position, objective);
            if (distance > objectiveRadius + 100f)
                continue;

            exposure += Mathf.Clamp01(report.Confidence) *
                        (report.Type == CommanderContactType.GroundVehicle ? 0.55f : 0.32f);
        }

        return exposure >= 0.45f;
    }

    private static int SmokeRolePriority(CommanderRole role) => role switch
    {
        CommanderRole.AntiTank => 0,
        CommanderRole.SupportByFire => 1,
        CommanderRole.Reserve => 2,
        CommanderRole.Assault => 3,
        CommanderRole.Flank => 4,
        _ => 5
    };
}
