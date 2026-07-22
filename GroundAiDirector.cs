using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// The only coordinator allowed to grant ownership of ground-AI command channels.
/// Feature modules remain sensors/policies; native mutations are reached through
/// this director or the soldier executor selected here.
/// </summary>
internal static class GroundAiDirector
{
    private const string CommanderOwner = "ground-director/commander";
    private const string StaticWeaponOwner = "ground-director/static-weapon";
    private const string AircraftAutonomyOwner = "ground-director/aircraft-autonomy";
    private const string AircraftSafetyOwner = "ground-director/aircraft-safety";
    private const string PlayerOwner = "external/player";
    private const string ScriptOwner = "external/script";
    private const float CommanderLeaseSeconds = 20f;

    private static readonly CommandLeaseRegistryCore Leases = new();
    private static readonly Dictionary<string, FactionPostureState> FactionPostures =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, SoldierTacticalResolution> SoldierResolutions = new();
    private static readonly List<TacticalProposal> TacticalProposalBuffer = new(12);
    private static readonly HashSet<int> CommanderSquads = new();
    private static readonly HashSet<int> CommanderSquadsSeen = new();
    private static readonly Dictionary<int, CommanderDefensiveArea> CommanderDefensiveAreas = new();
    private static readonly Dictionary<int, string> EntityFactions = new();
    private static int _objectiveRevision;
    private static bool _planning;

    internal static void UpdateBattle(BattleManager manager, float now)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
        {
            ClearRuntimeState();
            return;
        }

        CommanderMvp.Update(manager, now);
    }

    internal static void BeginCommanderPlanning(IEnumerable<int>? protectedSquadIds = null)
    {
        _planning = true;
        CommanderSquadsSeen.Clear();
        // The commander now plans one side per tick (staggered every
        // SidePlanningStaggerSeconds). The idle side's squads are not re-leased
        // this tick, so seed them as already "seen" or CompleteCommanderPlanning
        // below would treat them as stale and release their command leases.
        if (protectedSquadIds != null)
        {
            foreach (var id in protectedSquadIds)
                CommanderSquadsSeen.Add(id);
        }
    }

    internal static void CompleteCommanderPlanning()
    {
        if (!_planning)
            return;

        foreach (var stale in CommanderSquads.Where(id => !CommanderSquadsSeen.Contains(id)).ToArray())
        {
            Leases.Release(CommandChannel.SquadOrders, stale, CommanderOwner);
            CommanderSquads.Remove(stale);
            CommanderDefensiveAreas.Remove(stale);
        }

        _planning = false;
        CommanderSquadsSeen.Clear();
    }

    internal static void AbortCommanderPlanning()
    {
        ReleaseAllCommanderLeases();
        _planning = false;
        CommanderSquadsSeen.Clear();
    }

    internal static int RecordPosture(
        string? faction,
        int objectiveId,
        StrategicPosture posture,
        float now)
    {
        if (string.IsNullOrWhiteSpace(faction))
            return 0;

        if (!FactionPostures.TryGetValue(faction, out var previous) ||
            previous.ObjectiveId != objectiveId || previous.Posture != posture)
        {
            var revision = ++_objectiveRevision;
            FactionPostures[faction] = new FactionPostureState(objectiveId, posture, revision, now);
            ReleaseFactionAssignments(faction, revision);
            StaticAntiTankStaffing.OnPostureOrObjectiveChanged(faction, revision);
            AiState.Trace($"Ground director: {faction} -> {posture} objective={objectiveId} revision={revision}");
            return revision;
        }

        FactionPostures[faction] = previous with { LastObservedAt = now };
        return previous.Revision;
    }

    internal static int CurrentObjectiveRevision(string? faction)
        => !string.IsNullOrWhiteSpace(faction) && FactionPostures.TryGetValue(faction, out var state)
            ? state.Revision
            : 0;

    internal static bool IsAttackingFaction(string? faction)
    {
        if (string.IsNullOrWhiteSpace(faction))
            return false;
        if (FactionPostures.TryGetValue(faction, out var state))
            return state.Posture == StrategicPosture.Attack;

        // Before the first commander snapshot, preserve the game's traditional
        // invader posture. The first objective inventory replaces this fallback.
        var battle = BattleManager.GetCurrentBattleData();
        return battle != null && battle.IsInvaderFaction(faction);
    }

    internal static bool IsDefendingSquad(Squad? squad)
    {
        var leader = squad?.Leader;
        return leader != null && !IsAttackingFaction(leader.faction) &&
               FactionPostures.ContainsKey(leader.faction ?? string.Empty);
    }

    internal static void LeaseCommanderSquad(
        Squad? squad,
        int objectiveRevision,
        CommanderRole role,
        Vector3 destination,
        float now)
    {
        if (squad == null)
            return;

        ObserveExternalOwnership(squad);
        var id = ContactKnowledge.GetSquadId(squad);
        CommanderSquadsSeen.Add(id);
        if (HasExternalSquadOwnership(id, now))
            return;

        var faction = squad.Leader?.faction ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(faction))
            EntityFactions[id] = faction;
        var request = new CommandLeaseRequest(
            CommandChannel.SquadOrders,
            id,
            CommanderOwner,
            CommandAuthority.CommanderIntent,
            Math.Max(0, objectiveRevision),
            role.ToString(),
            new MapPoint(destination.x, destination.z),
            "host-only;objective-bound",
            now + CommanderLeaseSeconds);
        if (Leases.TryAcquire(request, now, out _))
            CommanderSquads.Add(id);
    }

    /// <summary>
    /// Replaces only the accepted commander intent while preserving the objective
    /// revision already owned by this squad. Order executors call this after their
    /// stability policy has selected the destination that will actually be kept.
    /// </summary>
    internal static void RefreshCommanderSquadIntent(
        Squad? squad,
        CommanderRole role,
        Vector3 destination,
        float now)
    {
        if (squad == null || !IsFinite(destination))
            return;

        var id = ContactKnowledge.GetSquadId(squad);
        if (!Leases.TryGet(CommandChannel.SquadOrders, id, now, out var current) ||
            !string.Equals(current.Owner, CommanderOwner, StringComparison.Ordinal))
        {
            return;
        }

        LeaseCommanderSquad(squad, current.ObjectiveRevision, role, destination, now);
    }

    internal static bool TryGetCommanderSquadIntent(
        Squad? squad,
        out Vector3 destination)
    {
        destination = default;
        if (squad == null)
            return false;

        var id = ContactKnowledge.GetSquadId(squad);
        if (!Leases.TryGet(CommandChannel.SquadOrders, id, Time.time, out var lease) ||
            !string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal) ||
            !lease.Destination.IsFinite)
        {
            return false;
        }

        destination = new Vector3(lease.Destination.X, squad.Leader?.transform.position.y ?? 0f,
            lease.Destination.Z);
        return true;
    }

    internal static bool OwnsSquad(Squad? squad)
    {
        if (squad == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return false;
        }

        ObserveExternalOwnership(squad);
        var id = ContactKnowledge.GetSquadId(squad);
        return Leases.TryGet(CommandChannel.SquadOrders, id, Time.time, out var lease) &&
               string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal);
    }

    internal static bool IsReserveSquad(Squad? squad)
    {
        if (squad == null)
            return false;
        var id = ContactKnowledge.GetSquadId(squad);
        return Leases.TryGet(CommandChannel.SquadOrders, id, Time.time, out var lease) &&
               string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal) &&
               string.Equals(lease.Role, CommanderRole.Reserve.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Records a stationary fighting area independently of the game's mutable
    /// Squad.order fields. This covers defenders as well as attackers waiting in a
    /// support, reserve, or assault-bound position.
    /// </summary>
    internal static void RegisterCommanderDefensiveArea(
        Squad? squad,
        Vector3 center,
        float radius)
    {
        if (squad == null || !IsFinite(center) || !float.IsFinite(radius) || radius < 0f)
            return;

        var id = ContactKnowledge.GetSquadId(squad);
        if (!Leases.TryGet(CommandChannel.SquadOrders, id, Time.time, out var lease) ||
            !string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal))
        {
            CommanderDefensiveAreas.Remove(id);
            return;
        }

        CommanderDefensiveAreas[id] = new CommanderDefensiveArea(
            lease.ObjectiveRevision,
            center,
            radius);
    }

    internal static void ClearCommanderStationaryArea(Squad? squad)
    {
        if (squad != null)
            CommanderDefensiveAreas.Remove(ContactKnowledge.GetSquadId(squad));
    }

    internal static bool TryGetCommanderDefensiveArea(
        Squad? squad,
        out Vector3 center,
        out float radius)
    {
        center = default;
        radius = 0f;
        if (squad == null || !Settings.CommanderEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return false;
        }

        ObserveExternalOwnership(squad);
        var id = ContactKnowledge.GetSquadId(squad);
        if (!CommanderDefensiveAreas.TryGetValue(id, out var area) ||
            !Leases.TryGet(CommandChannel.SquadOrders, id, Time.time, out var lease) ||
            !string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal) ||
            lease.ObjectiveRevision != area.ObjectiveRevision)
        {
            CommanderDefensiveAreas.Remove(id);
            return false;
        }

        center = area.Center;
        radius = area.Radius;
        return true;
    }

    internal static bool TryGetCommanderCohesionAnchor(
        Squad? squad,
        out Vector3 center,
        out float radius)
    {
        center = default;
        radius = SquadCohesionCore.CommanderMemberRadiusMeters;
        var leader = squad?.Leader;
        if (squad == null || leader == null || !OwnsSquad(squad))
            return false;

        // When a stationary area is registered its radius already reflects the cover
        // cluster the squad was anchored to (fix 3/4). Use it as the cohesion leash
        // so a squad spread along a building or trench line is not compressed back
        // into a flat 30 m disc; never tighten below the default member radius.
        if (TryGetCommanderDefensiveArea(squad, out _, out var areaRadius) &&
            float.IsFinite(areaRadius) && areaRadius > 0f)
        {
            radius = Mathf.Max(SquadCohesionCore.CommanderMemberRadiusMeters, areaRadius);
        }

        center = leader.transform.position;
        return IsFinite(center);
    }

    internal static void MarkMissionScripted(Squad? squad)
    {
        if (squad == null)
            return;
        MarkExternalSquad(squad, ScriptOwner);
    }

    internal static void ReleaseCommanderSquad(int squadId)
    {
        Leases.Release(CommandChannel.SquadOrders, squadId, CommanderOwner);
        CommanderSquads.Remove(squadId);
        CommanderSquadsSeen.Remove(squadId);
        CommanderDefensiveAreas.Remove(squadId);
    }

    /// <summary>
    /// Shared guard/try/release/log body for the commander-order executors below.
    /// Each caller supplies its own guard expression as <paramref name="authorized"/>
    /// (so a missing entity never reaches <paramref name="release"/>), the native
    /// call to attempt, the lease to release on failure, and the entity description
    /// to fold into the warning log.
    /// </summary>
    private static bool ExecuteGuardedNativeOrder(
        bool authorized, Action? nativeWrite, Action release, string failureContext)
    {
        if (!authorized)
            return false;

        try
        {
            nativeWrite!();
            return true;
        }
        catch (Exception ex)
        {
            // Fail open to native AI for this entity. Keeping an invalid lease is
            // more damaging than losing one commander order.
            release();
            Plugin.LogSource.LogWarning(
                $"Ground director released failed {failureContext}: {ex.Message}");
            return false;
        }
    }

    internal static bool ExecuteCommanderSquadOrder(Squad? squad, Action nativeWrite)
    {
        var id = squad == null ? 0 : ContactKnowledge.GetSquadId(squad);
        return ExecuteGuardedNativeOrder(
            squad != null && nativeWrite != null && OwnsSquad(squad),
            nativeWrite,
            () => ReleaseCommanderSquad(id),
            $"squad order {id}");
    }

    internal static void LeaseCommanderVehicle(
        Vehicle? vehicle,
        int objectiveRevision,
        string role,
        Vector3 destination,
        float now)
    {
        if (vehicle == null)
            return;

        var id = vehicle.GetInstanceID();
        var faction = vehicle.GetVehicleFaction() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(faction))
            EntityFactions[id] = faction;
        Leases.TryAcquire(new CommandLeaseRequest(
            CommandChannel.VehicleOrders,
            id,
            CommanderOwner,
            CommandAuthority.CommanderIntent,
            Math.Max(0, objectiveRevision),
            role ?? string.Empty,
            new MapPoint(destination.x, destination.z),
            "host-only;objective-bound",
            now + CommanderLeaseSeconds), now, out _);
    }

    internal static bool ExecuteCommanderVehicleOrder(
        Vehicle? vehicle,
        Squad? crew,
        Action nativeWrite)
    {
        var id = vehicle == null ? 0 : vehicle.GetInstanceID();
        return ExecuteGuardedNativeOrder(
            vehicle != null && crew != null && nativeWrite != null && OwnsSquad(crew) &&
            Leases.TryGet(CommandChannel.VehicleOrders, id, Time.time, out var lease) &&
            string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal),
            nativeWrite,
            () => Leases.Release(CommandChannel.VehicleOrders, id, CommanderOwner),
            $"vehicle order {id}");
    }

    /// <summary>
    /// True when the commander has leased this vehicle for an offensive armor
    /// role (assault or flank support). The lease is the semantically correct
    /// signal for "this tank is committed to the assault" — Squad.order alone
    /// misses defending/reserve armor that still has a live movement order.
    /// </summary>
    internal static bool VehicleHasOffensiveArmorRole(Vehicle? vehicle)
    {
        if (vehicle == null)
            return false;

        var id = vehicle.GetInstanceID();
        if (!Leases.TryGet(CommandChannel.VehicleOrders, id, Time.time, out var lease) ||
            !string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal))
        {
            return false;
        }

        // nameof, not Enum.ToString(): this runs per frame for a parked tank near
        // infantry, and Enum.ToString() allocates. The member names are identical
        // to the CommanderMvp.ArmorRole names written into the lease, so the
        // compile-time constants compare exactly the same.
        return string.Equals(lease.Role, nameof(ArmorRoleAssignment.AssaultSupport), StringComparison.Ordinal) ||
               string.Equals(lease.Role, nameof(ArmorRoleAssignment.FlankSupport), StringComparison.Ordinal);
    }

    /// <summary>
    /// Releases a wedged tank's commander vehicle lease so the next planning
    /// pass can reassign it, mirroring ReleaseCommanderAircraft below.
    /// </summary>
    internal static void ReleaseCommanderVehicle(int vehicleId)
        => Leases.Release(CommandChannel.VehicleOrders, vehicleId, CommanderOwner);

    internal static bool LeaseCommanderAircraft(
        VehiclePlane? plane,
        Vector3 destination,
        float now)
    {
        if (plane == null)
            return false;

        var id = plane.GetInstanceID();
        var faction = plane.GetVehicleFaction() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(faction))
            EntityFactions[id] = faction;
        return Leases.TryAcquire(new CommandLeaseRequest(
            CommandChannel.AircraftOrders,
            id,
            CommanderOwner,
            CommandAuthority.CommanderIntent,
            CurrentObjectiveRevision(faction),
            "report-driven strike",
            new MapPoint(destination.x, destination.z),
            "host-only;live-target;friendly-clearance",
            now + CommanderLeaseSeconds), now, out _);
    }

    internal static bool ExecuteCommanderAircraftOrder(
        AIPlane? ai,
        VehiclePlane? plane,
        Action nativeWrite)
    {
        var id = plane == null ? 0 : plane.GetInstanceID();
        return ExecuteGuardedNativeOrder(
            ai != null && plane != null && nativeWrite != null &&
            Leases.TryGet(CommandChannel.AircraftOrders, id, Time.time, out var lease) &&
            string.Equals(lease.Owner, CommanderOwner, StringComparison.Ordinal),
            nativeWrite,
            () => ReleaseCommanderAircraft(id),
            $"aircraft order {id}");
    }

    internal static void ReleaseCommanderAircraft(int aircraftId)
        => Leases.Release(CommandChannel.AircraftOrders, aircraftId, CommanderOwner);

    internal static bool ExecuteAutonomousAircraftOrder(
        AIPlane? ai,
        VehiclePlane? plane,
        Vector3 destination,
        Action nativeWrite,
        float now)
    {
        if (ai == null || plane == null || nativeWrite == null)
            return false;

        var id = plane.GetInstanceID();
        if (!Leases.TryAcquire(new CommandLeaseRequest(
                CommandChannel.AircraftOrders,
                id,
                AircraftAutonomyOwner,
                CommandAuthority.NativeFallback,
                CurrentObjectiveRevision(plane.GetVehicleFaction()),
                "target-of-opportunity",
                new MapPoint(destination.x, destination.z),
                "idle-aircraft-only",
                now + CommanderLeaseSeconds), now, out var lease) ||
            !string.Equals(lease.Owner, AircraftAutonomyOwner, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            nativeWrite();
            return true;
        }
        catch
        {
            Leases.Release(CommandChannel.AircraftOrders, id, AircraftAutonomyOwner);
            throw;
        }
    }

    internal static void ReleaseAutonomousAircraft(int aircraftId)
        => Leases.Release(CommandChannel.AircraftOrders, aircraftId, AircraftAutonomyOwner);

    internal static void ExecuteAircraftSafetyCancellation(AIPlane? ai, VehiclePlane? plane)
    {
        if (ai == null || plane == null)
            return;

        var id = plane.GetInstanceID();
        var now = Time.time;
        if (!Leases.TryAcquire(new CommandLeaseRequest(
                CommandChannel.AircraftOrders,
                id,
                AircraftSafetyOwner,
                CommandAuthority.RequiredSafety,
                CurrentObjectiveRevision(plane.GetVehicleFaction()),
                "friendly-fire-safety",
                default,
                "cancel unsafe strike",
                now + 0.25f), now, out var lease) ||
            !string.Equals(lease.Owner, AircraftSafetyOwner, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            ai.CancelAttackingTarget();
        }
        finally
        {
            Leases.Release(CommandChannel.AircraftOrders, id, AircraftSafetyOwner);
        }
    }

    internal static void ReleaseAircraft(int aircraftId)
    {
        Leases.ReleaseEntity(aircraftId);
        EntityFactions.Remove(aircraftId);
    }

    internal static bool ProtectStaticWeaponAssignment(
        Soldier? soldier,
        Vehicle? weapon,
        int objectiveRevision,
        float now)
    {
        if (soldier == null || weapon == null)
            return false;
        var soldierId = soldier.GetInstanceID();
        var destination = weapon.GetCenterOfUnit();
        var request = new CommandLeaseRequest(
            CommandChannel.InfantryAssignment,
            soldierId,
            StaticWeaponOwner,
            CommandAuthority.ProtectedFortification,
            Math.Max(0, objectiveRevision),
            "static-gunner",
            new MapPoint(destination.x, destination.z),
            $"weapon={weapon.GetInstanceID()}",
            float.PositiveInfinity);
        if (!Leases.TryAcquire(request, now, out _))
            return false;
        EntityFactions[soldierId] = soldier.faction ?? string.Empty;
        return true;
    }

    internal static bool HasProtectedInfantryAssignment(Soldier? soldier)
    {
        if (soldier == null)
            return false;
        return Leases.TryGet(CommandChannel.InfantryAssignment, soldier.GetInstanceID(), Time.time,
                   out var lease) &&
               lease.Authority == CommandAuthority.ProtectedFortification;
    }

    internal static bool ExecuteProtectedInfantryAssignment(
        Soldier? soldier,
        Vehicle? weapon,
        Action nativeWrite)
    {
        var id = soldier == null ? 0 : soldier.GetInstanceID();
        var weaponId = weapon == null ? 0 : weapon.GetInstanceID();
        return ExecuteGuardedNativeOrder(
            soldier != null && weapon != null && nativeWrite != null &&
            Leases.TryGet(CommandChannel.InfantryAssignment, id, Time.time, out var lease) &&
            string.Equals(lease.Owner, StaticWeaponOwner, StringComparison.Ordinal) &&
            string.Equals(lease.Constraints, $"weapon={weaponId}", StringComparison.Ordinal),
            nativeWrite,
            () => ReleaseInfantryAssignment(soldier),
            $"weapon assignment {id}");
    }

    internal static void ReleaseInfantryAssignment(Soldier? soldier)
    {
        if (soldier != null)
            Leases.Release(CommandChannel.InfantryAssignment, soldier.GetInstanceID());
    }

    internal static bool ClaimSupportChannel(Squad? squad, float now)
    {
        if (squad == null)
            return false;
        ObserveExternalOwnership(squad);
        var id = ContactKnowledge.GetSquadId(squad);
        if (HasExternalSquadOwnership(id, now))
            return false;
        var revision = CurrentObjectiveRevision(squad.Leader?.faction);
        return Leases.TryAcquire(new CommandLeaseRequest(
            CommandChannel.SupportRequest,
            id,
            CommanderOwner,
            CommandAuthority.CommanderIntent,
            revision,
            "support-broker",
            default,
            "one-request-per-squad",
            now + CommanderLeaseSeconds), now, out _);
    }

    internal static void ExecuteGrenadeSafetyHalt(Soldier? soldier, bool haltMovement)
    {
        if (!AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        var ai = soldier.aiController;
        if (haltMovement && ai != null)
            ContactResponse.StopDangerMovement(
                ai, soldier, SoldierPose.Crouch, Time.deltaTime);
        ContactResponse.ExecuteStopFire(soldier);
    }

    internal static void ExecuteRequiredActionHalt(Soldier? soldier)
    {
        if (!AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        var ai = soldier.aiController;
        if (ai == null)
            return;
        ContactResponse.ExecuteFireInhibition(ai, soldier, clearWeaponRange: false);
        ContactResponse.StopDangerMovement(ai, soldier, SoldierPose.Prone, Time.deltaTime);
    }

    internal static void ExecuteSoldierFireInhibition(
        SoldierAI? ai,
        Soldier? soldier,
        bool clearWeaponRange = false)
    {
        if (!AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        ContactResponse.ExecuteFireInhibition(ai, soldier, clearWeaponRange);
    }

    internal static void ExecuteSoldierStopFire(Soldier? soldier)
    {
        if (!AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        ContactResponse.ExecuteStopFire(soldier);
    }

    internal static void ExecuteSoldierAim(Soldier? soldier, bool aiming)
    {
        if (!AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        ContactResponse.ExecuteAim(soldier, aiming);
    }

    internal static void ExecuteHazardEscape(
        SoldierAI? ai,
        Soldier? soldier,
        Vector3 escape)
    {
        if (ai == null || !AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        ContactResponse.ExecuteFireInhibition(ai, soldier, clearWeaponRange: false);
        ContactResponse.ExecuteHazardEscape(ai, soldier, escape);
    }

    internal static void UpdateSoldier(SoldierAI ai, Soldier soldier, float now)
    {
        if (ai == null || soldier == null || !MultiplayerAuthority.CanMutateGameplay())
            return;

        var squad = soldier.joinedSquad;
        if (squad != null)
            ObserveExternalOwnership(squad);

        var leader = squad?.Leader;
        if (squad != null && leader != null && leader.GetInstanceID() == soldier.GetInstanceID())
        {
            SquadRadioSupport.Update(squad, now);
            StaticAntiTankStaffing.Update(squad, now);
        }

        if (Settings.PerceptionEnabled.Value)
        {
            GunfireAwareness.Poll(soldier, now);
            SoldierSequentialUpdatePatch.ApplyPerception(ai, soldier);
        }
        else
            AiState.TargetMemory.Remove(soldier.GetInstanceID());

        var id = soldier.GetInstanceID();
        var currentSquadId = squad == null ? 0 : ContactKnowledge.GetSquadId(squad);
        if (SoldierResolutions.TryGetValue(id, out var previousResolution) &&
            previousResolution.Snapshot.SquadId != currentSquadId)
        {
            ReleaseInfantryAssignment(soldier);
            ContactResponse.ResetDefensivePositionOwnership(id);
            SoldierResolutions.Remove(id);
        }
        if (!Settings.TankFearEnabled.Value)
            AiState.TankCoverHideUntil.Remove(id);

        var snapshot = CaptureSnapshot(ai, soldier, squad);
        var reusableResolution = SoldierResolutions.TryGetValue(id, out var priorResolution)
            ? priorResolution
            : new SoldierTacticalResolution(snapshot);
        CollectProposals(snapshot, TacticalProposalBuffer);
        var resolution = TacticalArbitrationCore.ResolveInto(
            reusableResolution, snapshot, TacticalProposalBuffer);
        SoldierResolutions[id] = resolution;

        // Suppression, fire safety, and lethal hazard systems remain local safety
        // executors. The selected movement owner decides whether contact/tank policy
        // may replace the current assignment.
        ContactResponse.UpdateSuppressionReaction(ai, soldier, now, Time.deltaTime);
        KnownTargetSuppressiveFire.Schedule(ai, soldier, now);

        var movementSource = resolution.Winners.TryGetValue(TacticalChannel.Movement, out var movement)
            ? movement.Source
            : ProposalSource.Native;
        if (movementSource == ProposalSource.DefensivePosition)
            ContactResponse.UpdateDefensivePosition(ai, soldier);
        else if (movementSource == ProposalSource.PlayerHold)
            ContactResponse.UpdateDefensivePosition(ai, soldier);
        else if (movementSource is ProposalSource.Contact or ProposalSource.CoverHold or
            ProposalSource.Suppression)
            ContactResponse.Update(ai, soldier);
        else if (!Settings.ContactResponseEnabled.Value)
            ContactResponse.Disable(ai, soldier);
        else
            ContactResponse.YieldMovementToHigherAuthority(
                ai,
                soldier,
                releaseDefensiveAnchor:
                    movementSource is ProposalSource.External or ProposalSource.ProtectedAssignment);

        if (movementSource == ProposalSource.Hazard)
        {
            SoldierFireDanger.Execute(
                ai,
                soldier,
                new Vector3(snapshot.HazardPosition.X, soldier.transform.position.y,
                    snapshot.HazardPosition.Z),
                now);
        }

        if (movementSource == ProposalSource.TankFear)
            SoldierSequentialUpdatePatch.ApplyTankFear(ai, soldier);

        ContactResponse.ApplyMovementProgressWatchdog(
            ai,
            soldier,
            movementSource,
            movement.Action,
            now);

        ContactResponse.MaintainOwnedPose(ai, soldier, now);
        InfantryAntiArmorFireDiscipline.Update(ai, soldier);
        HandheldWeaponClassifier.EnforceEngagementRange(soldier, ai);
        SoldierTacticalSprintPatch.UpdateMovingFireInhibition(ai, soldier);
        ContactResponse.RestoreFireAfterOwnedInhibition(ai, soldier);

        if (Settings.BattleChatterEnabled.Value)
            BattleChatter.Update(ai, soldier, now);
    }

    internal static void ReleaseSoldier(int soldierId)
    {
        Leases.ReleaseEntity(soldierId);
        SoldierResolutions.Remove(soldierId);
        EntityFactions.Remove(soldierId);
        SoldierFireDanger.Remove(soldierId);
    }

    internal static SoldierTacticalResolution? DebugResolution(int soldierId)
        => SoldierResolutions.TryGetValue(soldierId, out var resolution) ? resolution : null;

    internal static void CollectDebugLeases(float now, List<CommandLease> destination)
        => Leases.CopyActive(now, destination);

    internal static void ClearRuntimeState()
    {
        Leases.Clear();
        FactionPostures.Clear();
        SoldierResolutions.Clear();
        CommanderSquads.Clear();
        CommanderSquadsSeen.Clear();
        CommanderDefensiveAreas.Clear();
        EntityFactions.Clear();
        StaticAntiTankStaffing.ResetBattle();
        SoldierFireDanger.Reset();
        _planning = false;
        _objectiveRevision = 0;
    }

    internal static void ReleaseAllCommanderLeases()
    {
        Leases.ReleaseOwner(CommanderOwner);
        CommanderSquads.Clear();
        CommanderSquadsSeen.Clear();
        CommanderDefensiveAreas.Clear();
    }

    private static SoldierTacticalSnapshot CaptureSnapshot(
        SoldierAI ai,
        Soldier soldier,
        Squad? squad)
    {
        var position = soldier.GetCenterOfUnit();
        var threat = AiState.GetContactState(soldier.GetInstanceID()).LastThreatPosition;
        var playerLed = false;
        var scriptOwned = false;
        if (squad != null && Leases.TryGet(
                CommandChannel.SquadOrders,
                ContactKnowledge.GetSquadId(squad),
                Time.time,
                out var externalLease))
        {
            playerLed = string.Equals(externalLease.Owner, PlayerOwner, StringComparison.Ordinal);
            scriptOwned = string.Equals(externalLease.Owner, ScriptOwner, StringComparison.Ordinal);
        }
        var lethalHazard = SoldierFireDanger.TrySense(soldier, Time.time, out var escape);
        var hasCommanderIntent = TryGetCommanderSquadIntent(squad, out var commanderDestination);
        return new SoldierTacticalSnapshot(
            soldier.GetInstanceID(),
            squad == null ? 0 : ContactKnowledge.GetSquadId(squad),
            CurrentObjectiveRevision(soldier.faction),
            IsAttackingFaction(soldier.faction) ? StrategicPosture.Attack : StrategicPosture.Defend,
            playerLed,
            scriptOwned,
            soldier.IsAlive,
            soldier.IsOnVehicle(),
            soldier.GetSuppressionValue() >= Settings.CrouchSuppression.Value,
            soldier.IsReloading,
            lethalHazard,
            new MapPoint(position.x, position.z),
            new MapPoint(threat.x, threat.z),
            new MapPoint(escape.x, escape.z),
            ContactResponse.SenseMovement(ai, soldier, Time.time),
            AiOwnership.IsAutonomous(soldier),
            ContactResponse.TryGetPlayerHoldOrder(soldier, out _, out _),
            HasProtectedInfantryAssignment(soldier),
            SoldierSequentialUpdatePatch.HasNearbyTankThreat(soldier),
            hasCommanderIntent,
            new MapPoint(commanderDestination.x, commanderDestination.z));
    }

    private static void CollectProposals(
        SoldierTacticalSnapshot snapshot,
        List<TacticalProposal> destination)
    {
        var options = new TacticalPolicyOptions(
            Settings.ContactResponseEnabled.Value,
            Settings.TankFearEnabled.Value);
        ProposalGenerationCore.Collect(snapshot, options, destination);
    }

    private static void ObserveExternalOwnership(Squad squad)
    {
        var id = ContactKnowledge.GetSquadId(squad);
        if (CommanderMvp.IsMissionScriptLocked(id))
        {
            MarkExternalSquad(squad, ScriptOwner);
            return;
        }

        if (CommanderMvp.HasPlayerOwnership(squad))
        {
            MarkExternalSquad(squad, PlayerOwner);
            return;
        }

        Leases.Release(CommandChannel.SquadOrders, id, PlayerOwner);
        Leases.Release(CommandChannel.SquadOrders, id, ScriptOwner);
    }

    private static void MarkExternalSquad(Squad squad, string owner)
    {
        var id = ContactKnowledge.GetSquadId(squad);
        var faction = squad.Leader?.faction ?? string.Empty;
        var revision = CurrentObjectiveRevision(faction);
        Leases.TryAcquire(new CommandLeaseRequest(
            CommandChannel.SquadOrders,
            id,
            owner,
            CommandAuthority.PlayerOrScript,
            revision,
            "external",
            default,
            "explicit external ownership",
            float.PositiveInfinity), Time.time, out _);
        CommanderSquads.Remove(id);
        CommanderDefensiveAreas.Remove(id);
        EntityFactions[id] = faction;
    }

    private static bool HasExternalSquadOwnership(int squadId, float now)
        => Leases.TryGet(CommandChannel.SquadOrders, squadId, now, out var lease) &&
           lease.Authority == CommandAuthority.PlayerOrScript;

    private static void ReleaseFactionAssignments(string faction, int revision)
    {
        foreach (var pair in EntityFactions.Where(pair =>
                     string.Equals(pair.Value, faction, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            foreach (var channel in Enum.GetValues<CommandChannel>())
            {
                if (Leases.TryGet(channel, pair.Key, Time.time, out var lease) &&
                    lease.ObjectiveRevision < revision)
                {
                    Leases.Release(channel, pair.Key);
                    if (channel == CommandChannel.SquadOrders)
                        CommanderDefensiveAreas.Remove(pair.Key);
                }
            }
        }
    }

    private readonly record struct FactionPostureState(
        int ObjectiveId,
        StrategicPosture Posture,
        int Revision,
        float LastObservedAt);

    private readonly record struct CommanderDefensiveArea(
        int ObjectiveRevision,
        Vector3 Center,
        float Radius);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
