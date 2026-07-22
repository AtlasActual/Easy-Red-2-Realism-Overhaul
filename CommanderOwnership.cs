using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class CommanderMvp
{
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

        // Runs every frame, independent of the planning cadence below, so a tick's
        // queued order executions drain over the following frames instead of all
        // landing synchronously on the tick frame.
        DrainPendingOrderExecutions(Invaders);
        DrainPendingOrderExecutions(Defenders);

        // The planning tick is split across two frames: the interop-heavy asset
        // collection/description runs on the tick frame, and the planning pipeline
        // consumes it on the next frame. At an 8s per-side cadence, one frame of
        // data staleness is irrelevant, but the split halves the tick's worst
        // single-frame cost (measured at ~105ms late-battle when both halves ran
        // together).
        if (_collectedPlanReady)
        {
            _collectedPlanReady = false;
            ExecuteCollectedPlan(manager, now);
            return;
        }

        if (now < _nextPlanAt)
            return;

        _nextPlanAt = now + SidePlanningStaggerSeconds;
        var planInvaderThisTick = _planInvaderNext;
        _planInvaderNext = !_planInvaderNext;

        _updating = true;
        try
        {
            var battle = BattleManager.GetCurrentBattleData();
            if (battle == null)
            {
                ReleaseOwnership();
                return;
            }

            CollectPlanningAssets(battle);
            _collectedPlanInvader = planInvaderThisTick;
            _collectedPlanReady = true;
        }
        catch (Exception ex)
        {
            _collectedPlanReady = false;
            ReleaseOwnership();
            Plugin.LogSource.LogWarning($"Commander collection failed closed: {ex.Message}");
        }
        finally
        {
            _updating = false;
        }
    }

    private static void CollectPlanningAssets(BattleData battle)
    {
        CollectCommandSquads(KnownSquads);
        var invaderSquads = _collectedInvaderSquads;
        var defenderSquads = _collectedDefenderSquads;
        var invaderTanks = _collectedInvaderTanks;
        var defenderTanks = _collectedDefenderTanks;
        var invaderAircraft = _collectedInvaderAircraft;
        var defenderAircraft = _collectedDefenderAircraft;
        invaderSquads.Clear();
        defenderSquads.Clear();
        invaderTanks.Clear();
        defenderTanks.Clear();
        invaderAircraft.Clear();
        defenderAircraft.Clear();
        var uniqueSquads = new HashSet<int>();
        var uniqueVehicles = new HashSet<int>();

        foreach (var squad in KnownSquads)
        {
            if (squad == null)
                continue;

            if (squad.IsVehicleCrew)
            {
                if (!TryDescribeCombatVehicleSquad(squad, out var tank) ||
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
    }

    private static void ExecuteCollectedPlan(BattleManager manager, float now)
    {
        var planInvaderThisTick = _collectedPlanInvader;
        _updating = true;
        try
        {
            var battle = BattleManager.GetCurrentBattleData();
            if (battle == null)
            {
                ReleaseOwnership();
                return;
            }

            var idleSide = planInvaderThisTick ? Defenders : Invaders;
            GroundAiDirector.BeginCommanderPlanning(idleSide.OwnedSquadIds);

            // Only the side whose turn it is runs PlanSide this tick (P1 stagger);
            // the other side keeps the ownership set from its own last planning
            // tick until it plans again. The per-side pipeline below is otherwise
            // unchanged from before the stagger.
            var planningSide = planInvaderThisTick ? Invaders : Defenders;
            LastPlanFrame = Time.frameCount;
            LastPlanSideName = planningSide.Name;
            var nextOwned = planningSide.OwnedSquadIds;
            foreach (var id in nextOwned)
                OwnedArmorVehicles.Remove(id);
            nextOwned.Clear();

            if (planInvaderThisTick)
            {
                PlanSide(manager, battle, Invaders, _collectedInvaderSquads,
                    _collectedInvaderTanks, _collectedInvaderAircraft, now, nextOwned);
            }
            else
            {
                PlanSide(manager, battle, Defenders, _collectedDefenderSquads,
                    _collectedDefenderTanks, _collectedDefenderAircraft, now, nextOwned);
            }

            OwnedSquads.Clear();
            OwnedSquads.UnionWith(Invaders.OwnedSquadIds);
            OwnedSquads.UnionWith(Defenders.OwnedSquadIds);

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

    private static void DrainPendingOrderExecutions(SideState side)
    {
        var budget = OrderExecutionBudgetPerSideFrame;
        while (budget-- > 0 && side.PendingOrderExecutions.Count > 0)
        {
            var execute = side.PendingOrderExecutions.Dequeue();
            RecordOrderExecution();
            try
            {
                execute();
            }
            catch (NullReferenceException ex)
            {
                Plugin.LogSource.LogWarning(
                    $"Commander queued order execution failed: {ex.Message}");
            }
            catch (Il2CppInterop.Runtime.Il2CppException ex)
            {
                Plugin.LogSource.LogWarning(
                    $"Commander queued order execution failed: {ex.Message}");
            }
            catch (Il2CppInterop.Runtime.ObjectCollectedException)
            {
                // The squad or tank despawned between enqueue and drain.
            }
        }
    }

    private static void RecordOrderExecution()
    {
        var frame = Time.frameCount;
        if (LastOrderExecutionFrame != frame)
            LastOrderExecutionCount = 0;
        LastOrderExecutionFrame = frame;
        LastOrderExecutionCount++;
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
        Invaders.OwnedSquadIds.Clear();
        Defenders.OwnedSquadIds.Clear();
        OwnedArmorVehicles.Clear();
        LastOrders.Clear();
        LastArtilleryGunByFaction.Clear();
        Invaders.ResetOperation();
        Defenders.ResetOperation();
        _nextPlanAt = 0f;
        _collectedPlanReady = false;
        _planInvaderNext = true;
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
        Invaders.OwnedSquadIds.Clear();
        Defenders.OwnedSquadIds.Clear();
        GroundAiDirector.ReleaseAllCommanderLeases();
        OwnedArmorVehicles.Clear();
        LastOrders.Clear();
        LastArtilleryGunByFaction.Clear();
        Invaders.ResetOperation();
        Defenders.ResetOperation();
        _nextPlanAt = 0f;
        _collectedPlanReady = false;
        _planInvaderNext = true;
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
        // Keep both sides' persisted ownership sets in sync so the next
        // union-rebuild in Update() cannot resurrect a squad released here
        // (RefreshOwnedSquads runs every 0.5s, independent of which side's
        // turn it is to plan).
        Invaders.OwnedSquadIds.Remove(id);
        Defenders.OwnedSquadIds.Remove(id);
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

    private static bool TryDescribeCombatVehicleSquad(Squad squad, out TankInfo info)
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
                if (occupied != null && IsCommanderCombatVehicle(occupied))
                {
                    vehicle = occupied;
                    break;
                }
            }

            if (vehicle == null || !ArmorStillCommanderSafe(squad, vehicle) ||
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
                (vehicle.GetComponent<VehicleTank>() != null ? 8f : 2.5f) * hullFraction *
                Mathf.Lerp(1f, 0.55f, suppression));
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

    private static bool IsCommanderCombatVehicle(Vehicle vehicle)
        => vehicle.GetComponent<VehicleTank>() != null ||
           !vehicle.IsStatic() && !vehicle.IsTransportVehicle() &&
           !vehicle.IsArtillery() && !vehicle.IsAA();

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

    internal static bool AllowsAutonomousAircraftTargeting(AIPlane? ai, VehiclePlane? plane)
    {
        if (ai == null || plane == null || AircraftHasCommanderOrder(plane))
            return false;

        return AircraftControlSafe(ai, plane);
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
}
