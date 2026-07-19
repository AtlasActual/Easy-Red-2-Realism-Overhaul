using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class TransportDismount
{
    private const float ThreatScanIntervalSeconds = 0.5f;
    private const float UnloadRetryIntervalSeconds = 0.25f;

    private static readonly Dictionary<int, DismountState> States = new();
    private static readonly List<Soldier> Occupants = new();
    private static readonly List<Soldier> Passengers = new();

    internal static void ResetBattle()
    {
        States.Clear();
        Occupants.Clear();
        Passengers.Clear();
    }

    internal static void NotifyIncomingFire(Vehicle vehicle, float now)
    {
        if (vehicle == null || !vehicle.IsTransportVehicle())
            return;

        var state = GetState(vehicle.GetInstanceID());
        BeginDismount(state, now, "incoming fire", 0f, immediate: true);
    }

    internal static void Update(AIVehicle ai, float now)
    {
        var vehicle = ai.veh;
        if (vehicle == null)
            return;

        var vehicleId = vehicle.GetInstanceID();
        if (!VehicleCanAutonomouslyDismount(vehicle) ||
            !TryCollectEligibleOccupants(vehicle))
        {
            States.Remove(vehicleId);
            return;
        }

        var state = GetState(vehicleId);
        if (state.Pending)
        {
            ContinueDismount(vehicle, vehicleId, state, now);
            return;
        }

        if (now < state.NextThreatScanAt)
            return;
        state.NextThreatScanAt = now + ThreatScanIntervalSeconds;

        if (!TryFindImminentThreat(ai, vehicle, now, out var reason, out var distance))
            return;

        BeginDismount(state, now, reason, distance, immediate: false);
        ContinueDismount(vehicle, vehicleId, state, now);
    }

    private static bool VehicleCanAutonomouslyDismount(Vehicle vehicle)
    {
        if (!vehicle.IsTransportVehicle() || !vehicle.IsLocalAIDriving() ||
            !vehicle.IsActive() ||
            vehicle.life <= 0 || vehicle.IsDisabled() ||
            vehicle.PlayerIsInside() || vehicle.PlayerIsDriving())
        {
            return false;
        }

        var driver = vehicle.GetDriver();
        if (driver == null || !driver.IsAlive || !driver.IsAI() ||
            driver.IsFPSPlayer() || driver.GetCurrentVehicle() != vehicle ||
            driver.LuaSoldier?.HasScriptAssigned() == true)
        {
            return false;
        }

        return driver.joinedSquad == null ||
               CommanderMvp.AllowsAutonomousSafetyResponse(driver.joinedSquad);
    }

    private static bool TryCollectEligibleOccupants(Vehicle vehicle)
    {
        Occupants.Clear();
        Passengers.Clear();

        var driver = vehicle.GetDriver();
        var driverId = driver != null ? driver.GetInstanceID() : 0;

        var seatedSoldiers = vehicle.SittedSoldiers().GetEnumerator();
        var iterator = seatedSoldiers.Cast<Il2CppSystem.Collections.IEnumerator>();
        while (iterator.MoveNext())
        {
            var seated = seatedSoldiers.Current;
            if (seated == null || !seated.IsAlive || seated.GetCurrentVehicle() != vehicle)
                continue;

            var sync = seated.GetComponent<SyncSoldier>();
            if (seated.IsFPSPlayer() ||
                sync != null && sync.IsControlledByAPlayer())
            {
                return false;
            }

            Occupants.Add(seated);

            // Match the game's own onlyPassengers seat rule.  Squad.IsVehicleCrew
            // is not reliable after scenario boarding and caused valid infantry
            // cargo to be rejected before threat detection ever ran.
            var seat = seated.currentVehicleSeat;
            if (!seated.IsAI() || seated.GetInstanceID() == driverId ||
                seat == null || seat.hasTurret)
            {
                continue;
            }

            var squad = seated.joinedSquad;
            if (squad == null ||
                !CommanderMvp.AllowsAutonomousSafetyResponse(squad))
            {
                return false;
            }

            Passengers.Add(seated);
        }

        return Passengers.Count > 0;
    }

    private static bool TryFindImminentThreat(
        AIVehicle ai,
        Vehicle vehicle,
        float now,
        out string reason,
        out float distance)
    {
        reason = string.Empty;
        distance = float.MaxValue;
        var ownFaction = vehicle.GetVehicleFaction();

        ConsiderVisibleThreat(
            vehicle.CurrentVisibleTarget, vehicle, ownFaction,
            ref reason, ref distance);

        foreach (var occupant in Occupants)
        {
            var controller = occupant.aiController;
            if (controller != null)
            {
                ConsiderVisibleThreat(
                    TargetAcquisition.ResolveObservedTarget(controller, occupant),
                    vehicle,
                    ownFaction,
                    ref reason,
                    ref distance);
            }
        }

        foreach (var passenger in Passengers)
        {
            if (!ContactKnowledge.TryGetImminentTransportThreat(
                    passenger, vehicle.GetCenterOfUnit(), now,
                    out var contactKind, out var contactDistance) ||
                contactDistance >= distance)
            {
                continue;
            }

            reason = contactKind == ContactKind.GroundVehicle
                ? "fresh report of enemy armor"
                : "fresh report of nearby infantry";
            distance = contactDistance;
        }

        return distance < float.MaxValue;
    }

    private static void ConsiderVisibleThreat(
        Spottable? target,
        Vehicle transport,
        string ownFaction,
        ref string reason,
        ref float closestDistance)
    {
        if (!TargetAcquisition.IsUsableTarget(target) || target!.IsAirVehicle())
            return;

        var targetSoldier = target.TryCast<Soldier>();
        var targetVehicle = targetSoldier == null ? target.TryCast<Vehicle>() : null;
        if (targetSoldier != null && !targetSoldier.IsAlive ||
            targetVehicle != null && targetVehicle.life <= 0)
        {
            return;
        }

        var targetFaction = targetSoldier?.faction ??
                            targetVehicle?.GetVehicleFaction() ??
                            string.Empty;
        if (string.IsNullOrWhiteSpace(ownFaction) ||
            string.IsNullOrWhiteSpace(targetFaction) ||
            !ResourcesManager.IsEnemyFaction(ownFaction, targetFaction))
        {
            return;
        }

        var threatType = target.IsWheeledVehicleOrTank()
            ? TransportThreatType.GroundVehicle
            : TransportThreatType.Infantry;
        var candidateDistance = Vector3.Distance(
            transport.GetCenterOfUnit(), target.GetCenterOfUnit());
        if (!TransportDismountDecisionCore.IsImminent(new TransportThreatInput(
                threatType,
                candidateDistance,
                Age: 0f,
                Confidence: 1f,
                DirectlyVisible: true)) ||
            candidateDistance >= closestDistance)
        {
            return;
        }

        reason = threatType == TransportThreatType.GroundVehicle
            ? "visible enemy vehicle"
            : "visible nearby infantry";
        closestDistance = candidateDistance;
    }

    private static void ContinueDismount(
        Vehicle vehicle,
        int vehicleId,
        DismountState state,
        float now)
    {
        vehicle.Brake();
        var rigidbody = vehicle.GetRigidbody();
        var speed = rigidbody != null ? rigidbody.velocity.magnitude : 0f;
        var brakingSeconds = Mathf.Max(0f, now - state.DetectedAt);
        if (!TransportDismountDecisionCore.ReadyToUnload(
                speed, brakingSeconds, state.Immediate))
        {
            return;
        }

        if (now < state.NextUnloadAttemptAt)
            return;
        state.NextUnloadAttemptAt = now + UnloadRetryIntervalSeconds;

        // Use the native seat-aware eject path directly.  Some transport modes
        // make DropInfantry eject the vehicle crew as well, while this response is
        // specifically for the infantry cargo.  A failed native attempt is kept
        // pending and retried instead of being reported as a successful unload.
        if (!vehicle.EjectAll(onlyPassengers: true, ignorePlayers: true))
            return;

        AiState.Trace(
            $"Transport dismount: vehicle {vehicleId} unloaded passengers for " +
            $"{state.Reason}" +
            (state.Distance > 0f ? $" at {state.Distance:0}m" : string.Empty));
        States.Remove(vehicleId);
    }

    private static void BeginDismount(
        DismountState state,
        float now,
        string reason,
        float distance,
        bool immediate)
    {
        if (!state.Pending)
        {
            state.Pending = true;
            state.DetectedAt = now;
            state.Reason = reason;
            state.Distance = distance;
        }

        state.Immediate |= immediate;
    }

    private static DismountState GetState(int vehicleId)
    {
        if (States.TryGetValue(vehicleId, out var state))
            return state;

        state = new DismountState();
        States[vehicleId] = state;
        return state;
    }

    private sealed class DismountState
    {
        internal float NextThreatScanAt;
        internal bool Pending;
        internal float DetectedAt;
        internal bool Immediate;
        internal string Reason = string.Empty;
        internal float Distance;
        internal float NextUnloadAttemptAt;
    }
}

[HarmonyPatch(typeof(AIVehicle), "Update")]
internal static class AnticipatoryTransportDismountPatch
{
    [HarmonyPostfix]
    private static void Postfix(AIVehicle __instance)
    {
        if (!Settings.DangerReactionsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        try
        {
            TransportDismount.Update(__instance, Time.time);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Transport dismount update failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(VehicleWithWheels), nameof(VehicleWithWheels.OnVehicleSuppressed))]
internal static class TransportIncomingFireDismountPatch
{
    [HarmonyPostfix]
    private static void Postfix(VehicleWithWheels __instance)
    {
        if (!Settings.DangerReactionsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        try
        {
            TransportDismount.NotifyIncomingFire(__instance, Time.time);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Transport incoming-fire response failed: {ex.Message}");
        }
    }
}
