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
internal static partial class CommanderMvp
{
    // Stutter-probe markers: the frame on which a side last ran its planning
    // pipeline, and which side it was. Diagnostic-only.
    internal static int LastPlanFrame = int.MinValue;
    internal static string LastPlanSideName = "none";

    // Stutter-probe markers: the frame on which queued order executions last ran,
    // and how many ran on it. Diagnostic-only.
    internal static int LastOrderExecutionFrame = int.MinValue;
    internal static int LastOrderExecutionCount;

    private const float PlanningCadenceSeconds = 8f;
    // Sides alternate on this half-cadence so only one side's planning pipeline
    // runs per tick; each side still plans every PlanningCadenceSeconds.
    private const float SidePlanningStaggerSeconds = PlanningCadenceSeconds / 2f;
    private const float OwnedAssetRefreshCadenceSeconds = 0.5f;
    // Bounds how many queued per-squad/tank order executions (cover-cluster
    // searches, raycasts, the native destination write) run per side per frame,
    // spreading a side's planning-tick order issuing across several frames instead
    // of one synchronous burst.
    private const int OrderExecutionBudgetPerSideFrame = 2;
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
    private const float CommanderClusterSearchRadiusMeters = 60f;
    private const float CommanderClusterMaxShiftMeters = 60f;
    private const int CommanderClusterCandidateLimit = 48;
    private const float StandingClusterWeight = 1f;
    private const float ProtectiveClusterWeight = 2f;
    // A cluster whose slots can actually see the attack axis is worth more than an
    // equally protected but blind one, so defenders settle where they can fire back.
    private const float ClusterFiringLaneWeightMultiplier = 1.5f;
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
    private static readonly Dictionary<string, int> LastArtilleryGunByFaction = new();
    private static readonly SideState Invaders = new(true);
    private static readonly SideState Defenders = new(false);

    private static float _nextPlanAt;
    private static bool _planInvaderNext = true;
    // Two-frame planning tick: assets collected on the tick frame are consumed by
    // the planning pipeline on the following frame (see CommanderOwnership.Update).
    private static bool _collectedPlanReady;
    private static bool _collectedPlanInvader;
    private static readonly List<SquadInfo> _collectedInvaderSquads = new();
    private static readonly List<SquadInfo> _collectedDefenderSquads = new();
    private static readonly List<TankInfo> _collectedInvaderTanks = new();
    private static readonly List<TankInfo> _collectedDefenderTanks = new();
    private static readonly List<AircraftInfo> _collectedInvaderAircraft = new();
    private static readonly List<AircraftInfo> _collectedDefenderAircraft = new();
    private static float _nextOwnedAssetRefreshAt;
    private static bool _updating;
    private static string _supportSelectionFaction = string.Empty;

    private static int RoleCount(IReadOnlyDictionary<CommanderRole, int> counts, CommanderRole role)
        => counts.TryGetValue(role, out var count) ? count : 0;

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
        // Persists this side's last-planned ownership set across ticks where the
        // other side plans instead, so staggered planning cannot drop ownership.
        internal readonly HashSet<int> OwnedSquadIds = new();
        // Deferred per-squad/tank order-execution work (cover-cluster searches,
        // raycasts, the native destination write) queued by IssueOrders/
        // IssueArmorOrders and drained at a bounded per-frame budget from
        // CommanderMvp.Update instead of running synchronously on the tick frame.
        internal readonly Queue<Action> PendingOrderExecutions = new();
        private readonly Dictionary<int, int> _positionSlots = new();
        private int _nextPositionSlot;
        internal SupportRequestStamp? ActiveSupport;
        internal int ObjectiveId = int.MinValue;
        internal bool Offensive;
        internal string OperationalSignature = string.Empty;
        internal ArmorRoleAllocationState ArmorAllocation = ArmorRoleAllocationState.Empty;
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

        internal int GetPositionSlot(int squadId)
        {
            if (_positionSlots.TryGetValue(squadId, out var slot))
                return slot;

            slot = _nextPositionSlot++;
            _positionSlots[squadId] = slot;
            return slot;
        }

        internal void BeginOperation(int objectiveId, bool offensive, float now)
        {
            Roles.Clear();
            ArmorRoles.Clear();
            _positionSlots.Clear();
            _nextPositionSlot = 0;
            // A new operation invalidates any order executions still queued for the
            // previous objective/posture; the new tick's queue replaces them wholesale.
            PendingOrderExecutions.Clear();
            ObjectiveId = objectiveId;
            Offensive = offensive;
            OperationalSignature = string.Empty;
            ArmorAllocation = ArmorRoleAllocationState.Empty;
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
            _positionSlots.Clear();
            _nextPositionSlot = 0;
            ObjectiveId = int.MinValue;
            Offensive = false;
            OperationalSignature = string.Empty;
            ArmorAllocation = ArmorRoleAllocationState.Empty;
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
            _positionSlots.Remove(id);
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
               (!CommanderMvp.OwnsSquad(__instance) &&
                !CommanderMvp.ShouldRetainArtilleryCrew(__instance));
    }
}

[HarmonyPatch(typeof(Squad), nameof(Squad.OrderLeaveVehicle),
    new Type[] { typeof(Vehicle), typeof(bool) })]
internal static class CommanderArtilleryCrewRetentionPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Squad __instance, Vehicle v, bool exitForced)
    {
        return exitForced || !CommanderMvp.ShouldRetainArtilleryCrew(__instance, v);
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
