using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// The only coordinator allowed to grant ownership of ground-AI command channels.
/// Feature modules remain sensors/policies; native mutations are reached through
/// this director or the soldier executor selected here.
///
/// The AI commander (attack/defense operation planning, squad/vehicle
/// command leases) was removed: ER2's maps are balanced for vanilla's continuous
/// frontal pressure, and the commander's staged gating made attacks stall. This
/// director now only arbitrates the tactical layer (perception, suppression,
/// danger reactions, cover), tracks external (player/mission-script) ownership so
/// that layer never fights a human or scripted order, and hosts the one salvaged
/// commander-adjacent behavior: static-weapon staffing.
/// </summary>
internal static class GroundAiDirector
{
    private const string StaticWeaponOwner = "ground-director/static-weapon";
    private const string PlayerOwner = "external/player";
    private const string ScriptOwner = "external/script";

    private static readonly CommandLeaseRegistryCore Leases = new();
    private static readonly Dictionary<int, SoldierTacticalResolution> SoldierResolutions = new();
    private static readonly Dictionary<int, ProposalSource> LastMovementAuthority = new();
    private static readonly List<TacticalProposal> TacticalProposalBuffer = new(12);
    private static readonly HashSet<int> ScriptLockedSquads = new();
    private static readonly Dictionary<int, string> EntityFactions = new();

    internal static void UpdateBattle(BattleManager manager, float now)
    {
        // Continuous authority check: if multiplayer authority is lost mid-battle
        // (e.g. a host migration), locally held leases must not linger.
        if (!MultiplayerAuthority.CanMutateGameplay())
            ClearRuntimeState();
    }

    /// <summary>
    /// True when the local faction is attacking this objective. Native-derived only
    /// (no side-planning posture cache): falls back to the battle's invader/defender
    /// classification directly.
    /// </summary>
    internal static bool IsAttackingFaction(string? faction)
    {
        if (string.IsNullOrWhiteSpace(faction))
            return false;

        var battle = BattleManager.GetCurrentBattleData();
        return battle != null && battle.IsInvaderFaction(faction);
    }

    internal static bool IsDefendingSquad(Squad? squad)
    {
        var faction = squad?.Leader?.faction;
        if (string.IsNullOrWhiteSpace(faction))
            return false;

        var battle = BattleManager.GetCurrentBattleData();
        return battle != null && battle.IsDefenderFaction(faction);
    }

    /// <summary>
    /// Objective-revision tracking existed only to invalidate commander leases
    /// across an objective/posture change. With the commander gone this is a
    /// harmless constant kept for the lease-request shape and for the kept
    /// callers (defensive-position stickiness) that compare revisions.
    /// </summary>
    internal static int CurrentObjectiveRevision(string? faction) => 0;

    internal static void MarkMissionScripted(Squad? squad)
    {
        if (squad == null)
            return;

        var id = SquadIdentity.GetSquadId(squad);
        ScriptLockedSquads.Add(id);
        MarkExternalSquad(squad, ScriptOwner);
    }

    /// <summary>
    /// True when a squad is not available for autonomous AI control: a live
    /// player member, a soldier with an active Lua script assignment, or a squad
    /// permanently released to a mission script.
    /// </summary>
    internal static bool IsExternallyControlledSquad(Squad? squad)
    {
        if (squad == null)
            return false;

        var id = SquadIdentity.GetSquadId(squad);
        return ScriptLockedSquads.Contains(id) || HasPlayerMember(squad) || HasScriptAssignedMember(squad);
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

    internal static void ExecuteGrenadeSafetyHalt(Soldier? soldier, bool haltMovement)
    {
        if (!AiOwnership.IsAutonomous(soldier) ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        var ai = soldier.aiController;
        if (haltMovement && ai != null)
        {
            // A grenade-safety halt has no state of its own for the movement arbiter to
            // read, so it declares its own rank (plan 018).
            ContactResponse.StopDangerMovement(
                ai, soldier, Time.deltaTime, "grenade-safety",
                MovementOwner.SafetyHalt);
        }
        ContactResponse.ExecuteStopFire(soldier);
    }

    // Edge-triggered like every other owner trace: silent while one proposal keeps the
    // movement channel, one line the moment it changes hands. A soldier whose executor
    // changes several times a second is the D2 flap, and the two sources named are the
    // pair to reconcile.
    private static void TraceMovementAuthority(int soldierId, ProposalSource source, string? constraint)
    {
        if (LastMovementAuthority.TryGetValue(soldierId, out var previous) && previous == source)
            return;

        LastMovementAuthority[soldierId] = source;
        if (!Settings.VerboseLogging.Value)
            return;

        AiState.Trace(
            $"Movement authority: soldier {soldierId} {previous}->{source} " +
            $"constraint={constraint ?? "none"}");
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
        // Fire PERMISSION belongs to the arbiter (rank b reads ExposedReloadProneOwned);
        // the halt only kills the shot already in flight.
        ContactResponse.ExecuteStopFire(soldier);
        // Declared, because the crawling-action caller halts a soldier who has no
        // ExposedReloadProneOwned ownership for the arbiter to resolve from.
        ContactResponse.StopDangerMovement(
            ai, soldier, Time.deltaTime, "required-action-halt",
            MovementOwner.SafetyHalt);
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

        // Fire permission is the arbiter's rank c (hazard); only stop the current shot.
        ContactResponse.ExecuteStopFire(soldier);
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
        var currentSquadId = squad == null ? 0 : SquadIdentity.GetSquadId(squad);
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
        // Plan 020 D2. The movement ARBITER traces which owner won the locomotion write,
        // but the executor it was asked to run is chosen one layer up, here - and a winner
        // that alternates makes the two executors fight: UpdateDefensivePosition sets an
        // engagement hold and halts, then YieldMovementToHigherAuthority tears that hold
        // down and grants an ordered move, forever. That reads in game as walking in place.
        // Name the winning proposal on every change so the flapping pair is identified from
        // the log instead of inferred from the halt/grant it produces.
        TraceMovementAuthority(id, movementSource, movement.Constraint);
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
        // Last word on the fire channel each authoritative tick, exactly as
        // MaintainOwnedPose is the last word on the pose channel.
        ContactResponse.ApplyFireDecision(ai, soldier, now, authoritative: true);

        if (Settings.BattleChatterEnabled.Value)
            BattleChatter.Update(ai, soldier, now);
    }

    internal static void ReleaseSoldier(int soldierId)
    {
        Leases.ReleaseEntity(soldierId);
        SoldierResolutions.Remove(soldierId);
        LastMovementAuthority.Remove(soldierId);
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
        SoldierResolutions.Clear();
        LastMovementAuthority.Clear();
        EntityFactions.Clear();
        StaticAntiTankStaffing.ResetBattle();
        StaticGunInfantryTargeting.ResetBattle();
        SoldierFireDanger.Reset();
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
                SquadIdentity.GetSquadId(squad),
                Time.time,
                out var externalLease))
        {
            playerLed = string.Equals(externalLease.Owner, PlayerOwner, StringComparison.Ordinal);
            scriptOwned = string.Equals(externalLease.Owner, ScriptOwner, StringComparison.Ordinal);
        }
        var lethalHazard = SoldierFireDanger.TrySense(soldier, Time.time, out var escape);
        return new SoldierTacticalSnapshot(
            soldier.GetInstanceID(),
            squad == null ? 0 : SquadIdentity.GetSquadId(squad),
            0,
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
            SoldierSequentialUpdatePatch.HasNearbyTankThreat(soldier));
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
        var id = SquadIdentity.GetSquadId(squad);
        if (ScriptLockedSquads.Contains(id))
        {
            MarkExternalSquad(squad, ScriptOwner);
            return;
        }

        if (HasPlayerMember(squad) || HasScriptAssignedMember(squad))
        {
            MarkExternalSquad(squad, PlayerOwner);
            return;
        }

        Leases.Release(CommandChannel.SquadOrders, id, PlayerOwner);
        Leases.Release(CommandChannel.SquadOrders, id, ScriptOwner);
    }

    private static void MarkExternalSquad(Squad squad, string owner)
    {
        var id = SquadIdentity.GetSquadId(squad);
        var faction = squad.Leader?.faction ?? string.Empty;
        Leases.TryAcquire(new CommandLeaseRequest(
            CommandChannel.SquadOrders,
            id,
            owner,
            CommandAuthority.PlayerOrScript,
            0,
            "external",
            default,
            "explicit external ownership",
            float.PositiveInfinity), Time.time, out _);
        EntityFactions[id] = faction;
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

    /// <summary>
    /// Shared guard/try/release/log body for the executors above. Each caller
    /// supplies its own guard expression as <paramref name="authorized"/> (so a
    /// missing entity never reaches <paramref name="release"/>), the native call
    /// to attempt, the lease to release on failure, and the entity description to
    /// fold into the warning log.
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
            // more damaging than losing one order.
            release();
            Plugin.LogSource.LogWarning(
                $"Ground director released failed {failureContext}: {ex.Message}");
            return false;
        }
    }
}

[HarmonyPatch(typeof(BattleManager), "Update")]
internal static class GroundAiDirectorBattleUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(BattleManager __instance)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            GroundAiDirector.UpdateBattle(__instance, Time.time);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }
}

[HarmonyPatch(typeof(BattleManager), "Start")]
internal static class GroundAiDirectorBattleStartPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        GroundAiDirector.ClearRuntimeState();
        TransportDismount.ResetBattle();
    }
}

[HarmonyPatch(typeof(BattleManager), "OnPhaseChange")]
internal static class GroundAiDirectorPhaseChangePatch
{
    [HarmonyPostfix]
    private static void Postfix() => GroundAiDirector.ClearRuntimeState();
}

/// <summary>
/// Marks a squad/soldier as mission-scripted so the tactical layer's external-
/// ownership tracking (used by <see cref="ProposalGenerationCore"/> to leave a
/// scripted squad's native order alone) never lets autonomous AI reclaim it.
/// This used to route through the commander; it is a standalone Lua-order
/// observation now.
/// </summary>
[HarmonyPatch]
internal static class LuaOrderOwnershipPatch
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

    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
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
            GroundAiDirector.MarkMissionScripted(__instance.connectedSquad);
    }
}

[HarmonyPatch]
internal static class LuaSoldierOwnershipPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
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
        GroundAiDirector.MarkMissionScripted(soldier?.joinedSquad);
    }
}
