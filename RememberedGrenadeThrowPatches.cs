using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class RememberedGrenadeThrowState
{
    internal bool Active;
    internal bool ThrowStarted;
    internal IntPtr TargetToken;
    internal IntPtr GrenadeToken;
    internal Vector3 AimPoint;
    internal Quaternion ThrowLookRotation;
    internal float ObservationAt;
    internal float LastConsumedObservationAt;
    internal float AimDeadlineAt;
    internal float ThrowDeadlineAt;
    internal float NextAttemptAt;
}

internal static class RememberedGrenadeThrows
{
    private const float AimTimeoutSeconds = 2f;
    private const float ThrowAnimationTimeoutSeconds = 3.5f;
    private const float BlockedRetrySeconds = 0.75f;
    private const float FacingToleranceDegrees = 12f;
    private const int ArcSegments = 7;
    private const float EndpointTolerance = 0.55f;
    private static readonly RaycastHit[] ArcHits = new RaycastHit[24];

    internal static void Schedule(SoldierAI ai, Soldier soldier, float now)
    {
        var id = soldier.GetInstanceID();
        var systemEnabled = Settings.SafeAiGrenadeThrowsEnabled.Value &&
                            Settings.PerceptionEnabled.Value;
        var hasAuthority = MultiplayerAuthority.CanMutateGameplay();
        var autonomous = AiOwnership.IsAutonomous(soldier);
        var alive = soldier.IsAlive;
        var stationary = !soldier.IsMoving(0.2f);
        var availableForThrow = !soldier.IsOnVehicle() &&
                                !soldier.IsOnFire &&
                                !soldier.IsThrowing;
        if (!systemEnabled || !hasAuthority || !autonomous || !alive ||
            !stationary || !availableForThrow)
        {
            if (AiState.RememberedGrenadeThrowStates.TryGetValue(id, out var activeState) &&
                activeState.Active)
            {
                Cancel(ai, soldier, activeState);
            }
            return;
        }

        var state = GetState(id);
        if (state.Active || now < state.NextAttemptAt)
            return;

        if (!TryGetFreshMemory(soldier, id, now, out var memory, out var memorySeconds) ||
            memory.ConfirmedLastKnownObservedAt <= state.LastConsumedObservationAt)
        {
            return;
        }

        var grenade = FindFragmentationGrenade(soldier);
        var hasDirectSight = HasActualVisualTarget(ai, soldier);
        if (hasDirectSight)
            return;

        var targetPosition = memory.ConfirmedLastKnownPosition;
        var distance = Vector3.Distance(soldier.GetCenterOfUnit(), targetPosition);
        var withinRange = distance >= Settings.GrenadeMinimumRange.Value &&
                          distance <= Settings.GrenadeMaximumRange.Value;
        var blastAreaClear = !CombatSafety.FriendlyNear(
            targetPosition,
            AiState.FactionOf(soldier),
            Settings.GrenadeFriendlySafetyRadius.Value,
            soldier);
        var hasClearArc = TryBuildThrowArc(
            soldier, targetPosition, out var throwLookRotation);
        var input = new RememberedGrenadeDecisionInput(
            systemEnabled,
            hasAuthority,
            autonomous,
            alive,
            stationary,
            availableForThrow,
            grenade != null && grenade.Pointer != IntPtr.Zero,
            memory.HasConfirmedTarget,
            memory.TargetToken != IntPtr.Zero &&
            memory.TargetToken == memory.ConfirmedLastKnownTargetToken,
            now - memory.ConfirmedLastKnownObservedAt,
            memorySeconds,
            hasDirectSight,
            withinRange,
            blastAreaClear,
            hasClearArc);

        if (!RememberedGrenadeDecisionCore.ShouldAttempt(input) ||
            !AiState.CooldownReady(AiState.NextGrenadeThrow, id, now))
        {
            if (!hasClearArc || !blastAreaClear)
                state.NextAttemptAt = now + BlockedRetrySeconds;
            return;
        }

        // Consume the frozen observation when the attempt begins. An interrupted
        // turn must not make the soldier repeatedly retry the same stale location.
        state.Active = true;
        state.ThrowStarted = false;
        state.TargetToken = memory.ConfirmedLastKnownTargetToken;
        state.GrenadeToken = grenade!.Pointer;
        state.AimPoint = targetPosition;
        state.ThrowLookRotation = throwLookRotation;
        state.ObservationAt = memory.ConfirmedLastKnownObservedAt;
        state.LastConsumedObservationAt = memory.ConfirmedLastKnownObservedAt;
        state.AimDeadlineAt = now + AimTimeoutSeconds;
        state.ThrowDeadlineAt = 0f;
        KnownTargetSuppressiveFire.Disable(ai, soldier);
        AiState.Trace(
            $"Remembered grenade: soldier {id} began one throw at a frozen last-known position");
    }

    internal static void FixedUpdate(SoldierAI ai)
    {
        Soldier? soldier = null;
        RememberedGrenadeThrowState? state = null;
        try
        {
            soldier = ai.GetSoldier();
            if (soldier == null)
                return;

            var id = soldier.GetInstanceID();
            if (!AiState.RememberedGrenadeThrowStates.TryGetValue(id, out state) || !state.Active)
                return;

            var now = Time.time;
            if (state.ThrowStarted)
            {
                // The grenade has already been primed. Preserve only its frozen
                // release direction; do not turn a soldier who starts moving.
                if (!soldier.IsThrowing || now >= state.ThrowDeadlineAt)
                    Cancel(ai, soldier, state);
                return;
            }

            if (!Settings.SafeAiGrenadeThrowsEnabled.Value ||
                !Settings.PerceptionEnabled.Value ||
                !soldier.IsAlive ||
                !AiOwnership.IsAutonomous(soldier) ||
                !MultiplayerAuthority.CanMutateGameplay() ||
                now >= state.AimDeadlineAt ||
                soldier.IsMoving(0.2f) ||
                soldier.IsOnVehicle() ||
                soldier.IsOnFire ||
                soldier.IsThrowing ||
                HasActualVisualTarget(ai, soldier) ||
                !TryGetFreshMemory(soldier, id, now, out var memory, out _) ||
                memory.ConfirmedLastKnownTargetToken != state.TargetToken ||
                Mathf.Abs(memory.ConfirmedLastKnownObservedAt - state.ObservationAt) > 0.001f)
            {
                Cancel(ai, soldier, state);
                return;
            }

            var facingDirection = state.AimPoint - soldier.transform.position;
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.01f)
            {
                Cancel(ai, soldier, state);
                return;
            }

            ai.moveLookingTarget = true;
            ai.fireDir = state.AimPoint - soldier.LookPosition();
            soldier.RotateToward(facingDirection, Time.fixedDeltaTime);
            if (Vector3.Angle(soldier.transform.forward, facingDirection) >
                FacingToleranceDegrees)
            {
                return;
            }

            var grenade = FindFragmentationGrenade(soldier);
            if (grenade == null || grenade.Pointer != state.GrenadeToken ||
                !TryAuthorizeOwnedThrow(soldier, id, grenade, out _))
            {
                Cancel(ai, soldier, state);
                return;
            }

            soldier.Throw(grenade);
            if (!soldier.IsThrowing)
            {
                Cancel(ai, soldier, state);
                return;
            }

            state.ThrowStarted = true;
            state.ThrowDeadlineAt = now + ThrowAnimationTimeoutSeconds;
        }
        catch (NullReferenceException)
        {
            if (soldier != null && state != null)
                Cancel(ai, soldier, state);
        }
        catch (Il2CppException)
        {
            if (soldier != null && state != null)
                Cancel(ai, soldier, state);
        }
        catch (ObjectCollectedException)
        {
            if (soldier != null && state != null)
                Cancel(ai, soldier, state);
        }
    }

    internal static bool IsActive(int soldierId)
        => AiState.RememberedGrenadeThrowStates.TryGetValue(soldierId, out var state) &&
           state.Active;

    internal static bool TryAuthorizeOwnedThrow(
        Soldier soldier,
        int soldierId,
        VirtualItem item,
        out Vector3 targetPosition)
    {
        targetPosition = default;
        if (!AiState.RememberedGrenadeThrowStates.TryGetValue(soldierId, out var state) ||
            !state.Active ||
            state.TargetToken == IntPtr.Zero ||
            item is not VirtualGrenade ||
            item.Pointer != state.GrenadeToken ||
            !TryBuildThrowArc(soldier, state.AimPoint, out var throwLookRotation))
        {
            return false;
        }

        state.ThrowLookRotation = throwLookRotation;
        targetPosition = state.AimPoint;
        return true;
    }

    internal static bool TryGetOwnedLookRotation(
        Soldier soldier,
        out Quaternion lookRotation)
    {
        lookRotation = default;
        if (!AiState.RememberedGrenadeThrowStates.TryGetValue(
                soldier.GetInstanceID(), out var state) ||
            !state.Active || !state.ThrowStarted)
        {
            return false;
        }

        lookRotation = state.ThrowLookRotation;
        return true;
    }

    internal static void Disable(SoldierAI ai, Soldier soldier)
    {
        if (AiState.RememberedGrenadeThrowStates.TryGetValue(
                soldier.GetInstanceID(), out var state))
        {
            Cancel(ai, soldier, state);
        }
    }

    internal static void RemoveSoldier(int soldierId)
        => AiState.RememberedGrenadeThrowStates.Remove(soldierId);

    internal static void ResetBattle()
        => AiState.RememberedGrenadeThrowStates.Clear();

    private static RememberedGrenadeThrowState GetState(int soldierId)
    {
        if (AiState.RememberedGrenadeThrowStates.TryGetValue(soldierId, out var state))
            return state;

        state = new RememberedGrenadeThrowState();
        AiState.RememberedGrenadeThrowStates[soldierId] = state;
        return state;
    }

    private static VirtualGrenade? FindFragmentationGrenade(Soldier soldier)
    {
        try
        {
            return soldier.inventory?.FindItemOfType<VirtualGrenade>() as VirtualGrenade;
        }
        catch (NullReferenceException)
        {
            return null;
        }
        catch (Il2CppException)
        {
            return null;
        }
        catch (ObjectCollectedException)
        {
            return null;
        }
    }

    private static bool TryGetFreshMemory(
        Soldier soldier,
        int soldierId,
        float now,
        out TargetMemoryState memory,
        out float memorySeconds)
    {
        memorySeconds = AiBehaviorTuning.TargetMemorySeconds *
                        Mathf.Lerp(
                            1f,
                            Settings.SuppressedMemoryMultiplier.Value,
                            TargetAcquisition.Suppression(soldier));
        if (!AiState.TargetMemory.TryGetValue(soldierId, out var found))
        {
            memory = null!;
            return false;
        }

        memory = found;
        return memory.HasConfirmedTarget &&
               memory.HasConfirmedLastKnownPosition &&
               memory.TargetToken != IntPtr.Zero &&
               memory.TargetToken == memory.ConfirmedLastKnownTargetToken &&
               memory.ConfirmedLastKnownObservedAt > 0f &&
               now - memory.ConfirmedLastKnownObservedAt <= memorySeconds;
    }

    private static bool HasActualVisualTarget(SoldierAI ai, Soldier soldier)
    {
        var aiTarget = TargetAcquisition.GetUsableAiTarget(ai);
        if (CanActuallySee(soldier, aiTarget))
            return true;

        return CanActuallySee(
            soldier,
            TargetAcquisition.GetUsableSoldierTarget(soldier));
    }

    private static bool CanActuallySee(Soldier soldier, Spottable? target)
    {
        if (!TargetAcquisition.IsUsableTarget(target))
            return false;

        try
        {
            return soldier.CanSee(target);
        }
        catch
        {
            // Visibility uncertainty must never create a blind grenade throw.
            return true;
        }
    }

    private static bool TryBuildThrowArc(
        Soldier soldier,
        Vector3 targetPosition,
        out Quaternion throwLookRotation)
    {
        throwLookRotation = default;
        var origin = soldier.rightHand != null
            ? soldier.rightHand.position
            : soldier.LookPosition();
        var direct = targetPosition - origin;
        if (direct.sqrMagnitude <= 0.01f)
            return false;

        throwLookRotation = Quaternion.LookRotation(direct.normalized, soldier.transform.up);
        var horizontal = new Vector2(direct.x, direct.z).magnitude;
        var apex = Mathf.Max(origin.y, targetPosition.y) +
                   Mathf.Clamp(horizontal * 0.18f, 1.2f, 3.8f);
        var control = (origin + targetPosition) * 0.5f;
        control.y = 2f * apex - 0.5f * (origin.y + targetPosition.y);

        var previous = origin;
        for (var segment = 1; segment <= ArcSegments; segment++)
        {
            var t = segment / (float)ArcSegments;
            var inverse = 1f - t;
            var next = inverse * inverse * origin +
                       2f * inverse * t * control +
                       t * t * targetPosition;
            if (!ArcSegmentClear(soldier, previous, next, targetPosition))
                return false;
            previous = next;
        }

        return true;
    }

    private static bool ArcSegmentClear(
        Soldier soldier,
        Vector3 start,
        Vector3 end,
        Vector3 targetPosition)
    {
        var direction = end - start;
        var distance = direction.magnitude;
        if (distance <= 0.01f)
            return true;

        var hitCount = Physics.RaycastNonAlloc(
            start,
            direction / distance,
            ArcHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (var i = 0; i < hitCount; i++)
        {
            var collider = ArcHits[i].collider;
            if (collider == null)
                continue;

            var hitSoldier = collider.GetComponentInParent<Soldier>();
            if (hitSoldier == soldier)
                continue;

            if ((ArcHits[i].point - targetPosition).sqrMagnitude <=
                EndpointTolerance * EndpointTolerance)
            {
                continue;
            }

            return false;
        }

        // A full buffer means there may be an unexamined obstacle.
        return hitCount < ArcHits.Length;
    }

    private static void Cancel(
        SoldierAI ai,
        Soldier soldier,
        RememberedGrenadeThrowState state)
    {
        if (!state.Active)
            return;

        state.Active = false;
        state.ThrowStarted = false;
        state.TargetToken = IntPtr.Zero;
        state.GrenadeToken = IntPtr.Zero;
        state.AimPoint = default;
        state.ThrowLookRotation = default;
        state.ObservationAt = 0f;
        state.AimDeadlineAt = 0f;
        state.ThrowDeadlineAt = 0f;

        var id = soldier.GetInstanceID();
        if (!KnownTargetSuppressiveFire.OwnsAim(id, Time.time) &&
            TargetAcquisition.GetUsableAiTarget(ai) == null &&
            TargetAcquisition.GetUsableSoldierTarget(soldier) == null)
        {
            ai.moveLookingTarget = false;
        }
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.LookRotation))]
internal static class RememberedGrenadeLookRotationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance, ref Quaternion __result)
    {
        if (!RememberedGrenadeThrows.TryGetOwnedLookRotation(__instance, out __result))
            return true;

        return false;
    }
}
