using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal static class MountedGunnerSuppression
{
    private sealed class State
    {
        internal IntPtr SeatToken;
        internal bool Ducked;
        internal bool DuckPoseApplied;
        internal bool FireInhibitedBySuppression;
        internal float DuckUntil;
        internal float FireAllowedAt;
        internal float NextUpdateAt;
    }

    private static readonly Dictionary<int, State> States = new();

    internal static void Update(SoldierAI ai)
    {
        if (!MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        var soldier = ai.GetSoldier();
        if (soldier == null || !soldier.IsAlive || !soldier.IsAI() || soldier.IsFPSPlayer())
            return;

        var id = soldier.GetInstanceID();
        var seat = soldier.currentVehicleSeat;
        if (!Settings.MountedGunnerSuppressionEnabled.Value)
        {
            if (States.TryGetValue(id, out var disabledState))
            {
                // Release the fire permission before trying to raise the gunner.
                // If the native pose call throws, the next update can retry it
                // without leaving fire permanently disabled.
                ReleaseFireInhibition(ai, soldier, disabledState);
                if (disabledState.Ducked &&
                    IsSameCrouchableTurretSeat(soldier, seat, disabledState.SeatToken))
                {
                    RaiseFromSuppression(soldier, disabledState);
                }
            }

            States.Remove(id);
            return;
        }

        if (States.TryGetValue(id, out var state))
        {
            // SetPoseVehicle(false) makes IsUsingSeatTurret false, so continuing
            // eligibility must use stable seat identity instead of the current pose.
            if (!IsSameCrouchableTurretSeat(soldier, seat, state.SeatToken))
            {
                ReleaseFireInhibition(ai, soldier, state);
                States.Remove(id);
                return;
            }
        }
        else
        {
            // IsUsingSeatTurret is useful only on initial entry, while the gunner
            // is still in the native raised pose. Capture the seat before ducking.
            if (!TryGetInitialCrouchableTurretSeat(soldier, seat, out var seatToken))
                return;

            state = new State { SeatToken = seatToken };
            States[id] = state;
        }

        var now = Time.time;
        if (now < state.NextUpdateAt)
            return;
        state.NextUpdateAt = now + 0.1f;

        // Sequential infantry logic stops while mounted. Clear any fire/movement
        // ownership carried into the seat before evaluating turret suppression.
        ContactResponse.SuspendForVehicle(ai, soldier);

        var suppression = soldier.GetSuppressionValue();
        if (!state.Ducked)
        {
            if (suppression < Settings.MountedGunnerDuckSuppression.Value)
                return;

            state.Ducked = true;
            state.DuckPoseApplied = false;
            state.FireInhibitedBySuppression = true;
            state.DuckUntil = now + Settings.MountedGunnerMinimumDuckSeconds.Value;
            state.FireAllowedAt = float.PositiveInfinity;
            soldier.StopFire();
            StopMountedGun(seat, soldier);
            ai.allowFireAtEnemy = false;
            ai.aimingEnemy = false;
            soldier.SetPoseVehicle(false);
            state.DuckPoseApplied = true;
            AiState.Trace($"Mounted gunner suppression: soldier {id} ducked at {suppression}");
            return;
        }

        // Keep the native trigger quiet while the gunner is below the shield. The
        // Shoot prefix below is a second guard against turret AI restarting a burst.
        state.FireInhibitedBySuppression = true;
        soldier.StopFire();
        StopMountedGun(seat, soldier);
        ai.allowFireAtEnemy = false;
        ai.aimingEnemy = false;
        if (!state.DuckPoseApplied)
        {
            soldier.SetPoseVehicle(false);
            state.DuckPoseApplied = true;
        }

        var releaseThreshold = Mathf.Min(
            Settings.MountedGunnerRiseSuppression.Value,
            Settings.MountedGunnerDuckSuppression.Value - 1);
        if (now < state.DuckUntil || suppression > releaseThreshold)
            return;

        RaiseFromSuppression(soldier, state);
        state.DuckPoseApplied = false;
        state.FireAllowedAt = now + Settings.MountedGunnerRiseSettleSeconds.Value;
        // The Shoot prefix continues to enforce settle time after permission is
        // released, so no persistent AI flag has to remain latched meanwhile.
        ReleaseFireInhibition(ai, soldier, state);
        AiState.Trace($"Mounted gunner suppression: soldier {id} rose at {suppression}");
    }

    internal static bool IsFireInhibited(int soldierId)
        => States.TryGetValue(soldierId, out var state) && state.FireInhibitedBySuppression;

    private static void ReleaseFireInhibition(SoldierAI ai, Soldier soldier, State state)
    {
        if (!state.FireInhibitedBySuppression)
            return;

        var contactState = AiState.GetContactState(soldier.GetInstanceID());
        contactState.FireRestorePending = true;
        state.FireInhibitedBySuppression = false;
        ContactResponse.RestoreFireAfterOwnedInhibition(ai, soldier);
    }

    internal static bool CanFire(Soldier soldier, float now)
    {
        if (soldier == null || !States.TryGetValue(soldier.GetInstanceID(), out var state))
            return true;

        // A state awaiting its next FixedUpdate must never block a newly occupied
        // seat. Cleanup will release the old AI permission latch on that update.
        if (!IsSameCrouchableTurretSeat(soldier, soldier.currentVehicleSeat, state.SeatToken))
            return true;

        return !state.Ducked && now >= state.FireAllowedAt;
    }

    internal static bool ShouldBlockStandingPose(Soldier soldier)
    {
        if (!Settings.MountedGunnerSuppressionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay() || soldier == null ||
            !States.TryGetValue(soldier.GetInstanceID(), out var state) || !state.Ducked)
        {
            return false;
        }

        return IsSameCrouchableTurretSeat(soldier, soldier.currentVehicleSeat, state.SeatToken);
    }

    private static void RaiseFromSuppression(Soldier soldier, State state)
    {
        // Native standing requests are rejected while Ducked is true. Release only
        // this latch for our own raise and restore it if the native pose call fails.
        state.Ducked = false;
        try
        {
            soldier.SetPoseVehicle(true);
        }
        catch
        {
            state.Ducked = true;
            throw;
        }
    }

    private static void StopMountedGun(Seats? seat, Soldier soldier)
    {
        try
        {
            var turret = seat?.connectedTurret;
            if (turret == null)
                return;

            // The seat exposes the general turret base. Releasing its trigger uses
            // native virtual dispatch for multi-weapon vehicle turrets; static MGs
            // also expose TurretGun directly, where the cycle latch can be cleared.
            turret.SetGunTriggerPressed(false, soldier);
            var gun = turret.TryCast<TurretGun>();
            if (gun != null)
            {
                gun.StopFire();
                gun.IsFiringCycled = false;
            }
        }
        catch
        {
            // The Shoot prefix remains a second guard if the seat is tearing down.
        }
    }

    private static bool TryGetInitialCrouchableTurretSeat(
        Soldier soldier,
        Seats? seat,
        out IntPtr seatToken)
    {
        seatToken = IntPtr.Zero;
        try
        {
            if (!soldier.IsOnVehicle() || !soldier.IsUsingSeatTurret || seat == null ||
                !seat.hasTurret || seat.seat == null || !seat.seat.canCrouch)
            {
                return false;
            }

            seatToken = seat.Pointer;
            return seatToken != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSameCrouchableTurretSeat(
        Soldier soldier,
        Seats? seat,
        IntPtr expectedSeatToken)
    {
        if (expectedSeatToken == IntPtr.Zero)
            return false;

        try
        {
            return soldier.IsOnVehicle() && seat != null &&
                   seat.Pointer == expectedSeatToken && seat.hasTurret &&
                   seat.seat != null && seat.seat.canCrouch;
        }
        catch
        {
            return false;
        }
    }

    internal static void RemoveSoldier(int soldierId)
        => States.Remove(soldierId);

    internal static void ResetBattle()
        => States.Clear();
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.SetPoseVehicle))]
internal static class DuckedMountedGunnerPosePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Soldier __instance, bool standing)
        => !standing || !MountedGunnerSuppression.ShouldBlockStandingPose(__instance);
}

[HarmonyPatch(typeof(SoldierAI), "FixedUpdate")]
internal static class MountedGunnerSuppressionUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(SoldierAI __instance)
    {
        try
        {
            MountedGunnerSuppression.Update(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Mounted gunner suppression update failed: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(TurretGun), nameof(TurretGun.Shoot))]
internal static class SuppressedMountedGunnerFirePatch
{
    [HarmonyPrefix]
    private static bool Prefix(TurretGun __instance, Creature user)
    {
        if (!Settings.MountedGunnerSuppressionEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return true;
        }

        var soldier = user?.TryCast<Soldier>();
        if (soldier == null || !soldier.IsAI() || soldier.IsFPSPlayer() ||
            MountedGunnerSuppression.CanFire(soldier, Time.time))
        {
            return true;
        }

        // Cycled turrets recursively call Shoot. If a recursive call is rejected
        // without clearing the cycle latch, the gun may remain stuck after rising.
        __instance.StopFire();
        __instance.IsFiringCycled = false;
        return false;
    }
}
