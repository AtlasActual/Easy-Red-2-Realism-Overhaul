using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static partial class ContactResponse
{
    private static bool HasCommittedDestination(Soldier soldier)
        => soldier.HasDestinationAssigned && !soldier.DestinationReached && soldier.DestinationDistance > 1.25f;

    /// <summary>
    /// Final locomotion watchdog for movement selected by the director. Native path
    /// distance can change while a blocked soldier's feet remain in place, so only
    /// real horizontal displacement counts as progress. A stall stops the animation,
    /// requests one new path, and imposes an increasingly long quiet hold before a
    /// retry. This prevents a permanent walking-in-place loop without manufacturing
    /// a stream of replacement destinations.
    /// </summary>
    internal static void ApplyMovementProgressWatchdog(
        SoldierAI ai,
        Soldier soldier,
        ProposalSource movementSource,
        TacticalAction movementAction,
        float now)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        var watchdogMayOwnMovement =
            movementAction is TacticalAction.Move or TacticalAction.Native &&
            movementSource is not (ProposalSource.External or ProposalSource.Hazard or
                ProposalSource.TankFear or ProposalSource.ActionSafety);
        if (!watchdogMayOwnMovement)
        {
            ResetMovementWatch(state);
            return;
        }

        var eligible = !soldier.IsOnVehicle() && HasCommittedDestination(soldier);
        var monitor = eligible && ai.moveCharacter;

        if (now < state.MovementStallHoldUntil)
        {
            StopDangerMovement(ai, soldier, Time.deltaTime);
            return;
        }

        if (state.MovementStallHoldUntil > 0f)
        {
            state.MovementStallHoldUntil = 0f;
            ResetMovementWatch(state, preserveFailures: true);
            // Only resume the progress watch if the arbiter actually released him: a
            // soldier still owned by a higher hold would otherwise be monitored while
            // legitimately stationary and re-arm the stall he just served.
            if (!eligible ||
                !MovementArbiterCore.Grants(ApplyMovementDecision(
                    ai, soldier, Time.deltaTime, now, MovementOwner.OrderedMove,
                    "stall-release")))
            {
                return;
            }

            monitor = true;
        }

        Vector3 destination;
        try
        {
            destination = ai.MoveDestination;
        }
        catch (NullReferenceException)
        {
            ResetMovementWatch(state);
            return;
        }
        catch (Il2CppException)
        {
            ResetMovementWatch(state);
            return;
        }
        catch (ObjectCollectedException)
        {
            ResetMovementWatch(state);
            return;
        }

        var destinationChanged = !state.MovementWatchActive ||
                                 state.MovementWatchSource != movementSource ||
                                 HorizontalDistance(
                                     destination, state.MovementWatchDestination) >=
                                 MovementProgressWatchdogCore.DestinationChangeMeters;
        var physicalTravel = state.MovementWatchActive
            ? HorizontalDistance(soldier.transform.position, state.MovementWatchPosition)
            : 0f;
        var decision = MovementProgressWatchdogCore.Evaluate(new MovementProgressInput(
            monitor,
            ai.HasPathRequest,
            destinationChanged,
            physicalTravel,
            state.MovementWatchActive ? now - state.MovementWatchLastProgressAt : 0f));

        switch (decision)
        {
            case MovementProgressDecision.Reset:
                ResetMovementWatch(state);
                return;
            case MovementProgressDecision.Progressed:
                state.MovementWatchActive = true;
                state.MovementWatchPosition = soldier.transform.position;
                state.MovementWatchDestination = destination;
                state.MovementWatchLastProgressAt = now;
                state.MovementWatchSource = movementSource;
                if (physicalTravel >= MovementProgressWatchdogCore.ProgressEpsilonMeters)
                {
                    state.MovementStallFailures = 0;
                    state.HasMovementStallDestination = false;
                    state.MovementStallDestination = default;
                }
                return;
            case MovementProgressDecision.Halt:
                var wasRelocating = state.Relocating;
                if (wasRelocating)
                {
                    FinishRelocation(
                        ai, soldier, state, soldier.GetInstanceID(), now,
                        keepOccupiedCover: false, completedMove: false);
                }
                BeginMovementStallHold(
                    ai,
                    soldier,
                    state,
                    now,
                    $"{movementSource} destination made no physical progress",
                    refreshPath: !wasRelocating && !ai.HasPathRequest);
                return;
            default:
                return;
        }
    }

    private static void BeginMovementStallHold(
        SoldierAI ai,
        Soldier soldier,
        ContactResponseState state,
        float now,
        string reason,
        bool refreshPath = false)
    {
        try
        {
            var blockedDestination = ai.MoveDestination;
            if (IsFinite(blockedDestination))
            {
                var sameBlockedDestination = state.HasMovementStallDestination &&
                    HorizontalDistance(
                        blockedDestination, state.MovementStallDestination) <
                    MovementProgressWatchdogCore.DestinationChangeMeters;
                if (!sameBlockedDestination)
                    state.MovementStallFailures = 0;
                state.HasMovementStallDestination = true;
                state.MovementStallDestination = blockedDestination;
            }
        }
        catch (NullReferenceException)
        {
            state.HasMovementStallDestination = false;
            state.MovementStallDestination = default;
        }
        catch (Il2CppException)
        {
            state.HasMovementStallDestination = false;
            state.MovementStallDestination = default;
        }
        catch (ObjectCollectedException)
        {
            state.HasMovementStallDestination = false;
            state.MovementStallDestination = default;
        }

        state.MovementStallFailures = Math.Min(3, state.MovementStallFailures + 1);
        var holdSeconds = MovementProgressWatchdogCore.RecoverySeconds(
            state.MovementStallFailures);
        state.MovementStallHoldUntil = now + holdSeconds;
        state.NextRelocationAllowedAt = Mathf.Max(
            state.NextRelocationAllowedAt, state.MovementStallHoldUntil);
        state.NextDecisionAt = Mathf.Max(
            state.NextDecisionAt, state.MovementStallHoldUntil);
        ResetMovementWatch(state, preserveFailures: true);
        StopDangerMovement(ai, soldier, Time.deltaTime);
        if (refreshPath)
            RefreshPath(ai, "Stalled locomotion path refresh failed");
        AiState.Trace(
            $"Movement watchdog: soldier {soldier.GetInstanceID()} stopped for {holdSeconds:0.0}s; {reason}");
    }

    private static void ResetMovementWatch(
        ContactResponseState state,
        bool preserveFailures = false)
    {
        state.MovementWatchActive = false;
        state.MovementWatchPosition = default;
        state.MovementWatchDestination = default;
        state.MovementWatchLastProgressAt = 0f;
        state.MovementWatchSource = ProposalSource.None;
        if (!preserveFailures)
        {
            state.MovementStallHoldUntil = 0f;
            state.MovementStallFailures = 0;
            state.HasMovementStallDestination = false;
            state.MovementStallDestination = default;
        }
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var x = first.x - second.x;
        var z = first.z - second.z;
        return Mathf.Sqrt(x * x + z * z);
    }

    internal static bool TryHoldMovementStall(
        SoldierAI ai,
        Soldier soldier,
        float now,
        float deltaTime)
    {
        var state = AiState.GetContactState(soldier.GetInstanceID());
        if (now >= state.MovementStallHoldUntil)
            return false;

        StopDangerMovement(ai, soldier, deltaTime);
        return true;
    }
}
