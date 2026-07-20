using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// Host-only adapter between the deterministic commander planner and Easy Red 2's
/// synchronized combined-arms controls.
/// </summary>
internal static class CommanderMvp
{
    private const float PlanningCadenceSeconds = 8f;
    private const float OwnedAssetRefreshCadenceSeconds = 0.5f;
    private const float SmokeScreenDelaySeconds = 10f;
    private const float SmokeBypassSeconds = 16f;
    private const float SideSmokeSpacingSeconds = 60f;
    private const float ArtilleryPreparationDelaySeconds = 14f;
    private const float ArtilleryBypassSeconds = 18f;
    private const float SideArtillerySpacingSeconds = 90f;
    private const float ArtilleryFriendlyClearanceMeters = 55f;
    private const float ArtilleryMaximumRetargetShiftMeters = 80f;
    private const float SupportRequestTimeoutSeconds = 40f;
    private const float AircraftRetaskSeconds = 24f;
    private const float MinimumSelectedTerrainScore = 0.30f;
    private const float MinimumTankTerrainScore = 0.42f;
    private const float TankLaneSpacingMeters = 28f;
    private const float AntiTankDesiredStandoffMeters = 65f;
    private const float AntiTankMaximumOrderStepMeters = 35f;
    private const float AntiTankLateralOffsetMeters = 18f;
    private const float AntiTankOrderRadiusMeters = 16f;
    private const float MinimumAntiTankGroundNormal = 0.65f;
    private const float MinimumCommanderCoverSearchRadius = 12f;
    private const float MaximumCommanderCoverSearchRadius = 25f;
    private const int CommanderCoverCandidateLimit = 16;
    private const float CommanderStandingCoverPenalty = 225f;
    private const float DefensiveHoldMarginMeters = 12f;
    private const float DefensiveArrivalMarginMeters = 35f;

    private static readonly List<Squad> KnownSquads = new();
    private static readonly List<ContactReport> ContactReports = new();
    private static readonly Dictionary<int, float> PeakStrength = new();
    private static readonly HashSet<int> ScriptLockedSquads = new();
    private static readonly HashSet<int> OwnedSquads = new();
    private static readonly Dictionary<int, Vehicle> OwnedArmorVehicles = new();
    private static readonly Dictionary<int, OrderStamp> LastOrders = new();
    private static readonly SideState Invaders = new(true);
    private static readonly SideState Defenders = new(false);

    private static float _nextPlanAt;
    private static float _nextOwnedAssetRefreshAt;
    private static bool _updating;
    private static string _supportSelectionFaction = string.Empty;

    internal static void Update(BattleManager manager, float now)
    {
        if (_updating)
            return;

        if (!Settings.CommanderEnabled.Value || !MultiplayerAuthority.CanMutateGameplay())
        {
            ReleaseOwnership(restoreNativeOrders: true);
            return;
        }

        if (manager == null || !BattleManager.IsBattleActive() || BattleManager.IsBattleEnded())
        {
            ResetPhase();
            return;
        }

        if (now >= _nextOwnedAssetRefreshAt || now >= _nextPlanAt)
        {
            _nextOwnedAssetRefreshAt = now + OwnedAssetRefreshCadenceSeconds;
            RefreshOwnedAssets(now);
        }
        if (now < _nextPlanAt)
            return;

        _nextPlanAt = now + PlanningCadenceSeconds;

        _updating = true;
        try
        {
            var battle = BattleManager.GetCurrentBattleData();
            if (battle == null)
            {
                ReleaseOwnership();
                return;
            }

            GroundAiDirector.BeginCommanderPlanning();

            CollectCommandSquads(KnownSquads);
            var invaderSquads = new List<SquadInfo>();
            var defenderSquads = new List<SquadInfo>();
            var invaderTanks = new List<TankInfo>();
            var defenderTanks = new List<TankInfo>();
            var invaderAircraft = new List<AircraftInfo>();
            var defenderAircraft = new List<AircraftInfo>();
            var uniqueSquads = new HashSet<int>();
            var uniqueVehicles = new HashSet<int>();

            foreach (var squad in KnownSquads)
            {
                if (squad == null)
                    continue;

                if (squad.IsVehicleCrew)
                {
                    if (!TryDescribeTankSquad(squad, out var tank) ||
                        !uniqueSquads.Add(tank.SquadId) || !uniqueVehicles.Add(tank.Id))
                    {
                        continue;
                    }

                    if (battle.IsInvaderFaction(tank.Faction))
                        invaderTanks.Add(tank);
                    else if (battle.IsDefenderFaction(tank.Faction))
                        defenderTanks.Add(tank);
                }
                else
                {
                    if (!TryDescribeSquad(squad, out var info) || !uniqueSquads.Add(info.Id))
                        continue;

                    if (battle.IsInvaderFaction(info.Faction))
                        invaderSquads.Add(info);
                    else if (battle.IsDefenderFaction(info.Faction))
                        defenderSquads.Add(info);
                }
            }

            CollectAircraft(battle, invaderAircraft, defenderAircraft);

            var nextOwned = new HashSet<int>();
            OwnedArmorVehicles.Clear();
            PlanSide(manager, battle, Invaders, invaderSquads, invaderTanks,
                invaderAircraft, now, nextOwned);
            PlanSide(manager, battle, Defenders, defenderSquads, defenderTanks,
                defenderAircraft, now, nextOwned);

            OwnedSquads.Clear();
            foreach (var id in nextOwned)
                OwnedSquads.Add(id);

            foreach (var stale in LastOrders.Keys.Where(id => !OwnedSquads.Contains(id)).ToArray())
                LastOrders.Remove(stale);
            GroundAiDirector.CompleteCommanderPlanning();
        }
        catch (Exception ex)
        {
            GroundAiDirector.AbortCommanderPlanning();
            ReleaseOwnership();
            Plugin.LogSource.LogWarning($"Commander update failed closed: {ex.Message}");
        }
        finally
        {
            _updating = false;
        }
    }

    internal static bool OwnsSquad(Squad? squad)
    {
        if (squad == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return false;
        }

        var id = ContactKnowledge.GetSquadId(squad);
        if (!GroundAiDirector.OwnsSquad(squad))
            return false;

        if (ScriptLockedSquads.Contains(id) || HasPlayerMember(squad) || HasScriptAssignedMember(squad))
        {
            ReleaseSquad(id);
            return false;
        }

        if (OwnedArmorVehicles.TryGetValue(id, out var armor) &&
            !ArmorStillCommanderSafe(squad, armor))
        {
            ReleaseSquad(id);
            return false;
        }

        return true;
    }

    internal static bool ControlsSquadSupport(Squad? squad)
    {
        if (squad == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return false;
        }

        var id = ContactKnowledge.GetSquadId(squad);
        // This intentionally covers every commander-eligible infantry squad so the
        // legacy random-support patch cannot race the first planning tick.
        return squad.fullySpawned && !squad.IsVehicleCrew && squad.HasAliveAIMembers() &&
               !ScriptLockedSquads.Contains(id) && !HasPlayerMember(squad) &&
               !HasScriptAssignedMember(squad) &&
               GroundAiDirector.ClaimSupportChannel(squad, Time.time);
    }

    internal static bool IsMissionScriptLocked(int squadId)
        => ScriptLockedSquads.Contains(squadId);

    internal static bool HasPlayerOwnership(Squad squad)
        => HasPlayerMember(squad) || HasScriptAssignedMember(squad);

    internal static void MarkMissionScripted(Squad? squad)
    {
        if (squad == null)
            return;

        var id = ContactKnowledge.GetSquadId(squad);
        ScriptLockedSquads.Add(id);
        GroundAiDirector.MarkMissionScripted(squad);
        CancelSupportForSquad(Invaders, id);
        CancelSupportForSquad(Defenders, id);
        CancelAircraftForCrew(Invaders, id);
        CancelAircraftForCrew(Defenders, id);
        ReleaseSquad(id);
        Invaders.RemoveSquad(id);
        Defenders.RemoveSquad(id);
        AiState.Trace($"Commander released mission-scripted squad {id}");
    }

    internal static void ResetPhase()
    {
        CancelSideAssets(Invaders);
        CancelSideAssets(Defenders);
        OwnedSquads.Clear();
        OwnedArmorVehicles.Clear();
        LastOrders.Clear();
        Invaders.ResetOperation();
        Defenders.ResetOperation();
        _nextPlanAt = 0f;
        _nextOwnedAssetRefreshAt = 0f;
        GroundAiDirector.ClearRuntimeState();
    }

    internal static void ResetBattle()
    {
        ResetPhase();
        ContactKnowledge.ResetBattle();
        TransportDismount.ResetBattle();
        ScriptLockedSquads.Clear();
        PeakStrength.Clear();
        Invaders.ResetOperation(hard: true);
        Defenders.ResetOperation(hard: true);
    }

    private static void ReleaseOwnership(bool restoreNativeOrders = false)
    {
        var released = restoreNativeOrders ? OwnedSquads.ToHashSet() : null;
        CancelSideAssets(Invaders);
        CancelSideAssets(Defenders);
        OwnedSquads.Clear();
        GroundAiDirector.ReleaseAllCommanderLeases();
        OwnedArmorVehicles.Clear();
        LastOrders.Clear();
        Invaders.ResetOperation();
        Defenders.ResetOperation();
        _nextPlanAt = 0f;
        _nextOwnedAssetRefreshAt = 0f;

        if (released == null || released.Count == 0 || !BattleManager.IsBattleActive())
            return;

        var all = Squad.AllSquads;
        if (all == null)
            return;
        foreach (var pair in all)
        {
            var squad = pair.Value;
            if (squad == null || !released.Contains(ContactKnowledge.GetSquadId(squad)) ||
                ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(squad)) ||
                HasPlayerMember(squad) || HasScriptAssignedMember(squad))
            {
                continue;
            }

            try
            {
                var leader = squad.Leader;
                if (leader != null)
                    squad.Leader_OrderAttackCurrentTask(leader);
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // A despawning squad needs no hand-back order.
            }
        }
    }

    private static void ReleaseSquad(int id)
    {
        OwnedSquads.Remove(id);
        GroundAiDirector.ReleaseCommanderSquad(id);
        OwnedArmorVehicles.Remove(id);
        LastOrders.Remove(id);
    }

    private static void RefreshOwnedAssets(float now)
    {
        RefreshOwnedSquads();
        RefreshSupportRequest(Invaders, now);
        RefreshSupportRequest(Defenders, now);
        RefreshAircraftTasks(Invaders);
        RefreshAircraftTasks(Defenders);
    }

    private static void RefreshOwnedSquads()
    {
        if (OwnedSquads.Count == 0)
            return;

        var all = Squad.AllSquads;
        if (all == null)
            return;
        var seen = new HashSet<int>();
        foreach (var pair in all)
        {
            var squad = pair.Value;
            if (squad == null)
                continue;
            var id = ContactKnowledge.GetSquadId(squad);
            if (!OwnedSquads.Contains(id))
                continue;
            seen.Add(id);

            var unsafeOwnership = ScriptLockedSquads.Contains(id) || HasPlayerMember(squad) ||
                                  HasScriptAssignedMember(squad);
            if (!unsafeOwnership && OwnedArmorVehicles.TryGetValue(id, out var armor))
                unsafeOwnership = !ArmorStillCommanderSafe(squad, armor);
            if (!unsafeOwnership)
                continue;

            ReleaseSquad(id);
            Invaders.RemoveSquad(id);
            Defenders.RemoveSquad(id);
        }

        foreach (var stale in OwnedSquads.Where(id => !seen.Contains(id)).ToArray())
            ReleaseSquad(stale);
    }

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

    private static void RefreshAircraftTasks(SideState side)
    {
        foreach (var pair in side.AircraftTasks.ToArray())
        {
            var stamp = pair.Value;
            if (AircraftStillCommanderSafe(stamp.Ai, stamp.Plane))
                continue;

            CancelAircraftTask(side, pair.Key, stamp);
        }
    }

    private static void CancelAircraftForCrew(SideState side, int squadId)
    {
        foreach (var pair in side.AircraftTasks
                     .Where(pair => pair.Value.CrewSquadId == squadId)
                     .ToArray())
        {
            CancelAircraftTask(side, pair.Key, pair.Value);
        }
    }

    private static void CancelAircraftTasks(SideState side)
    {
        foreach (var pair in side.AircraftTasks.ToArray())
            CancelAircraftTask(side, pair.Key, pair.Value);
    }

    private static void CancelAircraftTask(
        SideState side,
        int aircraftId,
        AircraftTaskStamp stamp)
    {
        side.AircraftTasks.Remove(aircraftId);
        try
        {
            if (stamp.Ai != null && stamp.Plane != null)
            {
                GroundAiDirector.ExecuteCommanderAircraftOrder(
                    stamp.Ai, stamp.Plane, stamp.Ai.CancelAttackingTarget);
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // The aircraft already despawned.
        }
        GroundAiDirector.ReleaseCommanderAircraft(aircraftId);
    }

    private static void CancelSideAssets(SideState side)
    {
        CancelSupportRequest(side);
        CancelAircraftTasks(side);
    }

    internal static Soldier? ValidateCommanderArtillerySelection(string faction, Soldier? selected)
    {
        var pendingCommanderRequest = !string.IsNullOrEmpty(_supportSelectionFaction) &&
                                      ResourcesManager.IsSameFaction(
                                          _supportSelectionFaction, faction);
        if (selected == null || string.IsNullOrEmpty(faction) ||
            (!pendingCommanderRequest &&
             !SideHasActiveSupportForFaction(Invaders, faction) &&
             !SideHasActiveSupportForFaction(Defenders, faction)))
        {
            return selected;
        }

        try
        {
            return SafeArtilleryCrewman(selected, faction) ? selected : null;
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

    private static bool TryDescribeSquad(Squad squad, out SquadInfo info)
    {
        info = null!;
        try
        {
            if (squad == null || !squad.fullySpawned || squad.IsVehicleCrew ||
                !squad.HasAliveAIMembers() || HasPlayerMember(squad) ||
                HasScriptAssignedMember(squad))
            {
                return false;
            }

            var leader = squad.Leader;
            if (leader == null || !leader.CanFight() || !AiOwnership.IsAutonomous(leader))
                return false;

            var id = ContactKnowledge.GetSquadId(squad);
            if (ScriptLockedSquads.Contains(id))
                return false;

            var effectiveStrength = 0f;
            var suppressionTotal = 0f;
            var hasAntiTank = false;
            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                if (member == null || !member.CanFight())
                    continue;

                effectiveStrength += 1f;
                suppressionTotal += Mathf.Clamp01(member.GetSuppressionValue() / 255f);
                hasAntiTank |= member.IsATUnit();
            }

            if (!PeakStrength.TryGetValue(id, out var peak) || effectiveStrength > peak)
            {
                peak = effectiveStrength;
                PeakStrength[id] = peak;
            }

            var position = leader.transform.position;
            if (!IsFinite(position))
                return false;

            info = new SquadInfo(
                id,
                squad,
                leader.faction ?? string.Empty,
                position,
                hasAntiTank,
                new CommanderSquadSnapshot(
                    id,
                    ToMapPoint(position),
                    effectiveStrength,
                    Mathf.Max(peak, 1f),
                    effectiveStrength > 0f ? suppressionTotal / effectiveStrength : 1f,
                    true,
                    false,
                    false));
            return true;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool TryDescribeTankSquad(Squad squad, out TankInfo info)
    {
        info = null!;
        try
        {
            if (squad == null || !squad.fullySpawned || !squad.IsVehicleCrew ||
                !squad.HasAliveAIMembers() || HasPlayerMember(squad) ||
                HasScriptAssignedMember(squad))
            {
                return false;
            }

            var squadId = ContactKnowledge.GetSquadId(squad);
            if (ScriptLockedSquads.Contains(squadId))
                return false;

            Vehicle? vehicle = null;
            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                var occupied = member?.GetCurrentVehicle();
                if (occupied != null && occupied.GetComponent<VehicleTank>() != null)
                {
                    vehicle = occupied;
                    break;
                }
            }

            if (vehicle == null || !TankStillCommanderSafe(squad, vehicle) ||
                !vehicle.IsLocalAIDriving() || !vehicle.IsActive() || vehicle.IsStatic() ||
                vehicle.IsTransportVehicle() || vehicle.IsArtillery() || vehicle.IsAA() ||
                vehicle.life <= 0 || vehicle.Maxlife <= 0 || vehicle.IsDisabled())
            {
                return false;
            }

            var driver = vehicle.GetDriver();
            if (driver == null || !AiOwnership.IsAutonomous(driver) || !driver.CanFight())
                return false;

            var suppressionTotal = 0f;
            var fightingCrew = 0;
            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                if (member == null || !member.CanFight())
                    continue;

                fightingCrew++;
                suppressionTotal += Mathf.Clamp01(member.GetSuppressionValue() / 255f);
            }

            var position = vehicle.GetCenterOfUnit();
            if (!IsFinite(position))
                return false;

            var hullFraction = Mathf.Clamp01((float)vehicle.life / vehicle.Maxlife);
            var suppression = fightingCrew > 0 ? suppressionTotal / fightingCrew : 1f;
            info = new TankInfo(
                vehicle.GetInstanceID(),
                squadId,
                squad,
                vehicle,
                driver.faction ?? vehicle.GetVehicleFaction() ?? string.Empty,
                position,
                hullFraction,
                suppression,
                8f * hullFraction * Mathf.Lerp(1f, 0.55f, suppression));
            return true;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static void CollectAircraft(
        BattleData battle,
        List<AircraftInfo> invaders,
        List<AircraftInfo> defenders)
    {
        invaders.Clear();
        defenders.Clear();
        var vehicles = Vehicle.allVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
        {
            try
            {
                var plane = vehicle?.GetComponent<VehiclePlane>();
                var ai = vehicle?.GetComponent<AIPlane>();
                if (vehicle == null || plane == null || ai == null || !plane.IsActive() ||
                    !ai.HasAIDriver || vehicle.life <= 0 || vehicle.IsDisabled() ||
                    vehicle.PlayerIsInside() || vehicle.PlayerIsDriving() ||
                    VehicleHasPlayerOccupant(vehicle))
                {
                    continue;
                }

                var pilot = plane.GetDriver();
                if (pilot == null || !AiOwnership.IsAutonomous(pilot) || !pilot.CanFight())
                    continue;

                var faction = pilot.faction ?? plane.GetVehicleFaction() ?? string.Empty;
                var position = plane.GetCenterOfUnit();
                if (string.IsNullOrEmpty(faction) || !IsFinite(position))
                    continue;

                var info = new AircraftInfo(
                    plane.GetInstanceID(),
                    ai,
                    plane,
                    faction,
                    position,
                    plane.CountBombs() > 0);
                if (battle.IsInvaderFaction(faction))
                    invaders.Add(info);
                else if (battle.IsDefenderFaction(faction))
                    defenders.Add(info);
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // A despawning aircraft is ignored until the next command cycle.
            }
        }
    }

    private static bool VehicleHasPlayerOccupant(Vehicle vehicle)
    {
        var seatedSoldiers = vehicle.SittedSoldiers().GetEnumerator();
        var iterator = seatedSoldiers.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (iterator.MoveNext())
        {
            var seated = seatedSoldiers.Current;
            var sync = seated?.GetComponent<SyncSoldier>();
            if (sync != null && sync.IsControlledByAPlayer())
                return true;
        }

        return false;
    }

    private static bool VehicleHasScriptedOccupant(Vehicle vehicle)
    {
        try
        {
            var seatedSoldiers = vehicle.SittedSoldiers().GetEnumerator();
            var iterator = seatedSoldiers.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (iterator.MoveNext())
            {
                var seated = seatedSoldiers.Current;
                if (seated == null)
                    continue;
                var squad = seated.joinedSquad;
                if (seated.LuaSoldier?.HasScriptAssigned() == true ||
                    squad != null && ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(squad)))
                {
                    return true;
                }
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return true;
        }

        return false;
    }

    private static bool ArmorStillCommanderSafe(Squad squad, Vehicle vehicle)
    {
        try
        {
            if (vehicle == null || squad == null || ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(squad)) ||
                !vehicle.IsLocalAIDriving() || !vehicle.IsActive() || vehicle.life <= 0 ||
                vehicle.IsDisabled() || HasPlayerMember(squad) ||
                HasScriptAssignedMember(squad))
            {
                return false;
            }

            return TankStillCommanderSafe(squad, vehicle);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool TankStillCommanderSafe(Squad squad, Vehicle vehicle)
    {
        if (!vehicle.IsOperative() || !vehicle.CanFight() || !vehicle.CheckHasDriverAlive() ||
            vehicle.PlayerIsInside() || vehicle.PlayerIsDriving() || VehicleHasPlayerOccupant(vehicle) ||
            VehicleHasScriptedOccupant(vehicle))
        {
            return false;
        }

        var ai = vehicle.GetComponent<AIVehicle>();
        var driver = vehicle.GetDriver();
        var mainSeat = vehicle.GetMainTurretSeat(false);
        var gunner = mainSeat?.unitSet;
        var mainGun = mainSeat?.connectedTurret as TurretGun;
        if (ai == null || driver == null || driver.GetCurrentVehicle() != vehicle ||
            driver.joinedSquad != squad || ai.squadInside != squad ||
            squad.Leader == null || squad.Leader.GetCurrentVehicle() != vehicle ||
            mainSeat == null || gunner == null || mainGun == null ||
            gunner.GetCurrentVehicle() != vehicle || gunner.joinedSquad != squad ||
            !AiOwnership.IsAutonomous(gunner) || !gunner.CanFight() ||
            !mainGun.HasAnyAmmo())
        {
            return false;
        }

        var tracks = vehicle.GetComponentsInChildren<VehicleDamageableTrackPart>(true);
        foreach (var track in tracks)
        {
            if (track != null && track.TrackIsBroken)
                return false;
        }

        return true;
    }

    private static bool HasPlayerMember(Squad squad)
    {
        try
        {
            if (squad.IsPlayerInSquad())
                return true;

            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                if (member == null)
                    continue;

                var sync = member.GetComponent<SyncSoldier>();
                if (sync != null && sync.IsControlledByAPlayer())
                    return true;
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return true;
        }

        return false;
    }

    private static bool HasScriptAssignedMember(Squad squad)
    {
        try
        {
            for (var index = 0; index < squad.CountMembers; index++)
            {
                var member = squad.GetMember(index);
                var luaSoldier = member?.LuaSoldier;
                if (luaSoldier != null && luaSoldier.HasScriptAssigned())
                    return true;
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            // A disappearing interop object is not safe to take over this tick.
            return true;
        }

        return false;
    }

    internal static bool AllowsAutonomousTacticalOverride(Squad? squad)
    {
        if (squad == null)
            return false;

        try
        {
            return !ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(squad)) &&
                   !HasPlayerMember(squad) &&
                   !HasScriptAssignedMember(squad);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    internal static bool AllowsAutonomousSafetyResponse(Squad? squad)
    {
        if (squad == null)
            return false;

        try
        {
            // ScriptLockedSquads is deliberately permanent because the commander
            // must never reclaim a squad after a Lua order.  It is too broad for
            // an immediate safety response, though: ordinary scenario setup often
            // boards transports through Lua, which used to disable dismounting for
            // the rest of the battle.  Protect live player control and soldiers
            // with an active script assignment without treating an old boarding
            // order as a permanent veto.
            return !HasPlayerMember(squad) && !HasScriptAssignedMember(squad);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static void CollectCommandSquads(List<Squad> destination)
    {
        destination.Clear();
        var allSquads = Squad.AllSquads;
        if (allSquads == null)
            return;

        foreach (var pair in allSquads)
        {
            if (pair.Value != null)
                destination.Add(pair.Value);
        }
    }

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
        var securedFaction = objective.IsSecured() ? objective.GetSecuredFaction() : string.Empty;
        var securedBySide = !string.IsNullOrEmpty(securedFaction) &&
                            (side.InvaderSide
                                ? battle.IsInvaderFaction(securedFaction)
                                : battle.IsDefenderFaction(securedFaction));
        var offensiveOperation = !securedBySide;
        var faction = squads.FirstOrDefault()?.Faction ??
                      tanks.FirstOrDefault()?.Faction ?? string.Empty;
        var objectiveRevision = GroundAiDirector.RecordPosture(
            faction,
            objectiveId,
            offensiveOperation ? StrategicPosture.Attack : StrategicPosture.Defend,
            now);

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
            side.BeginOperation(objectiveId, offensiveOperation, now);
        }

        var relevantReports = BuildPlannerReports(ContactReports, objectivePosition, objectiveRadius, now);
        var antiTankReports = BuildAntiTankReports(
            battle, side, ContactReports, objectivePosition, objectiveRadius, now);
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
            axes.Select(axis => axis.Candidate).ToArray());
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
        // Armor remains committed until the operation changes; damaged, heavily
        // suppressed, or route-blocked vehicles are still assigned to reserve below.
        var armorAttackAuthorized = gate.AttackAuthorized ||
                                    offensiveOperation && side.AttackLaunched;

        foreach (var squad in commandedSquads)
        {
            nextOwned.Add(squad.Id);
            var role = side.Roles.TryGetValue(squad.Id, out var assignedRole)
                ? assignedRole
                : CommanderRole.Reserve;
            GroundAiDirector.LeaseCommanderSquad(
                squad.Squad, objectiveRevision, role, objectivePosition, now);
        }
        foreach (var tank in tanks)
        {
            nextOwned.Add(tank.SquadId);
            OwnedArmorVehicles[tank.SquadId] = tank.Vehicle;
            GroundAiDirector.LeaseCommanderSquad(
                tank.Squad,
                objectiveRevision,
                CommanderRole.SupportByFire,
                objectivePosition,
                now);
            var armorRole = side.ArmorRoles.TryGetValue(tank.Id, out var assignedArmorRole)
                ? assignedArmorRole.ToString()
                : ArmorRole.Reserve.ToString();
            GroundAiDirector.LeaseCommanderVehicle(
                tank.Vehicle, objectiveRevision, armorRole, objectivePosition, now);
        }

        IssueOrders(side, commandedSquads, objectiveId, objectivePosition, objectiveRadius,
            offensiveOperation, mainAxis, flankAxis, antiTankTask,
            gate.AttackAuthorized, gate.SmokeBlocked);
        IssueArmorOrders(side, tanks, objectiveId, objectivePosition, objectiveRadius,
            offensiveOperation, mainAxis, flankAxis, armorAttackAuthorized);
        TaskAircraft(
            battle,
            side,
            aircraft,
            ContactReports,
            objectivePosition,
            objectiveRadius,
            now);

        var roleCounts = side.Roles.Values.GroupBy(role => role)
            .ToDictionary(group => group.Key, group => group.Count());
        var antiTankSummary = antiTankTask is { } assignedAntiTank
            ? $"{assignedAntiTank.Action}:{assignedAntiTank.SquadId}->{assignedAntiTank.TargetId}"
            : "none";
        AiState.Trace(
            $"Commander {side.Name}: objective={objectiveId}, squads={operational.Length}, " +
            $"roles=A{RoleCount(roleCounts, CommanderRole.Assault)}/" +
            $"F{RoleCount(roleCounts, CommanderRole.Flank)}/" +
            $"S{RoleCount(roleCounts, CommanderRole.SupportByFire)}/" +
            $"R{RoleCount(roleCounts, CommanderRole.Reserve)}, " +
            $"ratio={gate.StrengthRatio:0.00}, suppression={gate.AverageSuppression:0.00}, " +
            $"reports={relevantReports.Count}, AT={antiTankSummary}, " +
            $"arriving={squads.Count - commandedSquads.Length}, " +
            $"armor={tanks.Count}, aircraft={aircraft.Count}, " +
            $"attack={gate.AttackAuthorized}, armorAttack={armorAttackAuthorized}, " +
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
        MissionObjective? best = null;
        var bestScore = float.MaxValue;
        var bestId = int.MaxValue;

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

            var securedFaction = objective.IsSecured() ? objective.GetSecuredFaction() : string.Empty;
            var friendlySecured = !string.IsNullOrEmpty(securedFaction) &&
                                  (side.InvaderSide
                                      ? battle.IsInvaderFaction(securedFaction)
                                      : battle.IsDefenderFaction(securedFaction));
            var score = HorizontalDistance(centroid, position) + (friendlySecured ? 140f : 0f);
            foreach (var report in reports)
            {
                if (ReportAgeInvalid(report, now) || !IsFinite(report.LastKnownPosition))
                    continue;
                var distance = HorizontalDistance(report.LastKnownPosition, position);
                if (distance < 160f)
                    score -= Mathf.Clamp01(report.Confidence) * (1f - distance / 160f) * 45f;
            }

            var id = manager.GetObjectiveUniqueId(objective);
            if (score < bestScore - 0.01f || Mathf.Abs(score - bestScore) <= 0.01f && id < bestId)
            {
                best = objective;
                bestScore = score;
                bestId = id;
            }
        }

        return best;
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
        float now)
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
                    report, battle, side.InvaderSide, out var target, out _))
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
        var signature = string.Join(",", tanks
            .OrderBy(tank => tank.Id)
            .Select(tank => $"{tank.Id}:" +
                            $"{Mathf.RoundToInt(tank.HullFraction * 10f)}:" +
                            $"{Mathf.RoundToInt(tank.Suppression * 10f)}:" +
                            $"{Mathf.RoundToInt(tank.EffectivePower * 2f)}")) +
                        $"|m:{mainAxis?.Candidate.Id}:" +
                        $"{Mathf.RoundToInt((mainAxis?.Candidate.TerrainScore ?? 0f) * 10f)}" +
                        $"|f:{flankAxis?.Candidate.Id}:" +
                        $"{Mathf.RoundToInt((flankAxis?.Candidate.TerrainScore ?? 0f) * 10f)}";
        if (side.ArmorSignature == signature &&
            (tanks.Count == 0 || side.ArmorRoles.Count > 0))
        {
            return;
        }

        side.ArmorRoles.Clear();
        side.ArmorSignature = signature;
        if (tanks.Count == 0)
            return;

        var available = new List<TankInfo>();
        foreach (var tank in tanks.OrderBy(tank => tank.Id))
        {
            if (tank.HullFraction < 0.45f || tank.Suppression > 0.65f)
                side.ArmorRoles[tank.Id] = ArmorRole.Reserve;
            else
                available.Add(tank);
        }

        var desiredReserveCount = tanks.Count >= 3
            ? Mathf.CeilToInt(tanks.Count * CommanderPlannerCore.ReserveFraction)
            : 0;
        var additionalReserves = Mathf.Max(0,
            desiredReserveCount - side.ArmorRoles.Count(pair => pair.Value == ArmorRole.Reserve));
        foreach (var tank in available
                     .OrderBy(tank => tank.EffectivePower)
                     .ThenBy(tank => tank.Id)
                     .Take(additionalReserves)
                     .ToArray())
        {
            side.ArmorRoles[tank.Id] = ArmorRole.Reserve;
            available.Remove(tank);
        }

        var mainUsable = mainAxis != null &&
                         mainAxis.Candidate.TerrainScore >= MinimumTankTerrainScore;
        var flankUsable = flankAxis != null &&
                          flankAxis.Candidate.TerrainScore >= MinimumTankTerrainScore;
        if (!mainUsable && !flankUsable)
        {
            foreach (var tank in available)
                side.ArmorRoles[tank.Id] = ArmorRole.Reserve;
            return;
        }

        var committedIndex = 0;
        foreach (var tank in available
                     .OrderByDescending(tank => tank.EffectivePower)
                     .ThenBy(tank => tank.Id))
        {
            var useFlank = flankUsable && (!mainUsable || committedIndex % 2 == 1);
            side.ArmorRoles[tank.Id] = useFlank
                ? ArmorRole.FlankSupport
                : ArmorRole.AssaultSupport;
            committedIndex++;
        }
    }

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
        var loneTankElement = attackElementCount == 1 && operational.Count == 0 && tanks.Count == 1;
        var candidate = offensive && (attackElementCount >= 2 || loneTankElement) &&
                        hasAttackRole && terrainUsable &&
                        ratio >= CommanderPlannerCore.MinimumAttackRatio &&
                        averageSuppression <= CommanderPlannerCore.MaximumAverageSuppression;
        var smokeReady = !smokeRequired || side.SmokeBypassed ||
                         side.SmokeReadyAt >= 0f && now >= side.SmokeReadyAt;
        var smokeBlocked = candidate && smokeRequired && !smokeReady;
        return new AttackGate(candidate && !smokeBlocked, smokeBlocked, ratio, averageSuppression);
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
            var selected = ResourcesManager.GetAvailableSupportArtillery(faction);
            if (selected == null || !SafeArtilleryCrewman(selected, faction))
                return false;

            // A faction-wide request can be accepted by any eligible gun. Fail closed
            // unless every gun which can hear it is proven AI- and mission-safe.
            var alive = Creature.aliveCreatures;
            if (alive == null)
                return false;
            foreach (var creature in alive)
            {
                var crewman = creature as Soldier;
                if (crewman == null || !crewman.IsAlive || !crewman.IsOnVehicle() ||
                    !ResourcesManager.IsSameFaction(crewman.faction, faction))
                {
                    continue;
                }

                var gun = crewman.GetCurrentVehicle();
                if (gun == null || !gun.IsArtillery() ||
                    !RadioManager.IsNearRadio(crewman.transform.position))
                {
                    continue;
                }

                if (!SafeArtilleryCrewman(crewman, faction))
                    return false;
            }

            return true;
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

    internal static bool CommanderAircraftUnsafeImpact(
        VehiclePlane? plane,
        Vector3 impact,
        float radius)
    {
        if (plane == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || !IsFinite(impact))
        {
            return false;
        }

        try
        {
            var aircraftId = plane.GetInstanceID();
            var side = Invaders.AircraftTasks.ContainsKey(aircraftId)
                ? Invaders
                : Defenders.AircraftTasks.ContainsKey(aircraftId)
                    ? Defenders
                    : null;
            var battle = BattleManager.GetCurrentBattleData();
            return side != null && battle != null &&
                   FriendlyNearBattleSide(impact, battle, side.InvaderSide, radius);
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return true;
        }
    }

    private static void TaskAircraft(
        BattleData battle,
        SideState side,
        IReadOnlyList<AircraftInfo> aircraft,
        IReadOnlyList<ContactReport> reports,
        Vector3 objectivePosition,
        float objectiveRadius,
        float now)
    {
        var liveAircraftIds = new HashSet<int>(aircraft.Select(entry => entry.Id));
        foreach (var stale in side.AircraftTasks.Keys
                     .Where(id => !liveAircraftIds.Contains(id))
                     .ToArray())
        {
            CancelAircraftTask(side, stale, side.AircraftTasks[stale]);
        }

        var usedTargets = new HashSet<int>();
        foreach (var plane in aircraft.OrderBy(entry => entry.Id))
        {
            if (side.AircraftTasks.TryGetValue(plane.Id, out var existing))
            {
                if (!AircraftStillCommanderSafe(plane.Ai, plane.Plane))
                {
                    CancelAircraftTask(side, plane.Id, existing);
                    continue;
                }

                if (!GroundAiDirector.LeaseCommanderAircraft(
                        plane.Plane, existing.Position, now))
                {
                    side.AircraftTasks.Remove(plane.Id);
                    continue;
                }

                if (now - existing.AssignedAt < AircraftRetaskSeconds ||
                    !SafeIdleAircraft(plane.Ai, plane.Plane))
                {
                    usedTargets.Add(existing.TargetId);
                    continue;
                }

                side.AircraftTasks.Remove(plane.Id);
            }

            if (!SafeIdleAircraft(plane.Ai, plane.Plane))
                continue;

            ContactReport? selectedReport = null;
            Spottable? selectedTarget = null;
            var selectedPosition = Vector3.zero;
            var bestScore = float.MinValue;
            foreach (var report in reports)
            {
                if (usedTargets.Contains(report.TargetId) ||
                    !AircraftReportEligible(report, now) ||
                    !AircraftReportRelevantToObjective(
                        report, objectivePosition, objectiveRadius) ||
                    !TryResolveReportTarget(
                        report, battle, side.InvaderSide, out var target, out var targetPosition))
                {
                    continue;
                }

                if (report.Kind != ContactKind.Aircraft)
                {
                    var clearance = plane.HasBombs
                        ? Settings.AircraftBombFriendlyRadius.Value
                        : Settings.AircraftFriendlyAttackRadius.Value;
                    if (FriendlyNearBattleSide(
                            targetPosition, battle, side.InvaderSide, clearance))
                    {
                        continue;
                    }
                }

                var score = AircraftReportScore(plane, report, targetPosition, now);
                if (score <= bestScore)
                    continue;
                selectedReport = report;
                selectedTarget = target;
                selectedPosition = targetPosition;
                bestScore = score;
            }

            if (selectedReport == null || selectedTarget == null)
                continue;

            if (!GroundAiDirector.LeaseCommanderAircraft(plane.Plane, selectedPosition, now))
                continue;

            try
            {
                if (!GroundAiDirector.ExecuteCommanderAircraftOrder(
                        plane.Ai, plane.Plane, () => plane.Ai.DoAttackTarget(selectedTarget)))
                {
                    continue;
                }
                if (plane.Ai.targetToAttack == null)
                {
                    GroundAiDirector.ReleaseCommanderAircraft(plane.Id);
                    continue;
                }

                var crew = plane.Ai.squadInside ?? plane.Plane.GetSquadInside();
                var crewSquadId = crew != null ? ContactKnowledge.GetSquadId(crew) : 0;
                side.AircraftTasks[plane.Id] = new AircraftTaskStamp(
                    selectedReport.TargetId, selectedPosition, now,
                    plane.Ai, plane.Plane, crewSquadId);
                usedTargets.Add(selectedReport.TargetId);
                AiState.Trace($"Commander {side.Name}: aircraft {plane.Id} tasked from fresh " +
                              $"{selectedReport.Kind.ToString().ToLowerInvariant()} report");
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // The report target despawned between resolution and tasking.
                GroundAiDirector.ReleaseCommanderAircraft(plane.Id);
            }
        }
    }

    private static bool AircraftReportRelevantToObjective(
        ContactReport report,
        Vector3 objectivePosition,
        float objectiveRadius)
    {
        // Interceptors may meet aircraft away from the ground operation. Bombing
        // and strafing targets must remain part of the current objective fight.
        return report.Kind == ContactKind.Aircraft ||
               IsFinite(report.LastKnownPosition) &&
               HorizontalDistance(report.LastKnownPosition, objectivePosition) <=
               Mathf.Max(10f, objectiveRadius) + 160f;
    }

    private static bool SafeIdleAircraft(AIPlane ai, VehiclePlane plane)
    {
        if (!AircraftStillCommanderSafe(ai, plane))
            return false;

        try
        {
            return ai.planeState == AIPlane.PlaneAIState.flyingAroundArea &&
                   ai.targetToAttack == null && !ai.hasEnemy && !ai.hasEnemyTank;
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool AircraftStillCommanderSafe(AIPlane ai, VehiclePlane plane)
    {
        try
        {
            if (ai == null || plane == null || ai.veh == null ||
                !Settings.CommanderEnabled.Value || !MultiplayerAuthority.CanMutateGameplay() ||
                !ai.HasAIDriver || !ai.isPlaneActive || !plane.IsActive() ||
                !plane.IsOperative() || !plane.CanFight() || plane.life <= 0 ||
                plane.IsDisabled() || !plane.IsLocalAIDriving() || plane.PlayerIsInside() ||
                plane.PlayerIsDriving() || VehicleHasPlayerOccupant(plane) ||
                VehicleHasScriptedOccupant(plane))
            {
                return false;
            }

            var pilot = plane.GetDriver();
            if (pilot == null || !AiOwnership.IsAutonomous(pilot) || !pilot.CanFight())
                return false;
            var sync = pilot.GetComponent<SyncSoldier>();
            if (sync != null && sync.IsControlledByAPlayer())
                return false;

            var crew = ai.squadInside ?? plane.GetSquadInside();
            return crew != null && !HasPlayerMember(crew) && !HasScriptAssignedMember(crew) &&
                   !ScriptLockedSquads.Contains(ContactKnowledge.GetSquadId(crew));
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }
    }

    private static bool AircraftReportEligible(ContactReport report, float now)
    {
        if (report.TargetId == 0 || string.IsNullOrEmpty(report.TargetFaction) ||
            !IsFinite(report.LastKnownPosition) ||
            !float.IsFinite(report.Confidence))
        {
            return false;
        }

        var age = now - report.ObservedAt;
        var maximumAge = report.Kind == ContactKind.Aircraft ? 8f : 12f;
        var minimumConfidence = report.Kind switch
        {
            ContactKind.Aircraft => 0.30f,
            ContactKind.GroundVehicle => 0.35f,
            _ => 0.55f
        };
        return float.IsFinite(age) && age >= 0f && age <= maximumAge &&
               report.Confidence >= minimumConfidence;
    }

    private static float AircraftReportScore(
        AircraftInfo plane,
        ContactReport report,
        Vector3 targetPosition,
        float now)
    {
        var typeScore = report.Kind switch
        {
            ContactKind.Aircraft => plane.HasBombs ? 2.6f : 4f,
            ContactKind.GroundVehicle => plane.HasBombs ? 4f : 2.4f,
            _ => plane.HasBombs ? 2.2f : 1.2f
        };
        return typeScore + Mathf.Clamp01(report.Confidence) * 2f -
               Mathf.Max(0f, now - report.ObservedAt) * 0.08f -
               HorizontalDistance(plane.Position, targetPosition) / 3000f;
    }

    private static bool TryResolveReportTarget(
        ContactReport report,
        BattleData battle,
        bool invaderSide,
        out Spottable target,
        out Vector3 targetPosition)
    {
        target = null!;
        targetPosition = Vector3.zero;
        if (string.IsNullOrEmpty(report.TargetFaction) ||
            FactionBelongsToSide(battle, invaderSide, report.TargetFaction))
            return false;
        try
        {
            if (report.Kind == ContactKind.Infantry)
            {
                var alive = Creature.aliveCreatures;
                if (alive == null)
                    return false;
                foreach (var creature in alive)
                {
                    var soldier = creature as Soldier;
                    if (soldier == null || !soldier.IsAlive || !soldier.CanFight() ||
                        string.IsNullOrEmpty(soldier.faction) ||
                        soldier.GetInstanceID() != report.TargetId ||
                        !ResourcesManager.IsSameFaction(soldier.faction, report.TargetFaction) ||
                        !FactionBelongsToSide(battle, !invaderSide, soldier.faction))
                    {
                        continue;
                    }

                    var position = soldier.GetCenterOfUnit();
                    if (!TargetNearReport(report, position))
                        return false;
                    target = soldier.Cast<Spottable>();
                    targetPosition = position;
                    return true;
                }

                return false;
            }

            var vehicles = Vehicle.allVehicles;
            if (vehicles == null)
                return false;
            foreach (var vehicle in vehicles)
            {
                if (vehicle == null || vehicle.GetInstanceID() != report.TargetId ||
                    !vehicle.IsActive() || !vehicle.IsOperative() || vehicle.life <= 0 ||
                    string.IsNullOrEmpty(vehicle.GetVehicleFaction()) ||
                    !ResourcesManager.IsSameFaction(
                        vehicle.GetVehicleFaction(), report.TargetFaction) ||
                    !FactionBelongsToSide(battle, !invaderSide, vehicle.GetVehicleFaction()))
                {
                    continue;
                }

                var isAircraft = vehicle.GetComponent<VehiclePlane>() != null;
                if (isAircraft != (report.Kind == ContactKind.Aircraft))
                    return false;
                var position = vehicle.GetCenterOfUnit();
                if (!TargetNearReport(report, position))
                    return false;
                target = vehicle.Cast<Spottable>();
                targetPosition = position;
                return true;
            }
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
            return false;
        }

        return false;
    }

    private static bool TargetNearReport(ContactReport report, Vector3 currentPosition)
    {
        if (!IsFinite(currentPosition))
            return false;
        var maximumDrift = report.Kind switch
        {
            ContactKind.Aircraft => 650f,
            ContactKind.GroundVehicle => 90f,
            _ => 45f
        };
        return HorizontalDistance(currentPosition, report.LastKnownPosition) <= maximumDrift;
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
        bool smokeBlocked)
    {
        if (!offensive)
        {
            IssueDefensiveOrders(side, squads, objectiveId, objective, objectiveRadius);
            return;
        }

        var fallbackDirection = mainAxis?.Direction ?? Flatten(AveragePosition(squads) - objective).normalized;
        if (fallbackDirection.sqrMagnitude < 0.01f)
            fallbackDirection = Vector3.back;

        var supportPosition = FindSupportByFirePosition(objective, objectiveRadius,
            fallbackDirection, squads);
        var reservePosition = GroundPoint(
            objective + fallbackDirection * Mathf.Max(objectiveRadius + 90f, 125f),
            objective.y,
            out _,
            out _);

        foreach (var squad in squads.OrderBy(entry => entry.Id))
        {
            if (antiTankTask is { } task && task.SquadId == squad.Id)
            {
                IssueAntiTankOrder(squad, task, squads);
                continue;
            }

            if (!side.Roles.TryGetValue(squad.Id, out var role))
            {
                var strengthBroken = squad.Snapshot.EffectiveStrength /
                                     Mathf.Max(1f, squad.Snapshot.PeakStrength) <
                                     CommanderPlannerCore.MinimumSquadStrengthFraction;
                var recoveryPosition = strengthBroken ? reservePosition : squad.Position;
                IssueHold(squad, objectiveId, CommanderRole.Reserve, CommanderAction.Hold,
                    recoveryPosition, 24f, objective);
                continue;
            }

            var axis = role == CommanderRole.Flank ? flankAxis : mainAxis;
            switch (role)
            {
                case CommanderRole.SupportByFire:
                    IssueHold(squad, objectiveId, role, CommanderAction.Hold,
                        supportPosition, 18f, objective, requireLineOfSight: true);
                    break;
                case CommanderRole.Reserve:
                    IssueHold(squad, objectiveId, role, CommanderAction.Hold,
                        reservePosition, 25f, objective);
                    break;
                case CommanderRole.Assault:
                case CommanderRole.Flank:
                {
                    if (axis == null)
                    {
                        IssueHold(squad, objectiveId, role, CommanderAction.Hold,
                            squad.Position, 20f, objective);
                        break;
                    }

                    if (attackAuthorized)
                    {
                        var attackDirection = Flatten(objective - axis.StagingPosition);
                        IssueAttack(squad, objectiveId, role, attackDirection,
                            objective, objectiveRadius);
                    }
                    else
                    {
                        IssueHold(squad, objectiveId, role,
                            smokeBlocked ? CommanderAction.Prepare : CommanderAction.Hold,
                            axis.StagingPosition, 18f, objective);
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
        float objectiveRadius)
    {
        // This area order is only the incoming route into the defended position.
        // Once a soldier arrives, GroundAiDirector assigns one useful cover slot (or
        // holds the current position when none exists) and owns locomotion there.
        // Keeping one stable order stamp prevents native area-order refreshes from
        // turning the objective into a continuous patrol zone.
        var holdRadius = Mathf.Max(20f, objectiveRadius + DefensiveHoldMarginMeters);
        foreach (var squad in squads.OrderBy(entry => entry.Id))
        {
            var role = side.Roles.TryGetValue(squad.Id, out var assignedRole)
                ? assignedRole
                : CommanderRole.Reserve;

            IssueDefensiveAreaHold(squad, objectiveId, role, objective, holdRadius);
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
        // This authoritative area is refreshed as lease metadata even when the
        // native HoldArea write is intentionally suppressed as an identical order.
        GroundAiDirector.RegisterCommanderDefensiveArea(squad.Squad, center, radius);
        var stamp = new OrderStamp(
            objectiveId, role, CommanderAction.Hold, center, Vector3.zero, radius);
        StableDefensiveOrder? existingOrder = null;
        if (LastOrders.TryGetValue(squad.Id, out var existing) &&
            existing.Action == CommanderAction.Hold)
        {
            existingOrder = new StableDefensiveOrder(
                existing.ObjectiveId,
                ToMapPoint(existing.Destination),
                existing.Radius);
        }

        var proposedOrder = new StableDefensiveOrder(
            objectiveId, ToMapPoint(center), radius);
        if (!DefensiveOrderStabilityCore.ShouldReplace(existingOrder, proposedOrder))
            return;

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
                IssueArmorHold(tank, objectiveId, ArmorRole.Reserve, reserve, 24f);
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
                IssueArmorHold(tank, objectiveId, ArmorRole.Reserve, reserve, 24f);
                continue;
            }

            if (!offensive)
            {
                var defensive = objective + axis.Direction * Mathf.Max(30f, objectiveRadius * 0.8f) + lateral;
                IssueArmorHold(tank, objectiveId, role, defensive, 22f);
            }
            else if (attackAuthorized)
            {
                IssueArmorAttack(tank, objectiveId, role,
                    Flatten(objective - groundedLane), objective, objectiveRadius);
            }
            else
            {
                var staging = groundedLane + axis.Direction * 25f;
                IssueArmorHold(tank, objectiveId, role, staging, 20f);
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
        position = GroundPoint(position, tank.Position.y, out _, out _);
        var stamp = new OrderStamp(objectiveId, ToCommanderRole(role), CommanderAction.Hold,
            position, Vector3.zero, radius);
        if (OrderMatches(tank.Squad, tank.SquadId, stamp))
            return;

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
        direction = Flatten(direction).normalized;
        if (direction.sqrMagnitude < 0.01f)
            return;

        var stamp = new OrderStamp(objectiveId, ToCommanderRole(role), CommanderAction.Attack,
            objective, direction, radius);
        if (OrderMatches(tank.Squad, tank.SquadId, stamp))
            return;

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

    private static void IssueHold(
        SquadInfo squad,
        int objectiveId,
        CommanderRole role,
        CommanderAction action,
        Vector3 position,
        float radius,
        Vector3 threat,
        bool requireLineOfSight = false,
        float minimumThreatDistance = 0f)
    {
        position = GroundPoint(position, squad.Position.y, out _, out _);
        position = FindCommanderCoverPosition(
            squad, position, threat, radius, requireLineOfSight, minimumThreatDistance);
        var stamp = new OrderStamp(objectiveId, role, action, position, Vector3.zero, radius);
        if (OrderMatches(squad.Squad, squad.Id, stamp))
            return;

        if (GroundAiDirector.ExecuteCommanderSquadOrder(
                squad.Squad, () => squad.Squad.HoldArea(position, radius, false)))
        {
            LastOrders[squad.Id] = stamp;
        }
    }

    private static Vector3 FindCommanderCoverPosition(
        SquadInfo squad,
        Vector3 requested,
        Vector3 threat,
        float orderRadius,
        bool requireLineOfSight,
        float minimumThreatDistance)
    {
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
        direction = Flatten(direction).normalized;
        if (direction.sqrMagnitude < 0.01f)
            return;

        var stamp = new OrderStamp(
            objectiveId, role, CommanderAction.Attack, objective, direction, radius);
        if (OrderMatches(squad.Squad, squad.Id, stamp))
            return;

        // The game's public method synchronizes the order and constructs the flank waypoint
        // from the squad's travel direction toward the objective.
        if (GroundAiDirector.ExecuteCommanderSquadOrder(
                squad.Squad, () => squad.Squad.AttackFromSide(direction, objective, radius)))
        {
            LastOrders[squad.Id] = stamp;
        }
    }

    private static bool OrderMatches(Squad squad, int squadId, OrderStamp proposed)
    {
        var expected = proposed.Action == CommanderAction.Attack
            ? Order.attackFromSide
            : Order.defend;
        if (squad.order != expected ||
            HorizontalDistance(squad.moveOrderPosition, proposed.Destination) > 10f ||
            Mathf.Abs(squad.moveOrderRadius - proposed.Radius) > 4f)
        {
            return false;
        }

        if (!LastOrders.TryGetValue(squadId, out var existing) ||
            existing.ObjectiveId != proposed.ObjectiveId || existing.Role != proposed.Role ||
            existing.Action != proposed.Action ||
            HorizontalDistance(existing.Destination, proposed.Destination) > 10f ||
            Mathf.Abs(existing.Radius - proposed.Radius) > 4f)
        {
            return false;
        }

        if (proposed.Action != CommanderAction.Attack)
            return true;

        return Vector3.Angle(existing.Direction, proposed.Direction) < 10f;
    }

    private static Vector3 FindSupportByFirePosition(
        Vector3 objective,
        float objectiveRadius,
        Vector3 mainDirection,
        IReadOnlyList<SquadInfo> squads)
    {
        var distance = Mathf.Max(objectiveRadius + 50f, 80f);
        var offsets = new[] { -20f, 20f, -35f, 35f, 0f };
        Vector3? fallback = null;
        foreach (var offset in offsets)
        {
            var direction = Quaternion.Euler(0f, offset, 0f) * mainDirection;
            var candidate = GroundPoint(objective + direction * distance, objective.y,
                out _, out _);
            fallback ??= candidate;
            if (HasLineOfSight(candidate, objective) &&
                squads.Count(squad => HorizontalDistance(squad.Position, candidate) < 30f) <= 1)
            {
                return candidate;
            }
        }

        return fallback ?? objective + mainDirection * distance;
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
               hit.distance >= distance - 8f;
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

    private static int RoleCount(IReadOnlyDictionary<CommanderRole, int> counts, CommanderRole role)
        => counts.TryGetValue(role, out var count) ? count : 0;

    private static int SmokeRolePriority(CommanderRole role) => role switch
    {
        CommanderRole.AntiTank => 0,
        CommanderRole.SupportByFire => 1,
        CommanderRole.Reserve => 2,
        CommanderRole.Assault => 3,
        CommanderRole.Flank => 4,
        _ => 5
    };

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var segment = Flatten(end - start);
        var lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.001f)
            return HorizontalDistance(point, start);

        var fromStart = Flatten(point - start);
        var t = Mathf.Clamp01(Vector3.Dot(fromStart, segment) / lengthSquared);
        return HorizontalDistance(point, start + segment * t);
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
        => Flatten(first - second).magnitude;

    private static Vector3 Flatten(Vector3 value) => new(value.x, 0f, value.z);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static MapPoint ToMapPoint(Vector3 value) => new(value.x, value.z);

    private sealed class SquadInfo
    {
        internal readonly int Id;
        internal readonly Squad Squad;
        internal readonly string Faction;
        internal readonly Vector3 Position;
        internal readonly bool HasAntiTank;
        internal readonly CommanderSquadSnapshot Snapshot;

        internal SquadInfo(
            int id,
            Squad squad,
            string faction,
            Vector3 position,
            bool hasAntiTank,
            CommanderSquadSnapshot snapshot)
        {
            Id = id;
            Squad = squad;
            Faction = faction;
            Position = position;
            HasAntiTank = hasAntiTank;
            Snapshot = snapshot;
        }
    }

    private sealed class TankInfo
    {
        internal readonly int Id;
        internal readonly int SquadId;
        internal readonly Squad Squad;
        internal readonly Vehicle Vehicle;
        internal readonly string Faction;
        internal readonly Vector3 Position;
        internal readonly float HullFraction;
        internal readonly float Suppression;
        internal readonly float EffectivePower;

        internal TankInfo(
            int id,
            int squadId,
            Squad squad,
            Vehicle vehicle,
            string faction,
            Vector3 position,
            float hullFraction,
            float suppression,
            float effectivePower)
        {
            Id = id;
            SquadId = squadId;
            Squad = squad;
            Vehicle = vehicle;
            Faction = faction;
            Position = position;
            HullFraction = hullFraction;
            Suppression = suppression;
            EffectivePower = effectivePower;
        }
    }

    private sealed class AircraftInfo
    {
        internal readonly int Id;
        internal readonly AIPlane Ai;
        internal readonly VehiclePlane Plane;
        internal readonly string Faction;
        internal readonly Vector3 Position;
        internal readonly bool HasBombs;

        internal AircraftInfo(
            int id,
            AIPlane ai,
            VehiclePlane plane,
            string faction,
            Vector3 position,
            bool hasBombs)
        {
            Id = id;
            Ai = ai;
            Plane = plane;
            Faction = faction;
            Position = position;
            HasBombs = hasBombs;
        }
    }

    private enum ArmorRole
    {
        AssaultSupport,
        FlankSupport,
        Reserve
    }

    private sealed class AircraftTaskStamp
    {
        internal readonly int TargetId;
        internal readonly Vector3 Position;
        internal readonly float AssignedAt;
        internal readonly AIPlane Ai;
        internal readonly VehiclePlane Plane;
        internal readonly int CrewSquadId;

        internal AircraftTaskStamp(
            int targetId,
            Vector3 position,
            float assignedAt,
            AIPlane ai,
            VehiclePlane plane,
            int crewSquadId)
        {
            TargetId = targetId;
            Position = position;
            AssignedAt = assignedAt;
            Ai = ai;
            Plane = plane;
            CrewSquadId = crewSquadId;
        }
    }

    private sealed class SupportRequestStamp
    {
        internal readonly int SquadId;
        internal readonly Squad Squad;
        internal readonly string Faction;
        internal readonly RadioRequest Request;
        internal readonly Vector3 Target;
        internal readonly float IssuedAt;
        internal float ConfirmedAt = -1f;

        internal SupportRequestStamp(
            int squadId,
            Squad squad,
            string faction,
            RadioRequest request,
            Vector3 target,
            float issuedAt)
        {
            SquadId = squadId;
            Squad = squad;
            Faction = faction;
            Request = request;
            Target = target;
            IssuedAt = issuedAt;
        }
    }

    private sealed class AxisRuntime
    {
        internal readonly CommanderAxisCandidate Candidate;
        internal readonly Vector3 StagingPosition;
        internal readonly Vector3 Direction;

        internal AxisRuntime(
            CommanderAxisCandidate candidate,
            Vector3 stagingPosition,
            Vector3 direction)
        {
            Candidate = candidate;
            StagingPosition = stagingPosition;
            Direction = direction;
        }
    }

    private sealed class SideState
    {
        internal readonly bool InvaderSide;
        internal readonly Dictionary<int, CommanderRole> Roles = new();
        internal readonly Dictionary<int, ArmorRole> ArmorRoles = new();
        internal readonly Dictionary<int, AircraftTaskStamp> AircraftTasks = new();
        internal SupportRequestStamp? ActiveSupport;
        internal int ObjectiveId = int.MinValue;
        internal bool Offensive;
        internal string OperationalSignature = string.Empty;
        internal string ArmorSignature = string.Empty;
        internal int? MainAxisId;
        internal int? FlankAxisId;
        internal bool AttackLaunched;
        internal float SmokeBlockedAt = -1f;
        internal float SmokeReadyAt = -1f;
        internal bool SmokeBypassed;
        internal float NextSmokeAllowedAt;
        internal float ArtilleryBlockedAt = -1f;
        internal float ArtilleryReadyAt = -1f;
        internal bool ArtilleryBypassed;
        internal float NextArtilleryAllowedAt;

        internal string Name => InvaderSide ? "invaders" : "defenders";

        internal SideState(bool invaderSide)
        {
            InvaderSide = invaderSide;
        }

        internal void BeginOperation(int objectiveId, bool offensive, float now)
        {
            Roles.Clear();
            ArmorRoles.Clear();
            ObjectiveId = objectiveId;
            Offensive = offensive;
            OperationalSignature = string.Empty;
            ArmorSignature = string.Empty;
            MainAxisId = null;
            FlankAxisId = null;
            AttackLaunched = false;
            SmokeBlockedAt = -1f;
            SmokeReadyAt = -1f;
            SmokeBypassed = false;
            ArtilleryBlockedAt = -1f;
            ArtilleryReadyAt = -1f;
            ArtilleryBypassed = false;
        }

        internal void ResetOperation(bool hard = false)
        {
            Roles.Clear();
            ArmorRoles.Clear();
            ObjectiveId = int.MinValue;
            Offensive = false;
            OperationalSignature = string.Empty;
            ArmorSignature = string.Empty;
            MainAxisId = null;
            FlankAxisId = null;
            AttackLaunched = false;
            SmokeBlockedAt = -1f;
            SmokeReadyAt = -1f;
            SmokeBypassed = false;
            ArtilleryBlockedAt = -1f;
            ArtilleryReadyAt = -1f;
            ArtilleryBypassed = false;
            if (hard)
            {
                AircraftTasks.Clear();
                ActiveSupport = null;
                NextSmokeAllowedAt = 0f;
                NextArtilleryAllowedAt = 0f;
            }
        }

        internal void RemoveSquad(int id)
        {
            if (!Roles.Remove(id))
                return;
            OperationalSignature = string.Empty;
        }
    }

    private readonly record struct OrderStamp(
        int ObjectiveId,
        CommanderRole Role,
        CommanderAction Action,
        Vector3 Destination,
        Vector3 Direction,
        float Radius);

    private readonly record struct AttackGate(
        bool AttackAuthorized,
        bool SmokeBlocked,
        float StrengthRatio,
        float AverageSuppression);
}

[HarmonyPatch(typeof(BattleManager), "Update")]
internal static class CommanderBattleUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(BattleManager __instance)
    {
        GroundAiDirector.UpdateBattle(__instance, Time.time);
    }
}

[HarmonyPatch(typeof(BattleManager), "Start")]
internal static class CommanderBattleStartPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        CommanderMvp.ResetBattle();
    }
}

[HarmonyPatch(typeof(BattleManager), "OnPhaseChange")]
internal static class CommanderPhaseChangePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        CommanderMvp.ResetPhase();
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.ConfirmRadioRequest))]
internal static class CommanderRadioConfirmationPatch
{
    [HarmonyPostfix]
    private static void Postfix(Squad __instance)
    {
        CommanderMvp.OnRadioRequestConfirmed(__instance, Time.time);
    }
}

[HarmonyPatch(typeof(ResourcesManager), nameof(ResourcesManager.GetAvailableSupportArtillery),
    typeof(string))]
internal static class CommanderArtillerySelectionPatch
{
    [HarmonyPostfix]
    private static void Postfix(string faction, ref Soldier __result)
    {
        __result = CommanderMvp.ValidateCommanderArtillerySelection(faction, __result)!;
    }
}

[HarmonyPatch(typeof(Squad), "Leader_OrderAttackCurrentTask")]
internal static class CommanderNativeObjectiveOrderPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance)
    {
        return !CommanderMvp.OwnsSquad(__instance);
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.OrderLeaveAllVehiclesAndCovers),
    new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
internal static class CommanderNativeLeaveOrderPatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        Squad __instance,
        bool exitForced,
        bool leaveOnlyIfNotInsideTaskArea,
        bool cancelWaypoints)
    {
        // Suppress only SquadLeaderRoutine's automatic post-objective cleanup order.
        return exitForced || !leaveOnlyIfNotInsideTaskArea || cancelWaypoints ||
               !CommanderMvp.OwnsSquad(__instance);
    }
}

[HarmonyPatch]
internal static class CommanderLuaOrderOwnershipPatch
{
    private static readonly string[] OrderMethods =
    {
        "moveTo",
        "coverArea",
        "attackFromPoint",
        "setClosestObjective",
        "setRandomObjective",
        "charge",
        "followLeader",
        "boardVehicle",
        "leaveVehicle",
        "repairVehicle",
        "holdFire",
        "fireAtWill",
        "alertEnemies",
        "cancelRadioRequest"
    };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var methodName in OrderMethods)
        {
            var method = AccessTools.Method(typeof(Lua_Squad), methodName);
            if (method != null)
                yield return method;
        }
    }

    [HarmonyPrefix]
    private static void Prefix(Lua_Squad __instance)
    {
        if (__instance != null)
            CommanderMvp.MarkMissionScripted(__instance.connectedSquad);
    }
}

[HarmonyPatch]
internal static class CommanderLuaSoldierOwnershipPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var type = typeof(Lua_Soldier);
        var methods = new[]
        {
            AccessTools.Method(type, "moveTo", new[] { typeof(Vector3) }),
            AccessTools.Method(type, "stop", Type.EmptyTypes),
            AccessTools.Method(type, "findCover", new[] { typeof(Vector3), typeof(float) }),
            AccessTools.Method(type, "boardVehicle", new[] { typeof(Lua_Vehicle) }),
            AccessTools.Method(type, "leaveVehicle", Type.EmptyTypes),
            AccessTools.Method(type, "setInVehicle", new[] { typeof(Lua_Vehicle) })
        };
        foreach (var method in methods)
        {
            if (method != null)
                yield return method;
        }
    }

    [HarmonyPrefix]
    private static void Prefix(Lua_Soldier __instance)
    {
        var soldier = __instance?.connectedSoldier;
        CommanderMvp.MarkMissionScripted(soldier?.joinedSquad);
    }
}
