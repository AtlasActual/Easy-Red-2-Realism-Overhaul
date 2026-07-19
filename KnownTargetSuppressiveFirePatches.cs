using Corvostudio.Weapons;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class KnownTargetSuppressiveFireState
{
    internal bool Active;
    internal bool TriggerOwned;
    internal bool AimingOwned;
    internal bool BurstStarted;
    internal IntPtr TargetToken;
    internal IntPtr GunToken;
    internal Vector3 AimPoint;
    internal float ObservationAt;
    internal float LastConsumedObservationAt;
    internal float AimDeadlineAt;
    internal float BurstEndsAt;
    internal float NextAttemptAt;
}

internal static class KnownTargetSuppressiveFire
{
    private const float MinimumKnowledgeFreshSeconds = 5f;
    private const float PostMemoryKnowledgeSeconds = 2.5f;
    private const float AimTimeoutSeconds = 2.5f;
    private const float BurstSeconds = 1.1f;
    private const float BlockedRetrySeconds = 0.5f;

    internal static void Schedule(SoldierAI ai, Soldier soldier, float now)
    {
        var id = soldier.GetInstanceID();
        var state = GetState(id);
        if (!CanStart(ai, soldier, id, now, out var gun))
        {
            Cancel(ai, soldier, state, preserveNativeAim: HasUsableNativeTarget(ai, soldier));
            return;
        }

        if (state.Active || now < state.NextAttemptAt || HasUsableNativeTarget(ai, soldier))
            return;

        if (!AiState.TargetMemory.TryGetValue(id, out var memory) ||
            !memory.HasConfirmedLastKnownPosition ||
            memory.ConfirmedLastKnownTargetToken == IntPtr.Zero ||
            memory.ConfirmedLastKnownObservedAt <= state.LastConsumedObservationAt ||
            now - memory.ConfirmedLastKnownObservedAt > KnowledgeFreshSeconds())
        {
            return;
        }

        var aimPoint = memory.ConfirmedLastKnownPosition;
        var distance = Vector3.Distance(soldier.transform.position, aimPoint);
        if (distance <= 2f || distance > Settings.ContactEngagementHaltDistance.Value ||
            !TryGetBallisticDirection(
                ai, soldier, gun, aimPoint, out var origin, out var fireDirection))
        {
            return;
        }

        if (CombatSafety.FriendlyInFiringLane(
                soldier,
                origin,
                fireDirection,
                Settings.FriendlyFireLaneRadius.Value,
                distance + 2f))
        {
            state.NextAttemptAt = now + BlockedRetrySeconds;
            return;
        }

        // Consuming the observation when the attempt begins is deliberate. A failed
        // turn or interrupted burst must not recreate the old aim/cancel loop.
        state.Active = true;
        state.TriggerOwned = false;
        state.AimingOwned = false;
        state.BurstStarted = false;
        state.TargetToken = memory.ConfirmedLastKnownTargetToken;
        state.GunToken = gun.Pointer;
        state.AimPoint = aimPoint;
        state.ObservationAt = memory.ConfirmedLastKnownObservedAt;
        state.LastConsumedObservationAt = memory.ConfirmedLastKnownObservedAt;
        state.AimDeadlineAt = now + AimTimeoutSeconds;
        state.BurstEndsAt = 0f;
        AiState.Trace(
            $"Known-target suppression: soldier {id} began one MG attempt at a frozen observation");
    }

    internal static void FixedUpdate(SoldierAI ai)
    {
        Soldier? soldier = null;
        KnownTargetSuppressiveFireState? state = null;
        try
        {
            soldier = ai.GetSoldier();
            if (soldier == null)
                return;

            var id = soldier.GetInstanceID();
            if (!AiState.KnownTargetSuppressionStates.TryGetValue(id, out state) || !state.Active)
                return;

            var now = Time.time;
            if (!CanContinue(ai, soldier, id, state, now, out var gun) ||
                !TryGetBallisticDirection(
                    ai, soldier, gun, state.AimPoint, out var origin, out var fireDirection))
            {
                Cancel(ai, soldier, state, preserveNativeAim: HasUsableNativeTarget(ai, soldier));
                return;
            }

            var distance = Vector3.Distance(origin, state.AimPoint);
            if (CombatSafety.FriendlyInFiringLane(
                    soldier,
                    origin,
                    fireDirection,
                    Settings.FriendlyFireLaneRadius.Value,
                    distance + 2f))
            {
                Cancel(ai, soldier, state, preserveNativeAim: false);
                return;
            }

            ai.moveLookingTarget = true;
            ai.fireDir = fireDirection;
            soldier.RotateToward(fireDirection, Time.fixedDeltaTime);
            if (!soldier.IsAiming)
            {
                soldier.SetAiming(true);
                state.AimingOwned = true;
            }

            if (!state.BurstStarted)
            {
                if (now >= state.AimDeadlineAt)
                {
                    Cancel(ai, soldier, state, preserveNativeAim: false);
                    return;
                }

                if (!soldier.CanPointAndFire() || !soldier.pointingGun ||
                    !ai.IaAimingAt(state.AimPoint))
                    return;

                state.BurstStarted = true;
                state.BurstEndsAt = now + BurstSeconds;
            }

            if (now >= state.BurstEndsAt)
            {
                Cancel(ai, soldier, state, preserveNativeAim: false);
                return;
            }

            if (!soldier.CanPointAndFire() || !soldier.pointingGun)
            {
                ReleaseOwnedTrigger(soldier, state);
                return;
            }

            soldier.SetGunTriggerPressed(true);
            state.TriggerOwned = true;
        }
        catch (Exception ex)
        {
            if (soldier != null && state != null)
                Cancel(ai, soldier, state, preserveNativeAim: false);
            Plugin.LogSource.LogWarning($"Known-target suppressive fire failed: {ex.Message}");
        }
    }

    internal static bool TryGetOwnedAimPoint(int soldierId, float now, out Vector3 aimPoint)
    {
        aimPoint = default;
        if (!AiState.KnownTargetSuppressionStates.TryGetValue(soldierId, out var state) ||
            !state.Active ||
            (!state.BurstStarted && now >= state.AimDeadlineAt) ||
            (state.BurstStarted && now >= state.BurstEndsAt))
        {
            return false;
        }

        aimPoint = state.AimPoint;
        return true;
    }

    internal static bool TryGetOwnedTarget(
        int soldierId,
        out IntPtr targetToken,
        out Vector3 targetPosition)
    {
        targetToken = IntPtr.Zero;
        targetPosition = default;
        if (!AiState.KnownTargetSuppressionStates.TryGetValue(soldierId, out var state) ||
            !state.Active || state.TargetToken == IntPtr.Zero)
        {
            return false;
        }

        targetToken = state.TargetToken;
        targetPosition = state.AimPoint;
        return true;
    }

    internal static bool OwnsAim(int soldierId, float now)
        => TryGetOwnedAimPoint(soldierId, now, out _);

    internal static void Disable(SoldierAI ai, Soldier soldier)
    {
        if (AiState.KnownTargetSuppressionStates.TryGetValue(
                soldier.GetInstanceID(), out var state))
        {
            Cancel(ai, soldier, state, preserveNativeAim: HasUsableNativeTarget(ai, soldier));
        }
    }

    internal static void InterruptForMovement(SoldierAI ai, Soldier soldier)
    {
        if (!AiState.KnownTargetSuppressionStates.TryGetValue(
                soldier.GetInstanceID(), out var state) || !state.Active)
        {
            return;
        }

        // This behavior owns a stationary machine-gun burst. Once locomotion
        // resumes, release its body-facing latch before native movement runs so
        // the soldier turns into the path instead of sliding sideways.
        Cancel(ai, soldier, state, preserveNativeAim: false);
        ai.moveLookingTarget = false;
    }

    internal static void RemoveSoldier(int soldierId)
        => AiState.KnownTargetSuppressionStates.Remove(soldierId);

    internal static void ResetBattle()
        => AiState.KnownTargetSuppressionStates.Clear();

    private static KnownTargetSuppressiveFireState GetState(int soldierId)
    {
        if (AiState.KnownTargetSuppressionStates.TryGetValue(soldierId, out var state))
            return state;

        state = new KnownTargetSuppressiveFireState();
        AiState.KnownTargetSuppressionStates[soldierId] = state;
        return state;
    }

    private static bool CanStart(
        SoldierAI ai,
        Soldier soldier,
        int soldierId,
        float now,
        out GenericGun gun)
    {
        gun = null!;
        if (!Settings.KnownTargetSuppressionEnabled.Value ||
            !Settings.ContactResponseEnabled.Value ||
            !Settings.PerceptionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() ||
            soldier == null || !soldier.IsAlive || !soldier.IsAI() ||
            soldier.IsFPSPlayer() || soldier.IsOnVehicle() || soldier.IsOnFire ||
            soldier.IsMoving(0.2f) || ContactResponse.IsActualCharge(soldier) ||
            ContactResponse.IsRelocating(soldierId) || ContactResponse.IsPinned(soldierId) ||
            IncomingFireAwareness.HasActiveCue(soldierId, now) ||
            AiState.IsFlameEvading(soldierId, now))
        {
            return false;
        }

        var contact = AiState.GetContactState(soldierId);
        if (contact.SuppressionMovementOwned || contact.SuppressionFireInhibited ||
            contact.FireInhibitedByRange)
        {
            return false;
        }

        gun = soldier.GetHeldGun();
        return gun != null && gun.Pointer != IntPtr.Zero &&
               HandheldWeaponClassifier.IsMachineGun(gun, false) && gun.IsFullAuto() &&
               !gun.IsReloadingOrUsingBolt() && !soldier.MustReload();
    }

    private static bool CanContinue(
        SoldierAI ai,
        Soldier soldier,
        int soldierId,
        KnownTargetSuppressiveFireState state,
        float now,
        out GenericGun gun)
    {
        if (!CanStart(ai, soldier, soldierId, now, out gun) ||
            gun.Pointer != state.GunToken || HasUsableNativeTarget(ai, soldier))
        {
            return false;
        }

        if (AiState.GetContactState(soldierId).FireInhibitedByMovement)
            return false;

        return AiState.TargetMemory.TryGetValue(soldierId, out var memory) &&
               memory.HasConfirmedLastKnownPosition &&
               memory.ConfirmedLastKnownTargetToken == state.TargetToken &&
               Mathf.Abs(memory.ConfirmedLastKnownObservedAt - state.ObservationAt) < 0.001f &&
               now - state.ObservationAt <= KnowledgeFreshSeconds();
    }

    private static float KnowledgeFreshSeconds()
        => Mathf.Max(
            MinimumKnowledgeFreshSeconds,
            Settings.TargetMemorySeconds.Value + PostMemoryKnowledgeSeconds);

    private static bool HasUsableNativeTarget(SoldierAI ai, Soldier soldier)
        => TargetAcquisition.GetUsableAiTarget(ai) != null ||
           TargetAcquisition.GetUsableSoldierTarget(soldier) != null;

    private static bool TryGetBallisticDirection(
        SoldierAI ai,
        Soldier soldier,
        GenericGun gun,
        Vector3 targetPosition,
        out Vector3 origin,
        out Vector3 direction)
    {
        origin = gun.GetBulletGenerationPosition();
        direction = targetPosition - origin;
        var distance = direction.magnitude;
        if (distance <= 0.01f)
            return false;

        var elevation = BulletInstance.CalculateTrajectory(
            distance,
            soldier.GetBulletSpeed(),
            soldier.GetBulletGravity(),
            out var canReach);
        if (!canReach)
            return false;

        direction = Quaternion.AngleAxis(-elevation, ai.transform.right) * direction.normalized;
        return direction.sqrMagnitude > 0.001f;
    }

    private static void ReleaseOwnedTrigger(
        Soldier soldier,
        KnownTargetSuppressiveFireState state)
    {
        if (!state.TriggerOwned)
            return;

        soldier.SetGunTriggerPressed(false);
        state.TriggerOwned = false;
    }

    private static void Cancel(
        SoldierAI ai,
        Soldier soldier,
        KnownTargetSuppressiveFireState state,
        bool preserveNativeAim)
    {
        if (!state.Active)
            return;

        ReleaseOwnedTrigger(soldier, state);

        if (state.AimingOwned && !preserveNativeAim && soldier.IsAiming)
            soldier.SetAiming(false);

        state.Active = false;
        state.AimingOwned = false;
        state.BurstStarted = false;
        state.TargetToken = IntPtr.Zero;
        state.GunToken = IntPtr.Zero;
        state.AimPoint = default;
        state.ObservationAt = 0f;
        state.AimDeadlineAt = 0f;
        state.BurstEndsAt = 0f;
    }
}

[HarmonyPatch(typeof(SoldierAI), "FixedUpdate")]
internal static class KnownTargetSuppressiveFireFixedUpdatePatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(SoldierAI __instance)
    {
        if (__instance != null)
            KnownTargetSuppressiveFire.FixedUpdate(__instance);
    }
}
